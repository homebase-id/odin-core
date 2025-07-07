using System.Diagnostics;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Odin.Core.Exceptions;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Logging.CorrelationId.Serilog;
using Odin.Core.Logging.Hostname;
using Odin.Core.Logging.Hostname.Serilog;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;
using WaitingListApi.Config;

#nullable enable

namespace WaitingListApi
{
    public static class Program
    {
        private const string
            LogOutputTemplate = "{Timestamp:o} {Level:u3} {CorrelationId} {Hostname} {Message:lj}{NewLine}{Exception}"; // Add {SourceContext} to see source

        private static readonly object FileMutex = new();

        private static readonly SystemConsoleTheme LogOutputTheme = SystemConsoleTheme.Literate;

        public static int Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .Enrich.WithHostname(new StickyHostnameGenerator())
                .Enrich.WithCorrelationId(new CorrelationUniqueIdGenerator())
                .WriteTo.Console(outputTemplate: LogOutputTemplate, theme: LogOutputTheme)
                .CreateBootstrapLogger();

            try
            {
                Log.Information("Starting waiting list web host");
                CreateHostBuilder(args).Build().Run();
                Log.Information("Stopped waiting list web host");
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

        public static (WaitingListConfig, IConfiguration) LoadConfig()
        {
            const string configPathOverrideVariable = "ODIN_CONFIG_PATH";

            var cfgPathOverride = Environment.GetEnvironmentVariable(configPathOverrideVariable);
            var configFolder = string.IsNullOrEmpty(cfgPathOverride) ? Environment.CurrentDirectory : cfgPathOverride;
            Log.Information($"Looking for configuration in folder: {configFolder}");

            const string envVar = "ASPNETCORE_ENVIRONMENT";
            var env = Environment.GetEnvironmentVariable(envVar) ?? "";

            if (string.IsNullOrEmpty(env))
            {
                throw new OdinSystemException($"You must set an environment variable named [{envVar}] which specifies your environment.\n" +
                                              $"This must match your app settings file as follows 'appsettings.ENV.json'");
            }

            var appSettingsFile = $"appsettings.{env.ToLower()}.json";
            var configPath = Path.Combine(configFolder, appSettingsFile);
            
            if (!File.Exists(configPath))
            {
                throw new OdinSystemException($"Could not find configuration file [{configPath}]");
            }
            
            Log.Information($"Loading configuration at [{configPath}]");

            var config = new ConfigurationBuilder()
                .AddJsonFile(configPath, optional: false)
                .AddEnvironmentVariables()
                .Build();

            return (new WaitingListConfig(config), config);
        }

        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            var (waitingListConfig, appSettingsConfig) = LoadConfig();

            var loggingDirInfo = Directory.CreateDirectory(waitingListConfig.Logging.LogFilePath);
            if (!loggingDirInfo.Exists)
            {
                throw new OdinClientException($"Could not create logging folder at [{waitingListConfig.Logging.LogFilePath}]");
            }

            var dataRootDirInfo = Directory.CreateDirectory(waitingListConfig.Host.SystemDataRootPath);
            if (!dataRootDirInfo.Exists)
            {
                throw new OdinClientException($"Could not create data folder at [{waitingListConfig.Host.SystemDataRootPath}]");
            }

            var builder = Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration(builder => { builder.AddConfiguration(appSettingsConfig); })
                .UseSystemd()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.ConfigureKestrel(kestrelOptions =>
                        {
                            kestrelOptions.Limits.MaxRequestBodySize = null;

                            foreach (var address in waitingListConfig.Host.IPAddressListenList)
                            {
                                var ip = address.GetIp();
                                kestrelOptions.Listen(ip, address.HttpPort);
                                kestrelOptions.Listen(ip, address.HttpsPort,
                                    options => ConfigureHttpListenOptions(waitingListConfig, kestrelOptions, options));
                            }
                        })
                        .UseStartup<Startup>();
                });

            if (waitingListConfig.Logging.Level == LoggingLevel.ErrorsOnly)
            {
                builder.UseSerilog((context, services, configuration) => configuration
                    .ReadFrom.Services(services)
                    .MinimumLevel.Error());

                return builder;
            }

            if (waitingListConfig.Logging.Level == LoggingLevel.Verbose)
            {
                builder.UseSerilog((context, services, configuration) => configuration
                    .ReadFrom.Services(services)
                    .MinimumLevel.Debug()
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                    .MinimumLevel.Override("Microsoft.AspNetCore.Authentication", LogEventLevel.Error)
                    .MinimumLevel.Override("Odin.Hosting.Middleware.Logging.RequestLoggingMiddleware", LogEventLevel.Information)
                    .Enrich.FromLogContext()
                    .Enrich.WithHostname(new StickyHostnameGenerator())
                    .Enrich.WithCorrelationId(new CorrelationUniqueIdGenerator())
                    .WriteTo.Async(sink => sink.Console(outputTemplate: LogOutputTemplate, theme: LogOutputTheme))
                    .WriteTo.Async(sink =>
                        sink.RollingFile(Path.Combine(waitingListConfig.Logging.LogFilePath, "app-{Date}.log"), outputTemplate: LogOutputTemplate)));

                return builder;
            }

            return builder;
        }

        //

        private static void ConfigureHttpListenOptions(
            WaitingListConfig odinConfig,
            KestrelServerOptions kestrelOptions,
            ListenOptions listenOptions)
        {
            var handshakeTimeoutTimeSpan = Debugger.IsAttached
                ? TimeSpan.FromMinutes(60)
                : TimeSpan.FromSeconds(60);

            listenOptions.UseHttps(async (stream, clientHelloInfo, state, cancellationToken) =>
            {
                // SEB:NOTE ToLower() should not be needed here, but better safe than sorry.
                var hostName = clientHelloInfo.ServerName.ToLower();

                var serviceProvider = kestrelOptions.ApplicationServices;
                var cert = await ServerCertificateSelector(hostName, odinConfig, serviceProvider);

                if (cert == null)
                {
                    // This is an escape hatch so runtime won't log an error
                    // when no certificate could be found
                    throw new ConnectionAbortedException();
                }

                var result = new SslServerAuthenticationOptions
                {
                    ServerCertificate = cert
                };

                return result;
            }, state: null!, handshakeTimeoutTimeSpan);
        }

        //         

        private static Task<X509Certificate2> ServerCertificateSelector(
            string hostName,
            WaitingListConfig config,
            IServiceProvider serviceProvider)
        {
            if (string.IsNullOrWhiteSpace(hostName))
            {
                throw new Exception("No host name specified");
            }

            var certificate = LoadFromFile(Path.Combine(config.Host.SystemSslRootPath, hostName));
            if (null == certificate)
            {
                throw new Exception("No certificate configured");
            }

            return Task.FromResult(certificate);
        }

        //

        private static X509Certificate2? LoadFromFile(string certificateRoot)
        {
            string certificatePemPath = Path.Combine(certificateRoot, "certificate.crt");
            string keyPemPath = Path.Combine(certificateRoot, "private.key");

            string certPem;
            string keyPem;
            lock (FileMutex)
            {
                if (!File.Exists(certificatePemPath) || !File.Exists(keyPemPath))
                {
                    return null;
                }

                certPem = File.ReadAllText(certificatePemPath);
                keyPem = File.ReadAllText(keyPemPath);
            }

            var x509 = X509Certificate2.CreateFromPem(certPem, keyPem);

            return x509;
        }
    }
}