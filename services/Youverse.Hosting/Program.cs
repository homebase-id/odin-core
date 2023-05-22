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
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;
using Youverse.Core.Exceptions;
using Youverse.Core.Logging.CorrelationId;
using Youverse.Core.Logging.CorrelationId.Serilog;
using Youverse.Core.Logging.Hostname;
using Youverse.Core.Logging.Hostname.Serilog;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Certificate;
using Youverse.Core.Services.Configuration;
using Youverse.Core.Services.Registry;
using Youverse.Core.Services.Registry.Registration;
using Youverse.Hosting.Multitenant;

namespace Youverse.Hosting
{
    public static class Program
    {
        private const string LogOutputTemplate = "{Timestamp:o} {Level:u3} {CorrelationId} {Hostname} {Message:lj}{NewLine}{Exception}"; // Add {SourceContext} to see source
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

        public static (YouverseConfiguration, IConfiguration) LoadConfig()
        {
            const string envVar = "DOTYOU_ENVIRONMENT";
            var env = Environment.GetEnvironmentVariable(envVar) ?? "";

            if (string.IsNullOrEmpty(env))
            {
                throw new YouverseSystemException($"You must set an environment variable named [{envVar}] which specifies your environment.\n" +
                                                  $"This must match your app settings file as follows 'appsettings.ENV.json'");
            }

            var appSettingsFile = $"appsettings.{env.ToLower()}.json";
            Log.Information($"Current Folder: {Environment.CurrentDirectory}");
            if (!File.Exists(Path.Combine(Environment.CurrentDirectory, appSettingsFile)))
            {
                Log.Information($"Missing {appSettingsFile}");
            }

            var config = new ConfigurationBuilder()
                // .AddJsonFile("appsettings.json", optional: false)
                .AddJsonFile(appSettingsFile, optional: false)
                .AddEnvironmentVariables()
                .Build();

            return (new YouverseConfiguration(config), config);
        }

        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            var (youverseConfig, appSettingsConfig) = LoadConfig();

            var loggingDirInfo = Directory.CreateDirectory(youverseConfig.Logging.LogFilePath);
            if (!loggingDirInfo.Exists)
            {
                throw new YouverseClientException($"Could not create logging folder at [{youverseConfig.Logging.LogFilePath}]");
            }

            var dataRootDirInfo = Directory.CreateDirectory(youverseConfig.Host.TenantDataRootPath);
            if (!dataRootDirInfo.Exists)
            {
                throw new YouverseClientException($"Could not create logging folder at [{youverseConfig.Logging.LogFilePath}]");
            }

            Log.Information($"Root path:{youverseConfig.Host.TenantDataRootPath}");
 
            var builder = Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration(builder =>
                {
                    builder.AddConfiguration(appSettingsConfig);
                })
                .UseSystemd()
                .UseServiceProviderFactory(new MultiTenantServiceProviderFactory(DependencyInjection.ConfigureMultiTenantServices, DependencyInjection.InitializeTenant))
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.ConfigureKestrel(kestrelOptions => 
                    {
                        kestrelOptions.Limits.MaxRequestBodySize = null;

                        foreach (var address in youverseConfig.Host.IPAddressListenList)
                        {
                            var ip = address.GetIp();
                            kestrelOptions.Listen(ip, address.HttpPort);
                            kestrelOptions.Listen(ip, address.HttpsPort, 
                                options => ConfigureHttpListenOptions(youverseConfig, kestrelOptions, options));
                        }
                        
                    })
                    .UseStartup<Startup>();
                });

            if (youverseConfig.Logging.Level == LoggingLevel.ErrorsOnly)
            {
                builder.UseSerilog((context, services, configuration) => configuration
                    .ReadFrom.Services(services)
                    .MinimumLevel.Error());
            
                return builder;
            }

            if (youverseConfig.Logging.Level == LoggingLevel.Verbose)
            {
                builder.UseSerilog((context, services, configuration) => configuration
                    .ReadFrom.Services(services)
                    .MinimumLevel.Debug()
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                    .MinimumLevel.Override("Microsoft.AspNetCore.Authentication", LogEventLevel.Error)
                    .MinimumLevel.Override("Quartz", LogEventLevel.Warning)
                    .MinimumLevel.Override("Youverse.Hosting.Middleware.Logging.RequestLoggingMiddleware", LogEventLevel.Information)
                    .MinimumLevel.Override("Youverse.Core.Services.Transit.Outbox", LogEventLevel.Warning)
                    .MinimumLevel.Override("Youverse.Core.Services.Workers.Transit.StokeOutboxJob", LogEventLevel.Warning)
                    .Enrich.FromLogContext()
                    .Enrich.WithHostname(new StickyHostnameGenerator())
                    .Enrich.WithCorrelationId(new CorrelationUniqueIdGenerator())
                    // .WriteTo.Debug() // SEB:TODO only do this in debug builds
                    .WriteTo.Async(sink => sink.Console(outputTemplate: LogOutputTemplate, theme: LogOutputTheme))
                    .WriteTo.Async(sink => sink.RollingFile(Path.Combine(youverseConfig.Logging.LogFilePath, "app-{Date}.log"), outputTemplate: LogOutputTemplate)));
                
                return builder;
            }

            return builder;
        }
        
        //

        private static void ConfigureHttpListenOptions(
            YouverseConfiguration youverseConfig,
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
                var cert = await ServerCertificateSelector(hostName, youverseConfig, serviceProvider);

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
            YouverseConfiguration config,
            IServiceProvider serviceProvider)
        {
            if (string.IsNullOrWhiteSpace(hostName))
            {
                return null;
            }
            
            // Provisioning specifics
            // SEB:TODO should we create letsencrypt cert for provisioning as well?
            if (hostName == config.Registry.ProvisioningDomain)
            {
                var publicKeyPath = Path.Combine(config.Host.SystemSslRootPath, config.Registry.ProvisioningDomain, "certificate.crt");
                var privateKeyPath = Path.Combine(config.Host.SystemSslRootPath, config.Registry.ProvisioningDomain, "private.key");

                var cert = DotYouCertificateCache.LoadCertificate(hostName, privateKeyPath, publicKeyPath);
                if (null == cert)
                {
                    Log.Error($"No certificate configured for {hostName}");
                }

                return cert;
            }
            
            //
            // Hostname -> tenant
            //
            var registry = serviceProvider.GetRequiredService<IIdentityRegistry>();
            var idReg = registry.ResolveIdentityRegistration(hostName, out _);
            if (idReg == null)
            {
                Log.Debug("Cannot return certificate because {host} does not belong here", hostName);
                return null;
            }
            
            // SEB:TODO
            // TenantContext.Create does IO. This is bad in critical path.
            // Find another way to get the SslRoot of the tenant
            var tenantContext =
                TenantContext.Create(idReg.Id, idReg.PrimaryDomainName, config.Host.TenantDataRootPath, null);
            
            var certificateServiceFactory = serviceProvider.GetRequiredService<ICertificateServiceFactory>();
            var tc = certificateServiceFactory.Create(tenantContext.SslRoot);

            // 
            // Lookup tenant and certificate
            //
            var certificate = tc.ResolveCertificate(idReg);
            if (null != certificate)
            {
                return certificate;
            }
            
            // 
            // Tenant found, but no certificate. Create it.
            //
            certificate = await tc.CreateCertificate(idReg);

            if (null == certificate)
            {
                Log.Error($"No certificate configured for {hostName}");
            }

            return certificate;
        }
    }
}