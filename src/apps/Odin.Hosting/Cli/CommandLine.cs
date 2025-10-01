using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Odin.Core.Configuration;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Tasks;
using Odin.Hosting.Cli.Commands;
using Odin.Services.Configuration;
using Odin.Services.Registry;
using Odin.Services.Tenant.Container;
using Odin.Services.Util;

namespace Odin.Hosting.Cli;

#nullable enable

public class CommandLine
{
    private static ServiceProviders _serviceProviders = null!;
    private static IServiceProvider _serviceProvider = null!; // Convenience for the root service provider
    private static MultiTenantContainer _multiTenantContainer = null!; // Convenience for the root Autofac container
    private static OdinConfiguration _config = null!;
    private static ILogger<CommandLine> _logger = null!;

    public static (bool didHandle, int exitCode) HandleCommandLineArgs(string[]? args)
    {
        if (args == null || args.Length == 0)
        {
            return (false, 0);
        }

        (_config, _) = AppSettings.LoadConfig(true);
        _config.BackgroundServices.SystemBackgroundServicesEnabled = false;
        _config.BackgroundServices.TenantBackgroundServicesEnabled = false;

        _serviceProviders = ServiceProviders.Create(
            sc =>
            {
                sc.AddCommandLineLogging(commandLineOnly: !args.Contains("--verbose"), minimumLevel: LogLevel.Debug);
                sc.ConfigureSystemServices(_config);
            },
            cb =>
            {
                cb.ConfigureSystemServices(_config);
            });
        _serviceProvider = _serviceProviders.ServiceProvider;
        _multiTenantContainer = _serviceProviders.MultiTenantContainer;

        _logger = _serviceProviders.MultiTenantContainer.Resolve<ILogger<CommandLine>>();
        try
        {
            return ParseAndExecute(args.Where(x => x != "--verbose").ToArray());
        }
        finally
        {
            _serviceProviders.Dispose();
        }
    }

    //

    private static List<IdentityRegistration> LoadTenants()
    {
        var registry = _serviceProvider.GetRequiredService<IIdentityRegistry>();
        registry.LoadRegistrations().BlockingWait();
        return registry.GetTenants().Result;
    }

    //

    private static ILifetimeScope GetTenantScope(string tenantId)
    {
        var tenantContainer = _multiTenantContainer.Resolve<IMultiTenantContainer>();
        return tenantContainer.GetTenantScope(tenantId);
    }

    //

    private static ILifetimeScope GetTenantScope(IdentityRegistration tenant)
    {
        return GetTenantScope(tenant.PrimaryDomainName);
    }

    //

