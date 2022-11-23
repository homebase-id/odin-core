using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
using Youverse.Core.Services.Registry;
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

        private static Configuration LoadConfig()
        {
            var config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false)
                .AddEnvironmentVariables()
                .Build();

            return new Configuration(config);
        }

        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            var cfg = LoadConfig();

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

            var tempDataRootDirInfo = Directory.CreateDirectory(cfg.Host.TempTenantDataRootPath);
            if (!tempDataRootDirInfo.Exists)
            {
                throw new YouverseClientException($"Could not create logging folder at [{cfg.Logging.LogFilePath}]");
            }

            //HACK until I decide if we want to have ServerCertificateSelector read directly from disk
            _registry = cfg.Host.UseLocalCertificateRegistry
                ? new DevelopmentIdentityRegistry(cfg.Host.TenantDataRootPath, cfg.Host.TempTenantDataRootPath)
                : new FileSystemIdentityRegistry(cfg.Host.TenantDataRootPath, cfg.Host.TempTenantDataRootPath);

            _registry.Initialize();

            return Host.CreateDefaultBuilder(args)
                .UseSystemd()
                .UseServiceProviderFactory(new MultiTenantServiceProviderFactory(DependencyInjection.ConfigureMultiTenantServices, DependencyInjection.InitializeTenant))
                .UseSerilog((context, services, configuration) => configuration
                    .ReadFrom.Services(services)
                    .MinimumLevel.Debug()
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                    .MinimumLevel.Override("Quartz", LogEventLevel.Warning)
                    .MinimumLevel.Override("Youverse.Hosting.Middleware.Logging.RequestLoggingMiddleware", LogEventLevel.Information)
                    .MinimumLevel.Override("Youverse.Core.Services.Transit.Outbox", LogEventLevel.Warning)
                    .MinimumLevel.Override("Youverse.Core.Services.Workers.Transit.StokeOutboxJob", LogEventLevel.Warning)
                    .Enrich.FromLogContext()
                    .Enrich.WithHostname(new StickyHostnameGenerator())
                    .Enrich.WithCorrelationId(new CorrelationUniqueIdGenerator())
                    .WriteTo.Async(sink => sink.Console(outputTemplate: LogOutputTemplate, theme: LogOutputTheme))
                    .WriteTo.Async(sink => sink.RollingFile(Path.Combine(cfg.Logging.LogFilePath, "app-{Date}.log"), outputTemplate: LogOutputTemplate))
                )
                .ConfigureServices(services =>
                {
                    //TODO: I'm not sure it's a good idea to add this as a service.
                    services.Add(new ServiceDescriptor(typeof(IIdentityRegistry), _registry));
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
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

                                opts.ServerCertificateSelector = (connectionContext, hostName) =>
                                {
                                    if (!string.IsNullOrEmpty(hostName))
                                    {
                                        //TODO: add caching of loaded certificates
                                        Guid domainId = _registry.ResolveId(hostName);
                                        DotYouIdentity dotYouId = (DotYouIdentity)hostName;
                                        var cert = CertificateResolver.GetSslCertificate(cfg.Host.TenantDataRootPath, domainId, dotYouId);
                                        if (null == cert)
                                        {
                                            //TODO: add logging or throw exception
                                            Console.WriteLine($"No certificate configured for {hostName}");
                                        }

                                        return cert;
                                    }

                                    return null;
                                };


                                //Let the OS decide
                                //TODO: revisit if we should let the OS decide
                                //opts.SslProtocols = SslProtocols.None;
                                opts.ClientCertificateMode = ClientCertificateMode.AllowCertificate;
                            });
                        })
                        .UseKestrel() //Use Kestrel to ensure we can run this on linux
                        .UseUrls("https://*:443", "http://*:80") //you need to configure netsh on windows to allow 443
                        .UseStartup<Startup>();
                });
        }
    }
}