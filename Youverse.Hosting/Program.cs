using System;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Youverse.Core.Services.Registry;
using Youverse.Core.Util;
using Youverse.Hosting.IdentityRegistry;

namespace Youverse.Hosting
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
            CreateHostBuilder(Array.Empty<string>()).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            Config config = LoadConfig();

            //HACK: overriding the data and log file paths so the test runner can set the path.  need to overhaul this config loading process
            if (args.Length >= 1)
            {
                config.TenantDataRootPath = args[0];
            }

            if (args.Length >= 2)
            {
                config.TempTenantDataRootPath = args[1];
            }

            if (args.Length == 3)
            {
                config.LogFilePath = args[2];
            }

            Directory.CreateDirectory(config.LogFilePath);
            Directory.CreateDirectory(config.TenantDataRootPath);
            Directory.CreateDirectory(config.TempTenantDataRootPath);

            var useLocalReg = Environment.GetEnvironmentVariable("USE_LOCAL_DOTYOU_CERT_REGISTRY", EnvironmentVariableTarget.Process) == "1";
            if (useLocalReg)
            {
                _registry = new IdentityContextRegistry(config.TenantDataRootPath, config.TempTenantDataRootPath);
            }
            else
            {
                Console.WriteLine("Using IdentityRegistryRpc");
                _registry = new IdentityRegistryRpc(config);
            }

            _registry.Initialize();

            return Host.CreateDefaultBuilder(args)
                .ConfigureLogging(logConfig =>
                {
                    logConfig.ClearProviders();
                    logConfig.AddConsole();
                    logConfig.AddFile(PathUtil.Combine(config.LogFilePath, "app_{0:yyyy}-{0:MM}-{0:dd}.log"), opts =>
                    {
                        opts.FormatLogEntry = entry => $"{entry.LogName}:{entry.EventId}\t{entry.Message}\nException: {entry.Exception}";
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
                                        //Console.WriteLine($"Resolving certificate for host [{hostName}]");
                                        var certInfo = _registry.ResolveCertificate(hostName);
                                        var cert = certInfo.LoadCertificateWithPrivateKey();

                                        if (null == cert)
                                        {
                                            //TODO: add logging or throw exception
                                            Console.WriteLine($"No certificate configured for {hostName}");
                                        }

                                        return cert;
                                    }

                                    //Console.WriteLine($"Received request with a hostname.  Request Host is [{connectionContext.GetHttpContext()?.Request.Host}]");
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