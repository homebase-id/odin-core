using System;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;
using Youverse.Core.Identity;
using Youverse.Core.Logging.CorrelationId;
using Youverse.Core.Logging.CorrelationId.Serilog;
using Youverse.Core.Logging.Hostname;
using Youverse.Core.Logging.Hostname.Serilog;
using Youverse.Core.Services.Registry;
using Youverse.Core.Util;
using Youverse.Hosting.IdentityRegistry;
using Youverse.Hosting.Multitenant;

namespace Youverse.Hosting
{
    public static class Program
    {
        private const string LogOutputTemplate = "{Timestamp:o} {Level:u3} {CorrelationId} {Hostname} {Message:lj}{NewLine}{Exception}";
        private static readonly SystemConsoleTheme LogOutputTheme = SystemConsoleTheme.Literate;
        private static IIdentityContextRegistry _registry;

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

            Directory.CreateDirectory(cfg.Logging.LogFilePath);
            Directory.CreateDirectory(cfg.Host.TenantDataRootPath);
            Directory.CreateDirectory(cfg.Host.TempTenantDataRootPath);

            //HACK until I decide if we want to have ServerCertificateSelector read directly from disk
            _registry = cfg.Host.UseLocalCertificateRegistry
                ? new DevelopmentIdentityContextRegistry(cfg.Host.TenantDataRootPath, cfg.Host.TempTenantDataRootPath)
                : new IdentityRegistryRpc(cfg);

            _registry.Initialize();

            return Host.CreateDefaultBuilder(args)
                .UseServiceProviderFactory(new MultiTenantServiceProviderFactory(DependencyInjection.ConfigureMultiTenantServices, DependencyInjection.InitializeTenant))
                .UseSerilog((context, services, configuration) => configuration
                    .ReadFrom.Services(services)
                    .MinimumLevel.Debug()
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                    .Enrich.FromLogContext()
                    .Enrich.WithHostname(new StickyHostnameGenerator())
                    .Enrich.WithCorrelationId(new CorrelationUniqueIdGenerator())
                    .WriteTo.Async(sink => sink.Console(outputTemplate: LogOutputTemplate, theme: LogOutputTheme))
                    .WriteTo.Async(sink => sink.RollingFile(Path.Combine(cfg.Logging.LogFilePath, "app-{Date}.log"), outputTemplate: LogOutputTemplate))
                )
                .ConfigureServices(services =>
                {
                    //TODO: I'm not sure it's a good idea to add this as a service.
                    services.Add(new ServiceDescriptor(typeof(IIdentityContextRegistry), _registry));
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
                        .UseUrls("https://*:443") //you need to configure netsh on windows to allow 443
                        .UseStartup<Startup>();
                });
        }
    }
}