using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
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
using Odin.Core.Logging.Exception.Serilog;
using Odin.Core.Logging.Hostname;
using Odin.Core.Logging.Hostname.Serilog;
using Odin.Core.Logging.LogLevelOverwrite.Serilog;
using Odin.Core.Logging.Statistics.Serilog;
using Odin.Hosting.Cli;
using Odin.Services.Certificate;
using Odin.Services.Configuration;
using Odin.Services.Registry;
using Odin.Services.Registry.Registration;
using Odin.Services.Tenant.Container;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;
using Version = Odin.Services.Version;

namespace Odin.Hosting
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            var (didHandle, exitCode) = CommandLine.HandleCommandLineArgs(args);
            if (didHandle)
            {
                return exitCode;
            }

            //
            // Web host
            //
            {
                var (odinConfig, appSettingsConfig) = AppSettings.LoadConfig(true);
                Log.Logger = CreateLogger(appSettingsConfig, odinConfig).CreateBootstrapLogger();
                try
                {
                    Log.Information("Identity-host version: {Version}", Version.VersionText);
                    var host = CreateHostBuilder(args).Build().BeforeApplicationStarting(args);
                    if (host.ProcessCommandLineArgs(args))
                    {
                        Log.Information("Starting web host");
                        host.Run();
                        Log.Information("Stopped web host\n\n\n");
                    }
                    host.OnApplicationStopping();
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



        //

        private static LoggerConfiguration CreateLogger(
            IConfiguration configuration,
            OdinConfiguration odinConfig,
            IServiceProvider services = null,
            LoggerConfiguration loggerConfig = null)
        {
            const string logOutputTemplate = // Add {SourceContext} to see source
                "{Timestamp:o} {Level:u3} {CorrelationId} {Hostname} {Message:lj}{ExceptionMessage:lj}{NewLine}{Exception}";

            loggerConfig ??= new LoggerConfiguration();

            loggerConfig
                .ReadFrom.Configuration(configuration)
                .Enrich.FromLogContext()
                .Enrich.WithHostname(new StickyHostnameGenerator())
                .Enrich.WithCorrelationId(new CorrelationUniqueIdGenerator())
                .Enrich.WithExceptionMessage()
                .WriteTo.Filter(sink => sink
                    .Async(s => s.Console(
                        outputTemplate: logOutputTemplate,
                        theme: SystemConsoleTheme.Literate)));

            if (odinConfig.Logging.LogFilePath != "")
            {
                loggerConfig
                    .WriteTo.Filter(sink => sink
                        .Async(s => s.File(
                            path: Path.Combine(odinConfig.Logging.LogFilePath, "app-.log"),
                            rollingInterval: RollingInterval.Day,
                            outputTemplate: logOutputTemplate,
                            fileSizeLimitBytes: 1L * 1024 * 1024 * 1024,
                            rollOnFileSizeLimit: true,
                            retainedFileCountLimit: null
                        )));
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
            var (odinConfig, appSettingsConfig) = AppSettings.LoadConfig(true);

            if (odinConfig.Logging.LogFilePath != "")
            {
                var loggingDirInfo = Directory.CreateDirectory(odinConfig.Logging.LogFilePath);
                if (!loggingDirInfo.Exists)
                {
                    throw new OdinSystemException($"Could not create logging folder at [{odinConfig.Logging.LogFilePath}]");
                }
            }

            Directory.CreateDirectory(odinConfig.Host.SystemDataRootPath);
            Log.Information($"System root path:{odinConfig.Host.SystemDataRootPath}");

            var dataRootDirInfo = Directory.CreateDirectory(odinConfig.Host.TenantDataRootPath);
            if (!dataRootDirInfo.Exists)
            {
                throw new OdinSystemException($"Could not create tenant root folder at [{odinConfig.Host.TenantDataRootPath}]");
            }

            Log.Information($"Tenant root path:{odinConfig.Host.TenantDataRootPath}");

            var builder = Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration(builder => { builder.AddConfiguration(appSettingsConfig); })
                .UseSerilog((context, services, loggerConfiguration) =>
                {
                    CreateLogger(context.Configuration, odinConfig, services, loggerConfiguration);
                })
                .UseServiceProviderFactory(new MultiTenantServiceProviderFactory())
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
                var (cert, requireClientCertificate) =
                    await ServerCertificateSelector(hostName, odinConfig, serviceProvider, cancellationToken);

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

        private static readonly string[] NoSans = [];
        private static async Task<(X509Certificate2 certificate, bool requireClientCertificate)> ServerCertificateSelector(
            string hostName,
            OdinConfiguration config,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken = default)
        {
            if (Log.IsEnabled(LogEventLevel.Verbose))
            {
                Log.Verbose("Getting certificate for {host}", hostName);
            }

            if (string.IsNullOrWhiteSpace(hostName))
            {
                return (null, false);
            }

            string domain;

            //
            // Look up tenant from host name
            //
            var requireClientCertificate = false;
            var registry = serviceProvider.GetRequiredService<IIdentityRegistry>();
            var idReg = registry.ResolveIdentityRegistration(hostName, out _);
            if (idReg != null)
            {
                domain = idReg.PrimaryDomainName;

                // Require client certificate if domain prefix is "capi"
                requireClientCertificate =
                    hostName != domain && hostName.StartsWith(DnsConfigurationSet.PrefixCertApi);
            }
            //
            // Not a tenant, is hostName a known system (e.g. provisioning)? 
            //
            else if (IsKnownSystemDomain(hostName, config))
            {
                domain = hostName;
            }
            //
            // We don't know what hostName is, get out! 
            //
            else
            {
                Log.Verbose(
                    "Cannot find nor create certificate for {host} since it's neither a tenant nor a known system on this identity host",
                    hostName);
                return (null, false);
            }

            var certificateServiceFactory = serviceProvider.GetRequiredService<ICertificateServiceFactory>();
            var certificateService = certificateServiceFactory.Create();

            // 
            // Tenant or system found, lookup certificate
            //
            var certificate = await certificateService.GetCertificateAsync(domain);
            if (certificate != null)
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

            var sans = NoSans;
            if (idReg != null)
            {
                sans = idReg.GetSans();
            }

            certificate = await certificateService.CreateCertificateAsync(domain, sans, cancellationToken);

            // Sanity #2
            if (certificate == null)
            {
                Log.Error("No certificate configured for {hostName}", hostName);
            }

            return (certificate, requireClientCertificate);
        }

        //

        private static bool IsKnownSystemDomain(string hostName, OdinConfiguration config)
        {
            if (config.Registry.ProvisioningEnabled && hostName == config.Registry.ProvisioningDomain)
            {
                return true;
            }

            if (config.Admin.ApiEnabled && hostName == config.Admin.Domain)
            {
                return true;
            }

            return false;
        }

        //
    }
}