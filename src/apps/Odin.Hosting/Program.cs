using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Odin.Core.Configuration;
using Odin.Core.Exceptions;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Logging.CorrelationId.Serilog;
using Odin.Core.Logging.Hostname;
using Odin.Core.Logging.Hostname.Serilog;
using Odin.Core.Logging.LogLevelOverwrite.Serilog;
using Odin.Core.Logging.Statistics.Serilog;
using Odin.Core.Storage.SQLite.Migrations;
using Odin.Services.Certificate;
using Odin.Services.Configuration;
using Odin.Services.Registry;
using Odin.Services.Registry.Registration;
using Odin.Services.Tenant.Container;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;

namespace Odin.Hosting
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            var (didHandle, exitCode) = HandleCommandLineArgs(args);
            if (didHandle)
            {
                return exitCode;
            }

            //
            // Web host
            //
            {
                var (odinConfig, appSettingsConfig) = LoadConfig(true);
                Log.Logger = CreateLogger(appSettingsConfig, odinConfig).CreateBootstrapLogger();
                try
                {
                    Log.Information("Starting web host");
                    Log.Information("Identity-host version: {Version}", Extensions.Version.VersionText);
                    CreateHostBuilder(args).Build().Run();
                    Log.Information("Stopped web host\n\n\n");
                }
                catch (Exception ex)
                {
                    Log.Fatal(ex, "Host terminated unexpectedly");
                    return 1;
                }
                finally
                {
                    Log.CloseAndFlush();
                }

                return 0;
            }
        }

        //

        private static (OdinConfiguration, IConfiguration) LoadConfig(bool includeEnvVars)
        {
            var configFolder = Environment.GetEnvironmentVariable("ODIN_CONFIG_PATH") ?? Directory.GetCurrentDirectory();
            var aspNetCoreEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? Environments.Production;
            var configSources = new List<string>();
            var configBuilder = new ConfigurationBuilder();

            void AddConfigFile(string fileName)
            {
                var appSettingsFile = Path.Combine(configFolder, fileName);
                if (File.Exists(appSettingsFile))
                {
                    configSources.Insert(0, appSettingsFile);
                    configBuilder.AddJsonFile(appSettingsFile, optional: true, reloadOnChange: false);
                }
            }

            AddConfigFile("appsettings.json"); // Common env configuration
            AddConfigFile($"appsettings.{aspNetCoreEnv.ToLower()}.json"); // Specific env configuration
            AddConfigFile("appsettings.local.json"); // Local development overrides

            // Environment variables configuration
            if (includeEnvVars)
            {
                configBuilder.AddEnvironmentVariables();
                configSources.Insert(0, "environment variables");
            }

            try
            {
                var config = configBuilder.Build();
                return (new OdinConfiguration(config), config);
            }
            catch (Exception e)
            {
                var text = $"{e.Message} - check config sources in this order: {string.Join(", ", configSources)}";
                throw new Exception(text, e);
            }
        }

        //

        private static LoggerConfiguration CreateLogger(
            IConfiguration configuration,
            OdinConfiguration odinConfig,
            IServiceProvider services = null,
            LoggerConfiguration loggerConfig = null)
        {
            const string logOutputTemplate = // Add {SourceContext} to see source
                "{Timestamp:o} {Level:u3} {CorrelationId} {Hostname} {Message:lj}{NewLine}{Exception}";
            var logOutputTheme = SystemConsoleTheme.Literate;

            loggerConfig ??= new LoggerConfiguration();

            loggerConfig
                .ReadFrom.Configuration(configuration)
                .Enrich.FromLogContext()
                .Enrich.WithHostname(new StickyHostnameGenerator())
                .Enrich.WithCorrelationId(new CorrelationUniqueIdGenerator())
                .WriteTo.LogLevelModifier(s => s.Async(
                    sink => sink.Console(outputTemplate: logOutputTemplate, theme: logOutputTheme)));

            if (odinConfig.Logging.LogFilePath != "")
            {
                loggerConfig.WriteTo.LogLevelModifier(s => s.Async(
                    sink => sink.RollingFile(Path.Combine(odinConfig.Logging.LogFilePath, "app-{Date}.log"),
                        outputTemplate: logOutputTemplate)));
            }

            if (services != null)
            {
                loggerConfig.ReadFrom.Services(services);
                if (odinConfig.Logging.EnableStatistics)
                {
                    var store = services.GetRequiredService<ILogEventMemoryStore>();
                    var sink = new InMemorySink(store);
                    loggerConfig.WriteTo.Sink(sink);
                }
            }

            return loggerConfig;
        }

        //

        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            var (odinConfig, appSettingsConfig) = LoadConfig(true);

            if (odinConfig.Logging.LogFilePath != "")
            {
                var loggingDirInfo = Directory.CreateDirectory(odinConfig.Logging.LogFilePath);
                if (!loggingDirInfo.Exists)
                {
                    throw new OdinSystemException($"Could not create logging folder at [{odinConfig.Logging.LogFilePath}]");
                }
            }

            var dataRootDirInfo = Directory.CreateDirectory(odinConfig.Host.TenantDataRootPath);
            if (!dataRootDirInfo.Exists)
            {
                throw new OdinSystemException($"Could not create logging folder at [{odinConfig.Host.TenantDataRootPath}]");
            }

            Log.Information($"Root path:{odinConfig.Host.TenantDataRootPath}");

            var builder = Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration(builder => { builder.AddConfiguration(appSettingsConfig); })
                .UseSerilog((context, services, loggerConfiguration) =>
                {
                    CreateLogger(context.Configuration, odinConfig, services, loggerConfiguration);
                })
                .UseServiceProviderFactory(
                    new MultiTenantServiceProviderFactory(
                        TenantServices.ConfigureMultiTenantServices,
                        TenantServices.InitializeTenant))
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.ConfigureKestrel(kestrelOptions =>
                        {
                            kestrelOptions.Limits.MaxRequestBodySize = null;

                            foreach (var address in odinConfig.Host.IPAddressListenList)
                            {
                                var ip = address.GetIp();
                                kestrelOptions.Listen(ip, address.HttpPort);
                                kestrelOptions.Listen(ip, address.HttpsPort,
                                    options => ConfigureHttpListenOptions(odinConfig, kestrelOptions, options));
                            }

                            // Admin API
                            var reservedHttpsPorts = odinConfig.Host.IPAddressListenList.Select(x => x.HttpsPort);
                            if (odinConfig.Admin.ApiEnabled && !reservedHttpsPorts.Contains(odinConfig.Admin.ApiPort))
                            {
                                kestrelOptions.Listen(IPAddress.Any, odinConfig.Admin.ApiPort,
                                    options => ConfigureHttpListenOptions(odinConfig, kestrelOptions, options));
                            }
                        })
                        .UseStartup(context => new Startup(context.Configuration, args));
                });

            return builder;
        }

        //

        private static void ConfigureHttpListenOptions(
            OdinConfiguration odinConfig,
            KestrelServerOptions kestrelOptions,
            ListenOptions listenOptions)
        {
            var handshakeTimeoutTimeSpan = Debugger.IsAttached
                ? TimeSpan.FromMinutes(60)
                : TimeSpan.FromSeconds(60);

            // SEB:NOTE
            // We get a strange error on MacOS when using http/2 when running debug build and/or frontend proxy.
            // It doesn't happen on Linux in production.
            //   https://github.com/dotnet/aspnetcore/issues/8843
            //   https://github.com/dotnet/aspnetcore/issues/43625
            //
            // Repro:
            //   $ curl -v --http2 https://frodo.dotyou.cloud
            //
            // Below: override the default protocols to troubleshoot:
            if (odinConfig.Host.Http1Only)
            {
                Log.Warning("Limiting HTTP protocol to HTTP/1.1 only");
                listenOptions.Protocols = HttpProtocols.Http1;
            }

            listenOptions.UseHttps(async (stream, clientHelloInfo, state, cancellationToken) =>
            {
                var hostName = clientHelloInfo.ServerName.ToLower();

                var serviceProvider = kestrelOptions.ApplicationServices;
                var (cert, requireClientCertificate) = await ServerCertificateSelector(hostName, odinConfig, serviceProvider);

                if (cert == null)
                {
                    //
                    // This is an escape hatch so the runtime won't log an error
                    // when no certificate could be found.
                    //
                    // NOTE:
                    // When bots are probing for SSLv2 support (which is unsecure and denied in Kestrel),
                    // the runtime will throw the exception:
                    //
                    //   System.NotSupportedException:
                    //     The server mode SSL must use a certificate with the associated private key.
                    //
                    // Without ever hitting this part of the code.
                    //
                    // Reproducible with: $ testssl.sh --serial --protocols <identity-host>
                    //

                    throw new ConnectionAbortedException();
                }

                var result = new SslServerAuthenticationOptions
                {
                    ServerCertificate = cert
                };

                if (requireClientCertificate)
                {
                    result.AllowRenegotiation = true;
                    result.ClientCertificateRequired = true;
                    result.RemoteCertificateValidationCallback = (_, _, _, _) => true;
                }

                return result;
            }, state: null!, handshakeTimeoutTimeSpan);
        }

        //         

        private static async Task<(X509Certificate2 certificate, bool requireClientCertificate)> ServerCertificateSelector(
            string hostName,
            OdinConfiguration config,
            IServiceProvider serviceProvider)
        {
            if (string.IsNullOrWhiteSpace(hostName))
            {
                return (null, false);
            }

            string sslRoot, domain;

            //
            // Look up tenant from host name
            //
            var requireClientCertificate = false;
            var registry = serviceProvider.GetRequiredService<IIdentityRegistry>();
            var idReg = registry.ResolveIdentityRegistration(hostName, out _);
            if (idReg != null)
            {
                var tenantContext = registry.CreateTenantContext(idReg);
                sslRoot = tenantContext.SslRoot;
                domain = idReg.PrimaryDomainName;

                // Require client certificate if domain prefix is "capi"
                requireClientCertificate =
                    hostName != domain && hostName.StartsWith(DnsConfigurationSet.PrefixCertApi);
            }
            //
            // Not a tenant, is hostName a known system (e.g. provisioning)? 
            //
            else if (TryGetSystemSslRoot(hostName, config, out sslRoot))
            {
                domain = hostName;
            }
            //
            // We don't know what hostName is, get out! 
            //
            else
            {
                Log.Verbose("Cannot find nor create certificate for {host} since it's neither a tenant nor a known system on this identity host", hostName);
                return (null, false);
            }

            var certificateServiceFactory = serviceProvider.GetRequiredService<ICertificateServiceFactory>();
            var tc = certificateServiceFactory.Create(sslRoot);

            // 
            // Tenant or system found, lookup certificate
            //
            var certificate = tc.ResolveCertificate(domain);
            if (null != certificate)
            {
                return (certificate, requireClientCertificate);
            }

            // 
            // Tenant or system found, but no certificate. Create it.
            //

            // Sanity #1
            if (config.Host.DefaultHttpPort != 80)
            {
                Log.Error("Lets-encrypt requires port 80 for HTTP-01 challenge");
                return (null, false);
            }

            string[] sans = null;
            if (idReg != null)
            {
                sans = idReg.GetSans();
            }

            certificate = await tc.CreateCertificate(domain, sans);

            // Sanity #2
            if (null == certificate)
            {
                Log.Error($"No certificate configured for {hostName}");
            }

            return (certificate, requireClientCertificate);
        }

        //

        private static bool TryGetSystemSslRoot(string hostName, OdinConfiguration config, out string sslRoot)
        {
            if (hostName == config.Registry.ProvisioningDomain || hostName == config.Admin.Domain)
            {
                sslRoot = config.Host.SystemSslRootPath;
                return true;
            }

            sslRoot = "";
            return false;
        }

        //

        private static (bool didHandle, int exitCode) HandleCommandLineArgs(string[] args)
        {
            //
            // Command line: export docker env config
            //
            //
            // Example:
            //   dotnet run --no-build -- --export-docker-env
            //
            if (args.Contains("--export-docker-env"))
            {
                var (_, appSettingsConfig) = LoadConfig(false);
                var envVars = appSettingsConfig.ExportAsEnvironmentVariables();
                foreach (var envVar in envVars)
                {
                    Console.WriteLine($@"--env {envVar} \");
                }
                return (true, 0);
            }

            //
            // Command line: export shell env config
            //
            //
            // Example:
            //   dotnet run --no-build -- --export-shell-env
            //
            if (args.Contains("--export-shell-env"))
            {
                var (_, appSettingsConfig) = LoadConfig(false);
                var envVars = appSettingsConfig.ExportAsEnvironmentVariables();
                foreach (var envVar in envVars)
                {
                    Console.WriteLine($"export {envVar}");
                }
                return (true, 0);
            }

            //
            // Command line: export shell env config as bash array
            //
            //
            // Example:
            //   dotnet run --no-build -- --export-shell-env
            //
            if (args.Contains("--export-bash-array-env"))
            {
                var (_, appSettingsConfig) = LoadConfig(false);
                var envVars = appSettingsConfig.ExportAsEnvironmentVariables();
                Console.WriteLine("env_vars=(");
                foreach (var envVar in envVars)
                {
                    Console.WriteLine($"  \"{envVar}\"");
                }
                Console.WriteLine(")");
                Console.WriteLine(
                    """
                    for env_var in "${env_vars[@]}"; do
                      echo $env_var
                    done
                    """);
                return (true, 0);
            }

            //
            // Command line: dump environment variables
            //
            // examples:
            //
            //   FOO=BAR dotnet run --no-build -- --dump-env
            //
            //   ASPNETCORE_ENVIRONMENT=Production ./Odin.Hosting --dump-env
            //
            //
            if (args.Contains("--dump-env"))
            {
                var (_, appSettingsConfig) = LoadConfig(true);
                var envVars = appSettingsConfig.ExportAsEnvironmentVariables();
                foreach (var envVar in envVars)
                {
                    Console.WriteLine(envVar);
                }
                return (true, 0);
            }

            //
            // Command line: start connection test
            //
            // examples:
            //
            //   dotnet run -- --tcp-connection-test 80 5000
            //
            //   80: TCP port to listen on
            //   5000: timeout in milliseconds before giving up
            //
            //   ASPNETCORE_ENVIRONMENT=Production ./Odin.Hosting --tcp-connection-test 80 5000
            //
            //
            if (args.Length == 3 && args[0] == "--tcp-connection-test")
            {
                var port = int.Parse(args[1]);
                var timeout = int.Parse(args[2]);
                var listener = new TcpListener(IPAddress.Any, port);
                listener.Start();
                Console.WriteLine($"Listening on port {port} for {timeout} ms");
                var task = listener.AcceptTcpClientAsync();
                var result = task.Wait(timeout);
                if (result)
                {
                    Console.WriteLine("Connection established");
                    return (true, 0);
                }

                Console.WriteLine("Connection timed out");
                return (true, 1);
            }

            return (false, 0);
        }
    }
}