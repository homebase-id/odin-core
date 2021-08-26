using System;
using System.IO;
using System.Security.Authentication;
using DotYou.DigitalIdentityHost;
using DotYou.DigitalIdentityHost.IdentityRegistry;
using DotYou.IdentityRegistry;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;


namespace DotYou.TenantHost
{
    public class Program
    {
        private static IIdentityContextRegistry _registry;

        private static Config LoadConfig()
        {
            var config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false)
                .Build();

            var cfg = new Config();
            config.GetSection("Config").Bind(cfg);
            return cfg;
        }

        public static void Main(string[] args)
        {
            Console.WriteLine("Includes-RPC v5");
            CreateHostBuilder(Array.Empty<string>()).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            Config config = LoadConfig();
            Directory.CreateDirectory(config.LogFilePath);

            //_registry = new IdentityContextRegistry(parsedArgs.DataPathRoot);
            _registry = new IdentityRegistryRpc(config);
            _registry.Initialize();

            return Host.CreateDefaultBuilder(args)
                .ConfigureLogging(logConfig =>
                {
                    logConfig.ClearProviders();
                    logConfig.AddConsole();
                    logConfig.AddFile(Path.Combine(config.LogFilePath, "app_{0:yyyy}-{0:MM}-{0:dd}.log"), opts =>
                    {
                        //opts.FormatLogEntry
                        opts.FormatLogFileName = name => string.Format(name, DateTime.UtcNow);
                    });
                    //config.AddMultiTenantLogger(
                    //        configuration =>
                    //        {
                    //            configuration.LogLevels.Add(LogLevel.Information, ConsoleColor.Gray);
                    //            configuration.LogLevels.Add(LogLevel.Warning, ConsoleColor.DarkMagenta);
                    //            configuration.LogLevels.Add(LogLevel.Error, ConsoleColor.Red);
                    //        });
                })
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
                                opts.ServerCertificateSelector = (connectionContext, hostName) =>
                                {
                                    //Console.WriteLine($"Resolving certificate for host [{hostName}]");
                                    var certInfo = _registry.ResolveCertificate(hostName);
                                    var cert = certInfo.LoadCertificateWithPrivateKey();
                                    return cert;
                                };

                                opts.SslProtocols = SslProtocols.None; //| SslProtocols.Tls13;
//                                opts.AllowAnyClientCertificate();
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