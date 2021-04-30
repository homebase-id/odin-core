using DotYou.TenantHost.Certificate;
using DotYou.TenantHost.Certificate.FileBased;
using DotYou.TenantHost.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DotYou.TenantHost
{
    public class Program
    {
        static ICertificateResolver _certificateResolver;

        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
            _certificateResolver = new FilebasedBasedCertificateResolver();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
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
                        opts.ServerCertificateSelector = (connectionContext, serverName) =>
                        {
                            return _certificateResolver.Resolve(serverName);
                        };
                    });
                })
                .UseKestrel() //Use Kestrel to ensure we can run this on linux
                .UseUrls("http://*:80", "https://*:443") //you need to configure netsh on windows to allow 80 and 443
                .UseStartup<Startup>();
            });
    }
}
