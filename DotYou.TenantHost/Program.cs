using DotYou.Kernel.Identity;
using DotYou.TenantHost.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;

namespace DotYou.TenantHost
{

    public class Program
    {
        static ICertificateResolver _certificateResolver;
        static IdentityContextRegistry _registry;

        public static void Main(string[] args)
        {
            _certificateResolver = new ContextBasedCertificateResolver();

            _registry = new IdentityContextRegistry();

            //TODO:load the registry
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args)
        {
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
                              return _certificateResolver.Resolve(context);
                          };
                      });
                  })
                  .UseKestrel() //Use Kestrel to ensure we can run this on linux
                  .UseUrls("http://*:80", "https://*:443") //you need to configure netsh on windows to allow 80 and 443
                  .UseStartup<Startup>();
              });
        }
    }
}
