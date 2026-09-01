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
using Odin.Hosting.Cli.Commands.ClientTokenRegistrationUpgrade;
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
        // Command line: Export one identity's tables to a single JSON file
        //
        // THE HOST MUST BE STOPPED. Nothing here can stop a running host's tenant
        // background workers: they live in that host's container, and on more than one
        // host they are out of reach entirely. Exporting underneath them silently loses
        // whatever they commit after the snapshot, so the export aborts if it can see a
        // host listening on the configured ports. That probe is local-only; see
        // IdentityJsonTransfer.HostIsStopped and HostLivenessCheck for what it cannot catch.
        //
        // S3 PAYLOADS ONLY. Payloads are not in the file and move separately, which today
        // means a copy between S3 buckets, so a disk-based host is refused outright. See
        // IdentityJsonTransfer.PayloadsAreOnS3.
        //
        // The file contains key material; see the warning it prints.
        //
        // examples:
        //   dotnet run -- identity-export frodo.dotyou.cloud /path/to/frodo.json
        //
        if (args.Length >= 1 && args[0] == "identity-export")
        {
            var operands = args.Skip(1).Where(a => !a.StartsWith("--")).ToList();
            var flags = args.Skip(1).Where(a => a.StartsWith("--")).ToList();

            if (flags.Count > 0)
            {
                _logger.LogError("Unknown option(s): {options}", string.Join(", ", flags));
                return (true, 1);
            }

            if (operands.Count != 2)
            {
                _logger.LogError("Usage: identity-export <domain> <file.json>");
                return (true, 1);
            }

            var exported = IdentityJsonTransfer.ExportAsync(
                _serviceProvider, operands[0], operands[1]).BlockingWait();
            return (true, exported ? 0 : 1);
        }

        //
        // Command line: Import an identity export file
        //
        // Refuses unless the target is empty of this identity and every table version
        // matches. Dry run unless "commit" is passed. Like export, this aborts if a host
        // is listening: the import writes the shared system tables, and a running host
        // would neither see the new identity nor expect its registration to appear. Also
        // like export, the target host must keep payloads on S3, since that is the only
        // place the payloads can be copied to. See IdentityJsonTransfer.PayloadsAreOnS3.
        //
        // examples:
        //   dotnet run -- identity-import /path/to/frodo.json commit
        //
        if (args.Length >= 1 && args[0] == "identity-import")
        {
            var operands = args.Skip(1).Where(a => !a.StartsWith("--")).ToList();
            var flags = args.Skip(1).Where(a => a.StartsWith("--")).ToList();

            if (flags.Count > 0)
            {
                _logger.LogError("Unknown option(s): {options}", string.Join(", ", flags));
                return (true, 1);
            }

            // "commit" is a positional keyword, not a flag: anything else after the path
            // is a typo, and treating a typo as a dry run would be the wrong way to fail.
            if (operands.Count < 1 || operands.Count > 2 ||
                (operands.Count == 2 && operands[1] != "commit"))
            {
                _logger.LogError("Usage: identity-import <file.json> [commit]");
                return (true, 1);
            }

            var commit = operands.Count == 2;
            var imported = IdentityJsonTransfer.ImportAsync(_serviceProvider, operands[0], commit).BlockingWait();
            return (true, imported ? 0 : 1);
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
        // Command line: Upgrades server side aspects of CAT registration
        //
        // examples:
        //   dotnet run -- kitty-upgrades
        //
        if (args.Length > 0 && args[0] == "kitty-upgrades")
        {
            UpgradeClientRegistrations.ExecuteAsync(_serviceProvider).BlockingWait();
            return (true, 0);
        }

        //
        // Command line: Migrate identity data from SQLite to PostgreSQL
        //
        // The system must be configured for PostgreSQL (target).
        // Arguments:
        // - Domain name to import
        // - System SQLite database file to import from
        // - Per-identity SQLite database file to import from
        // Fails if the identity already exists in the registry.
        //
        // examples:
        //   dotnet run -- sqlite2pg-identity frodo.dotyou.cloud /path/to/src/system.db /path/to/src/identity.db commit
        //
        if (args.Length >= 5 && args[0] == "sqlite2pg-identity")
        {
            Sqlite2Pg.ImportIdentityAsync(_serviceProvider, args[1], args[2], args[3], args[4] == "commit").BlockingWait();
            return (true, 0);
        }

        //
        // Command line: Migrate ALL system + identity data from SQLite to PostgreSQL
        //
        // The system must be configured for PostgreSQL (target).
        // Arguments:
        // - System SQLite database file to import from
        // - SQLite tenants root directory (the parent of the "registrations" folder).
        //   Each identity database is expected at:
        //     <tenants-root>/registrations/<identity-id>/headers/identity.db
        // - "commit" to apply, anything else for a dry run
        //
        // examples:
        //   dotnet run -- sqlite2pg-all /path/to/src/sys.db /path/to/src/tenants commit
        //
        if (args.Length >= 4 && args[0] == "sqlite2pg-all")
        {
            Sqlite2Pg.ImportAllAsync(_serviceProvider, args[1], args[2], args[3] == "commit").BlockingWait();
            return (true, 0);
        }

        //
        // Command line: Repair created/modified columns on already-imported PG rows
        //
        // DataImporter.InsertAsync paths replace source created/modified with NOW()
        // because the CRUD INSERT SQL hard-codes those columns. This command re-reads
        // the SQLite source and UPDATEs the target rows with the original values,
        // honouring DataImportPatcher.DefaultCutoffUtc so post-import edits aren't
        // reverted.
        //
        // The system must be configured for PostgreSQL (target).
        // Arguments:
        // - System SQLite database file to read from
        // - SQLite tenants root directory (same layout as sqlite2pg-all)
        // - "commit" to apply, anything else for a dry run
        //
        // examples:
        //   dotnet run -- sqlite2pg-patch-timestamps /path/to/src/sys.db /path/to/src/tenants commit
        //
        if (args.Length >= 4 && args[0] == "sqlite2pg-patch-timestamps")
        {
            Sqlite2Pg.PatchAllTimestampsAsync(_serviceProvider, args[1], args[2], args[3] == "commit").BlockingWait();
            return (true, 0);
        }

        //
        // Command line: Ping configured PG database connection
        //
        // examples:
        //   dotnet run -- pg-ping
        //
        if (args.Length >= 1 && args[0] == "pg-ping")
        {
            PgPing.ExecuteAsync(_serviceProvider).BlockingWait();
            return (true, 0);
        }

        //
        // Command line: Ping configured redis database connection
        //
        // examples:
        //   dotnet run -- redis-ping
        //
        if (args.Length >= 1 && args[0] == "redis-ping")
        {
            RedisPing.ExecuteAsync(_serviceProvider).BlockingWait();
            return (true, 0);
        }

        //
        // Command line: Create CDN CAT
        //
        // examples:
        //   dotnet run -- create-cdn-cat
        //
        if (args.Length > 0 && args[0] == "create-cdn-cat")
        {
            CreateCdnCat.ExecuteAsync(_serviceProvider);
            return (true, 0);
        }

        //
        // Command line: Backfill pre-provisioned DNS zones for existing own-domain identities
        //
        // Dry-run unless "commit" is passed.
        //
        // examples:
        //   dotnet run -- create-own-domain-zones
        //   dotnet run -- create-own-domain-zones commit
        //
        if (args.Length > 0 && args[0] == "create-own-domain-zones")
        {
            OwnDomainZones.CreateAsync(_serviceProvider, args.Length > 1 && args[1] == "commit").BlockingWait();
            return (true, 0);
        }

        //
        // Print the DNSSEC verdict for every own-domain identity (read-only).
        //
        // example:
        //   dotnet run -- own-domain-dnssec-status
        //
        if (args.Length > 0 && args[0] == "own-domain-dnssec-status")
        {
            OwnDomainZones.DnssecStatusAsync(_serviceProvider).BlockingWait();
            return (true, 0);
        }

        //
        // Command line: (Re)write managed-domain records in the shared apex zones - the
        // managed-domain counterpart of create-own-domain-zones (applies new record types,
        // e.g. the email records, to existing tenants).
        //
        // Dry-run unless "commit" is passed.
        //
        // examples:
        //   dotnet run -- populate-managed-domain-records
        //   dotnet run -- populate-managed-domain-records commit
        //
        if (args.Length > 0 && args[0] == "populate-managed-domain-records")
        {
            ManagedDomainRecords.PopulateAsync(_serviceProvider, args.Length > 1 && args[1] == "commit").BlockingWait();
            return (true, 0);
        }


        
        return (false, 0);
    }
}