    private static (bool didHandle, int exitCode) ParseAndExecute(string[] args)
    {
        if (args is ["dependency-demo"])
        {
            _logger.LogInformation("Dependency demo");

            // Show that we can resolve a system service
            {
                var jobs = _serviceProvider.GetRequiredService<TableJobs>();
                var jobCount = jobs.GetCountAsync().Result;
                _logger.LogInformation("Found {JobCount} jobs in the scheduler", jobCount);
            }

            // Show that we can resolve a tenant service
            {
                foreach (var tenant in LoadTenants())
                {
                    var scope = GetTenantScope(tenant);
                    var drives = scope.Resolve<TableDrivesCached>();
                    var driveCount = drives.GetCountAsync().Result;
                    _logger.LogInformation("Found {DriveCount} drives on {tenant}", driveCount, tenant.PrimaryDomainName);
                }
            }

            return (true, 0);
        }

        //
        // Command line: run docker setup helper
        //
        // Example:
        //   dotnet run -- docker-setup foo=bar
        //
        // "commandLineArgs": "docker-setup config-file=appsettings.table-top-defaults.json default-root-dir=/opt/homebase"
        //
        if (args.Length > 0 && args[0] == "docker-setup")
        {
            var result = DockerSetup.Execute(args);
            return (true, result);
        }

        //
        // Command line: export docker env config
        //
        //
        // Example:
        //   dotnet run --no-build -- export-docker-env
        //
        if (args.Length > 0 && args[0] == "export-docker-env")
        {
            var (_, appSettingsConfig) = AppSettings.LoadConfig(false);
            var envVars = appSettingsConfig.ExportAsEnvironmentVariables();
            foreach (var envVar in envVars)
            {
                Console.WriteLine($@"--env {envVar} \");
            }

            return (true, 0);
        }

        //
        // Command line: export shell env config
        //
        //
        // Example:
        //   dotnet run --no-build -- export-shell-env
        //
        if (args.Length > 0 && args[0] == "export-shell-env")
        {
            var (_, appSettingsConfig) = AppSettings.LoadConfig(false);
            var envVars = appSettingsConfig.ExportAsEnvironmentVariables();
            foreach (var envVar in envVars)
            {
                Console.WriteLine($"export {envVar}");
            }

            return (true, 0);
        }

        //
        // Command line: export shell env config as bash array
        //
        //
        // Example:
        //   dotnet run --no-build -- export-shell-env
        //
        if (args.Length > 0 && args[0] == "export-bash-array-env")
        {
            var (_, appSettingsConfig) = AppSettings.LoadConfig(false);
            var envVars = appSettingsConfig.ExportAsEnvironmentVariables();
            Console.WriteLine("env_vars=(");
            foreach (var envVar in envVars)
            {
                Console.WriteLine($"  \"{envVar}\"");
            }

            Console.WriteLine(")");
            Console.WriteLine(
                """
                for env_var in "${env_vars[@]}"; do
                  echo $env_var
                done
                """);
            return (true, 0);
        }

        //
        // Command line: dump environment variables
        //
        // examples:
        //
        //   FOO=BAR dotnet run --no-build -- dump-env
        //
        //   ASPNETCORE_ENVIRONMENT=Production ./Odin.Hosting dump-env
        //
        //
        if (args.Length > 0 && args[0] == "dump-env")
        {
            var (_, appSettingsConfig) = AppSettings.LoadConfig(true);
            var envVars = appSettingsConfig.ExportAsEnvironmentVariables();
            foreach (var envVar in envVars)
            {
                Console.WriteLine(envVar);
            }

            return (true, 0);
        }

        //
        // Command line: start connection test
        //
        // examples:
        //
        //   dotnet run -- tcp-connection-test 80 5000
        //
        //   80: TCP port to listen on
        //   5000: timeout in milliseconds before giving up
        //
        //   ASPNETCORE_ENVIRONMENT=Production ./Odin.Hosting tcp-connection-test 80 5000
        //
        //
        if (args.Length > 2 && args[0] == "tcp-connection-test")
        {
            var port = int.Parse(args[1]);
            var timeout = int.Parse(args[2]);
            using var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            Console.WriteLine($"Listening on port {port} for {timeout} ms");
            var task = listener.AcceptTcpClientAsync();
            var result = task.Wait(timeout);
            if (result)
            {
                Console.WriteLine("Connection established");
                return (true, 0);
            }

            Console.WriteLine("Connection timed out");
            return (true, 1);
        }

        //
        // Command line: Defragment
        //
        // examples:
        //   dotnet run -- defragment just-looking
        //   dotnet run -- defragment cleanup
        //
        if (args.Length > 1 && args[0] == "defragment")
        {
            Defragment.ExecuteAsync(_serviceProvider, args[1] == "cleanup").BlockingWait();
            return (true, 0);
        }

        //
        // Command line: Reset Feed
        //
        // examples:
        //   dotnet run -- reset-feed
        //
        if (args.Length > 0 && args[0] == "reset-feed")
        {
            ResetFeed.ExecuteAsync(_serviceProvider).BlockingWait();
            return (true, 0);
        }

        //
        // Command line: Create identity FOR TESTING ONLY
        //
        // examples:
        //   dotnet run -- create-test-identity 11111111-1111-1111-1111-111111111111 example.com
        //
        //
        if (args.Length > 0 && args[0] == "create-test-identity")
        {
            CreateTestIdentity.ExecuteAsync(_serviceProvider, Guid.Parse(args[1]), args[2]).BlockingWait();
            return (true, 0);
        }

        //
        // Command line: Reset Modified
        //
        // examples:
        //   dotnet run -- reset-feed
        //
        if (args.Length > 0 && args[0] == "reset-modified")
        {
            ResetModified.ExecuteAsync(_serviceProvider).BlockingWait();
            return (true, 0);
        }

        //
        // Command line: Log tenant versions
        //
        // examples:
        //   dotnet run -- log-tenant-versions
        //
        if (args.Length > 0 && args[0] == "log-tenant-versions")
        {
            LogTenantVersions.ExecuteAsync(_serviceProvider).BlockingWait();
            return (true, 0);
        }

        //
        // Command line:
        //
        // examples:
        //   dotnet run -- import-static-files
        //
        if (args.Length > 0 && args[0] == "import-static-files")
        {
            ImportStaticFiles.ExecuteAsync(_serviceProvider).BlockingWait();
            return (true, 0);
        }

        return (false, 0);

    }
}

