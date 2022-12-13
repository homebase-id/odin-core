using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Logging.CorrelationId;
using Youverse.Core.Logging.CorrelationId.Serilog;
using Youverse.Core.Logging.Hostname;
using Youverse.Core.Logging.Hostname.Serilog;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Certificate;
using Youverse.Core.Services.Configuration;
using Youverse.Core.Services.Registry;
using Youverse.Hosting._dev;
using Youverse.Hosting.Multitenant;

namespace Youverse.Hosting
{
    public static class Program
    {
        private const string LogOutputTemplate = "{Timestamp:o} {Level:u3} {CorrelationId} {Hostname} {Message:lj}{NewLine}{Exception}"; // Add {SourceContext} to see source
        private static readonly SystemConsoleTheme LogOutputTheme = SystemConsoleTheme.Literate;
        private static IIdentityRegistry _registry;

        public static int Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
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
            var (cfg, _) = LoadConfig();

            var loggingDirInfo = Directory.CreateDirectory(cfg.Logging.LogFilePath);
            if (!loggingDirInfo.Exists)
            {
                throw new YouverseClientException($"Could not create logging folder at [{cfg.Logging.LogFilePath}]");
            }

            var dataRootDirInfo = Directory.CreateDirectory(cfg.Host.TenantDataRootPath);
            if (!dataRootDirInfo.Exists)
            {
                throw new YouverseClientException($"Could not create logging folder at [{cfg.Logging.LogFilePath}]");
            }

            _registry = new FileSystemIdentityRegistry(cfg.Host.TenantDataRootPath, cfg.CertificateRenewal.ToCertificateRenewalConfig());
            _registry.Initialize();

            DevEnvironmentSetup.ConfigureIfPresent(cfg, _registry);

            var builder = Host.CreateDefaultBuilder(args)
                .UseSystemd()
                .UseServiceProviderFactory(new MultiTenantServiceProviderFactory(DependencyInjection.ConfigureMultiTenantServices, DependencyInjection.InitializeTenant))
                .ConfigureServices(services =>
                {
                    //TODO: I'm not sure it's a good idea to add this as a service.
                    services.Add(new ServiceDescriptor(typeof(IIdentityRegistry), _registry));
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    var urls = cfg.Host.IPAddressListenList.Select(entry => $"https://{entry.Ip}:{entry.HttpsPort}").ToList();
                    urls.AddRange(cfg.Host.IPAddressListenList.Select(entry => $"http://{entry.Ip}:{entry.HttpPort}"));

                    webBuilder.ConfigureKestrel(options =>
                        {
                            options.ConfigureHttpsDefaults(opts =>
                            {
                                opts.ClientCertificateValidation = (certificate2, chain, arg3) =>
                                {
                                    //HACK: need to expand this to perform validation.
                                    //HACK: to work around the fact that ISRG Root X1 is not set for Client Certificate authentication
                                    return true;
                                };

                                opts.ServerCertificateSelector = (context, s) => ServerCertificateSelector(context, s, cfg);
                                opts.ClientCertificateMode = ClientCertificateMode.AllowCertificate;
                            });
                        })
                        .UseKestrel() //Use Kestrel to ensure we can run this on linux
                        .UseUrls(urls.ToArray()) //you need to configure netsh on windows to allow 443
                        .UseStartup<Startup>();
                });

            if (cfg.Logging.Level == LoggingLevel.ErrorsOnly)
            {
                builder.UseSerilog((context, services, configuration) => configuration
                    .ReadFrom.Services(services)
                    .MinimumLevel.Error());

                return builder;
            }

            if (cfg.Logging.Level == LoggingLevel.Verbose)
            {
                builder.UseSerilog((context, services, configuration) => configuration
                    .ReadFrom.Services(services)
                    .MinimumLevel.Error()
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
                    .WriteTo.Async(sink => sink.Console(outputTemplate: LogOutputTemplate, theme: LogOutputTheme))
                    .WriteTo.Async(sink => sink.RollingFile(Path.Combine(cfg.Logging.LogFilePath, "app-{Date}.log"), outputTemplate: LogOutputTemplate)));
                return builder;
            }

            return builder;
        }

        private static X509Certificate2 ServerCertificateSelector(ConnectionContext connectionContext, string hostName, YouverseConfiguration config)
        {
            Log.Information($"provisioning domain: [{config.Registry.ProvisioningDomain}]");
            if (hostName.ToLower().Trim() == config.Registry.ProvisioningDomain.ToLower().Trim())
            {
                Log.Information("Loading certificate for provisioning domain");
                string publicKeyPath = Path.Combine(config.Host.SystemSslRootPath, config.Registry.ProvisioningDomain, "certificate.crt");
                string privateKeyPath = Path.Combine(config.Host.SystemSslRootPath, config.Registry.ProvisioningDomain, "private.key");
                Log.Information($"Checking path [{publicKeyPath}]");
            
                var cert = DotYouCertificateLoader.LoadCertificate(publicKeyPath, privateKeyPath);
                if (null == cert)
                {
                    Log.Error($"No certificate configured for {hostName}");
                }
            
                return cert;
            }

            // connectionContext.ConnectionId
            if (!string.IsNullOrEmpty(hostName))
            {
                //TODO: add caching of loaded certificates
                Guid registryId = _registry.ResolveId(hostName);
                DotYouIdentity dotYouId = (DotYouIdentity)hostName;

                ITenantCertificateService tc = new TenantCertificateService(TenantContext.Create(registryId, dotYouId, config.Host.TenantDataRootPath, null));
                var cert = tc.ResolveCertificate(dotYouId);

                if (null == cert)
                {
                    Log.Error($"No certificate configured for {hostName}");
                }

                return cert;
            }

            return null;


            if (!string.IsNullOrEmpty(hostName))
            {
                //TODO: add caching of loaded certificates
                Guid domainId = _registry.ResolveId(hostName);
                DotYouIdentity dotYouId = (DotYouIdentity)hostName;
                var cert = CertificateResolver.GetSslCertificate(config.Host.TenantDataRootPath, domainId, dotYouId);
                if (null == cert)
                {
                    //TODO: add logging or throw exception
                    Console.WriteLine($"No certificate configured for {hostName}");
                }

                return cert;
            }

            return null;
        }
    }
}