using System;
using System.IO;
using System.Runtime.InteropServices;
using DotYou.DigitalIdentityHost;
using DotYou.IdentityRegistry;
using DotYou.TenantHost.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;


namespace DotYou.TenantHost
{
    public class Program
    {
        private static IIdentityContextRegistry _registry;

        public static void Main(string[] args)
        {
            const string logPathEnvName = "DOTYOU_LOGPATH";
            const string dataRootPath = "DATA_ROOT_PATH";
            string path = Environment.GetEnvironmentVariable(dataRootPath);
            string logPath = Environment.GetEnvironmentVariable(logPathEnvName);

            // Console.WriteLine("Start Paths");
            // Console.WriteLine($"DATA_ROOT_PATH={path}");
            // Console.WriteLine($"DOTYOU_LOGPATH={logPath}");
            // Console.WriteLine("End Paths");

            path ??= "\\srv\\data\\tenants";
            logPath ??= "\\srv\\data\\logs";
            
            //HACK: need a centralized method to handle paths by operating system
            path = (path ?? "").Replace('\\', Path.DirectorySeparatorChar);
            logPath = (logPath ?? "").Replace('\\', Path.DirectorySeparatorChar);

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            if (!Directory.Exists(logPath))
            {
                Directory.CreateDirectory(logPath);
            }

            var newargs = new[] {path, logPath};

            CreateHostBuilder(newargs).Build().Run();
        }

        private static Args ParseArgs(string[] args)
        {
            if (args.Length != 2)
            {
                throw new InvalidDataException(
                    "Args are invalid.  the first must be the DataPathRoot indicating where to store tenant data and the second must be LogPath for your log files.");
            }

            var parsed = new Args()
            {
                DataPathRoot = args[0],
                LogFilePath = args[1]
            };

            if (Directory.Exists(parsed.DataPathRoot) && Directory.Exists(parsed.LogFilePath))
            {
                return parsed;
            }

            throw new InvalidDataException(
                $"Could not find or access the DatPathRoot at [{parsed.DataPathRoot}] or the LogFilePath at [{parsed.LogFilePath}].  The directories must exist and be accessible to the process.");
        }

        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            var parsedArgs = ParseArgs(args);

            _registry = new IdentityContextRegistry(parsedArgs.DataPathRoot);
            _registry.Initialize();

            string logPath = FindLogPath(parsedArgs);

            return Host.CreateDefaultBuilder(args)
                .ConfigureLogging(config =>
                {
                    config.ClearProviders();
                    config.AddConsole();
                    config.AddFile(logPath, opts =>
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
                                    var context = _registry.ResolveContext(hostName);
                                    var cert = context.TenantCertificate.LoadCertificateWithPrivateKey();
                                    return cert;
                                };

                                opts.ClientCertificateMode = ClientCertificateMode.AllowCertificate;
                            });
                        })
                        .UseKestrel() //Use Kestrel to ensure we can run this on linux
                        .UseUrls("https://*:443") //you need to configure netsh on windows to allow 443
                        .UseStartup<Startup>();
                });
        }

        private static string FindLogPath(Args args)
        {
            string logPath = args.LogFilePath;

            if (string.IsNullOrEmpty(logPath) && string.IsNullOrWhiteSpace(logPath))
            {
                var isUnixBased = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
                                  RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD) ||
                                  RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

                logPath = isUnixBased ? "/tmp/dotyoulogs" : "\\temp\\dotyoulogs";
            }
            else
            {
                //if an env var is set it must be a valid path.
                if (!Directory.Exists(logPath))
                {
                    throw new InvalidConfigurationException(
                        $"No path found at [{logPath}].  Please be sure your app has read/write access to the path.");
                }
            }

            logPath = Path.Combine(logPath, "app_{0:yyyy}-{0:MM}-{0:dd}.log");

            Console.WriteLine($"Logs will be found at [{logPath}]");
            return logPath;
        }
    }

    internal class Args
    {
        public string DataPathRoot { get; set; }
        public string LogFilePath { get; set; }
    }
}