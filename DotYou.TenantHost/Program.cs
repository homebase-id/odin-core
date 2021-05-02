using System;
using DotYou.Kernel.Identity;
using DotYou.TenantHost.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;


namespace DotYou.TenantHost
{
    public class Program
    {
        private static IdentityContextRegistry _registry;

        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            _registry = new IdentityContextRegistry();
            _registry.Initialize();

            return Host.CreateDefaultBuilder(args)
              .ConfigureLogging(config =>
              {
                  config.ClearProviders();
                  config.AddMultiTenantLogger(
                          configuration =>
                          {
                              configuration.LogLevels.Add(LogLevel.Information, ConsoleColor.Gray);
                              configuration.LogLevels.Add(LogLevel.Warning, ConsoleColor.DarkMagenta);
                              configuration.LogLevels.Add(LogLevel.Error, ConsoleColor.Red);
                          });
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
                              var cert = new ContextBasedCertificateResolver().Resolve(context);
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
    }
}
