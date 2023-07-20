using System;
using System.Diagnostics;
using System.IO;
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
using Odin.Core.Services.Base;
using Odin.Core.Services.Certificate;
using Odin.Core.Services.Configuration;
using Odin.Core.Services.Registry;
using Odin.Core.Services.Registry.Registration;
using Odin.Hosting.Multitenant;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

namespace Odin.Hosting
{
    public static class Program
    {
        private const string
            LogOutputTemplate = "{Timestamp:o} {Level:u3} {CorrelationId} {Hostname} {Message:lj}{NewLine}{Exception}"; // Add {SourceContext} to see source

        private static readonly SystemConsoleTheme LogOutputTheme = SystemConsoleTheme.Literate;

        public static int Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .Enrich.WithHostname(new StickyHostnameGenerator())
                .Enrich.WithCorrelationId(new CorrelationUniqueIdGenerator())
                .WriteTo.Console(outputTemplate: LogOutputTemplate, theme: LogOutputTheme)
                .CreateBootstrapLogger();

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

        public static (OdinConfiguration, IConfiguration) LoadConfig()
        {
            const string configPathOverrideVariable = "ODIN_CONFIG_PATH";

            var cfgPathOverride = Environment.GetEnvironmentVariable(configPathOverrideVariable);
            var configFolder = string.IsNullOrEmpty(cfgPathOverride) ? Environment.CurrentDirectory : cfgPathOverride;
            Log.Information($"Looking for configuration in folder: {configFolder}");

            const string envVar = "DOTYOU_ENVIRONMENT";
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

        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            var (odinConfig, appSettingsConfig) = LoadConfig();

            var loggingDirInfo = Directory.CreateDirectory(odinConfig.Logging.LogFilePath);
            if (!loggingDirInfo.Exists)
            {
                throw new OdinClientException($"Could not create logging folder at [{odinConfig.Logging.LogFilePath}]");
            }

            var dataRootDirInfo = Directory.CreateDirectory(odinConfig.Host.TenantDataRootPath);
            if (!dataRootDirInfo.Exists)
            {
                throw new OdinClientException($"Could not create logging folder at [{odinConfig.Logging.LogFilePath}]");
            }

            Log.Information($"Root path:{odinConfig.Host.TenantDataRootPath}");

            var builder = Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration(builder => { builder.AddConfiguration(appSettingsConfig); })
                .UseSystemd()
                .UseServiceProviderFactory(new MultiTenantServiceProviderFactory(DependencyInjection.ConfigureMultiTenantServices,
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
                        })
                        .UseStartup<Startup>();
                });

            if (odinConfig.Logging.Level == LoggingLevel.ErrorsOnly)
            {
                builder.UseSerilog((context, services, configuration) => configuration
                    .ReadFrom.Services(services)
                    .MinimumLevel.Error());

                return builder;
            }

            if (odinConfig.Logging.Level == LoggingLevel.Verbose)
            {
                builder.UseSerilog((context, services, configuration) => configuration
                    .ReadFrom.Services(services)
                    .MinimumLevel.Debug()
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                    .MinimumLevel.Override("Microsoft.AspNetCore.Authentication", LogEventLevel.Error)
                    .MinimumLevel.Override("Quartz", LogEventLevel.Warning)
                    .MinimumLevel.Override("Odin.Hosting.Middleware.Logging.RequestLoggingMiddleware", LogEventLevel.Information)
                    .MinimumLevel.Override("Odin.Core.Services.Transit.Outbox", LogEventLevel.Warning)
                    .MinimumLevel.Override("Odin.Core.Services.Workers.Transit.StokeOutboxJob", LogEventLevel.Warning)
                    .Enrich.FromLogContext()
                    .Enrich.WithHostname(new StickyHostnameGenerator())
                    .Enrich.WithCorrelationId(new CorrelationUniqueIdGenerator())
                    // .WriteTo.Debug() // SEB:TODO only do this in debug builds
                    .WriteTo.Async(sink => sink.Console(outputTemplate: LogOutputTemplate, theme: LogOutputTheme))
                    .WriteTo.Async(sink =>
                        sink.RollingFile(Path.Combine(odinConfig.Logging.LogFilePath, "app-{Date}.log"), outputTemplate: LogOutputTemplate)));

                return builder;
            }

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
                var cert = await ServerCertificateSelector(hostName, odinConfig, serviceProvider);

                if (cert == null)
                {
                    // This is an escape hatch so runtime won't log an error
                    // when no certificate could be found
                    throw new ConnectionAbortedException();
                }

                var result = new SslServerAuthenticationOptions
                {
                    ServerCertificate = cert
                };

                // Require client certificate if domain prefix is "capi"
                if (hostName.StartsWith(DnsConfigurationSet.PrefixCertApi))
                {
                    result.AllowRenegotiation = true;
                    result.ClientCertificateRequired = true;
                    result.RemoteCertificateValidationCallback = (_, _, _, _) => true;
                }

                return result;
            }, state: null!, handshakeTimeoutTimeSpan);
        }

        //         

        private static async Task<X509Certificate2> ServerCertificateSelector(
            string hostName,
            OdinConfiguration config,
            IServiceProvider serviceProvider)
        {
            if (string.IsNullOrWhiteSpace(hostName))
            {
                return null;
            }

            string sslRoot, domain;

            //
            // Look up tenant from host name
            //
            var registry = serviceProvider.GetRequiredService<IIdentityRegistry>();
            var idReg = registry.ResolveIdentityRegistration(hostName, out _);
            if (idReg != null)
            {
                var tenantContext = TenantContext.Create(
                    idReg.Id,
                    idReg.PrimaryDomainName,
                    config.Host.TenantDataRootPath,
                    config.Host.TenantPayloadRootPath,
                    false);
                sslRoot = tenantContext.SslRoot;
                domain = idReg.PrimaryDomainName;
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
                Log.Debug("Cannot return certificate for {host} because it does not belong here", hostName);
                return null;
            }

            var certificateServiceFactory = serviceProvider.GetRequiredService<ICertificateServiceFactory>();
            var tc = certificateServiceFactory.Create(sslRoot);

            // 
            // Tenant or system found, lookup certificate
            //
            var certificate = tc.ResolveCertificate(domain);
            if (null != certificate)
            {
                return certificate;
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

            return certificate;
        }

        //

        private static bool TryGetSystemSslRoot(string hostName, OdinConfiguration config, out string sslRoot)
        {
            // We only have provisioning system for now...
            if (hostName == config.Registry.ProvisioningDomain)
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