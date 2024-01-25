using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Odin.Core.Exceptions;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Logging.CorrelationId.Serilog;
using Odin.Core.Logging.Hostname;
using Odin.Core.Logging.Hostname.Serilog;
using Odin.Core.Logging.LogLevelOverwrite.Serilog;
using Odin.Core.Services.Certificate;
using Odin.Core.Services.Configuration;
using Odin.Core.Services.Registry;
using Odin.Core.Services.Registry.Registration;
using Odin.Core.Services.Tenant.Container;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;

namespace Odin.Hosting
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            var (odinConfig, appSettingsConfig) = LoadConfig();
            Log.Logger = CreateLogger(appSettingsConfig, odinConfig).CreateBootstrapLogger();

            try
            {
                Log.Information("Starting web host");
                CreateHostBuilder(args).Build().Run();
                Log.Information("Stopped web host");
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

        //

        private static (OdinConfiguration, IConfiguration) LoadConfig()
        {
            const string configPathOverrideVariable = "ODIN_CONFIG_PATH";

            var cfgPathOverride = Environment.GetEnvironmentVariable(configPathOverrideVariable);
            var configFolder = string.IsNullOrEmpty(cfgPathOverride) ? Environment.CurrentDirectory : cfgPathOverride;
            Log.Information($"Looking for configuration in folder: {configFolder}");

            const string envVar = "ASPNETCORE_ENVIRONMENT";
            var env = Environment.GetEnvironmentVariable(envVar) ?? "";

            if (string.IsNullOrEmpty(env))
            {
                throw new OdinSystemException($"You must set an environment variable named [{envVar}] which specifies your environment.\n" +
                                              $"This must match your app settings file as follows 'appsettings.ENV.json'");
            }

            var appSettingsFile = $"appsettings.{env.ToLower()}.json";
            var configPath = Path.Combine(configFolder, appSettingsFile);

            if (!File.Exists(configPath))
            {
                throw new OdinSystemException($"Could not find configuration file [{configPath}]");
            }

            Log.Information($"Loading configuration at [{configPath}]");

            var config = new ConfigurationBuilder()
                .AddJsonFile(configPath, optional: false)
                .AddEnvironmentVariables()
                .Build();

            return (new OdinConfiguration(config), config);
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
                    sink => sink.Console(outputTemplate: logOutputTemplate, theme: logOutputTheme)))
                .WriteTo.LogLevelModifier(s => s.Async(
                    sink => sink.RollingFile(Path.Combine(odinConfig.Logging.LogFilePath, "app-{Date}.log"),
                        outputTemplate: logOutputTemplate)));

            if (services != null)
            {
                loggerConfig.ReadFrom.Services(services);
            }

            if (odinConfig.Logging.EnableSeq)
            {
                loggerConfig.Enrich.WithProperty("SystemId", odinConfig.Logging.SeqSystemId);

                // NOTE Seq logging is async by default
                loggerConfig.WriteTo.LogLevelModifier(
                    sink => sink.Seq(odinConfig.Logging.SeqUri, apiKey: odinConfig.Logging.SeqApiKey));
            }

            return loggerConfig;
        }

        //

        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            var (odinConfig, appSettingsConfig) = LoadConfig();

            var loggingDirInfo = Directory.CreateDirectory(odinConfig.Logging.LogFilePath);
            if (!loggingDirInfo.Exists)
            {
                throw new OdinSystemException($"Could not create logging folder at [{odinConfig.Logging.LogFilePath}]");
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
                        DependencyInjection.ConfigureMultiTenantServices,
                        DependencyInjection.InitializeTenant))
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
                        .UseStartup<Startup>();
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

            listenOptions.UseHttps(async (stream, clientHelloInfo, state, cancellationToken) =>
            {
                // SEB:NOTE ToLower() should not be needed here, but better safe than sorry.
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
            string[] sans = null;
            if (idReg != null)
            {
                sans = idReg.GetSans();
            }

            certificate = await tc.CreateCertificate(domain, sans);

            //
            // Sanity
            //
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
    }
}