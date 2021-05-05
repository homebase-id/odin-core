using System;
using System.IO;
using System.Runtime.InteropServices;
using DotYou.Kernel.Identity;
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
        private const string LOG_PATH_ENV_NAME = "DOTYOU_LOGPATH";

        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            _registry = new IdentityContextRegistry();
            _registry.Initialize();


            string logPath = FindLogPath();

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
                              var context = _registry.ResolveContext(hostName);
                              var cert = context.TenantCertificate.LoadCertificateWithPrivateKey();
                              return cert;
                          };

                          opts.ClientCertificateMode = ClientCertificateMode.AllowCertificate;
                      });
                  })
                  .UseKestrel() //Use Kestrel to ensure we can run this on linux
                  .UseUrls("http://*:80", "https://*:443") //you need to configure netsh on windows to allow 80 and 443
                  .UseStartup<Startup>();
              });
        }

        private static string FindLogPath()
        {

            string logPath = Environment.GetEnvironmentVariable(LOG_PATH_ENV_NAME);

            if(string.IsNullOrEmpty(logPath) && string.IsNullOrWhiteSpace(logPath))
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
                    throw new InvalidConfigurationException($"No path found at [{logPath}].  Please be sure you have an enviornment variable named [{LOG_PATH_ENV_NAME}] set to an existing path and your app has read/write access to the path.");
                }
            }

            logPath = Path.Combine(logPath, "app_{0:yyyy}-{0:MM}-{0:dd}.log");

            Console.WriteLine($"Logs will be found at [{logPath}]");
            return logPath;

        }
    }
}
