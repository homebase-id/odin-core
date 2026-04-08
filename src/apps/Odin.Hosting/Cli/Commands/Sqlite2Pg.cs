using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.DatabaseImport;
using Odin.Core.Storage.Factory;
using Odin.Services.Configuration;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Tenant.Container;

namespace Odin.Hosting.Cli.Commands;

#nullable enable

public static class Sqlite2Pg
{
    internal static async Task ImportIdentityAsync(
        IServiceProvider services,
        string identityDomain,
        string sourceSqliteSystemDbPath,
        string sourceSqliteIdentityDbPath,
        bool commit
    )
    {
        var logger = services.GetRequiredService<ILogger<CommandLine>>();
        var config = services.GetRequiredService<OdinConfiguration>();
        var workContainer = services.GetRequiredService<IMultiTenantContainer>();

        if (config.Database.Type != DatabaseType.Postgres)
        {
            logger.LogError("Target database type is not PostgreSQL. Current type: {type}", config.Database.Type);
            return;
        }

        if (!File.Exists(sourceSqliteSystemDbPath))
        {
            logger.LogError("SQLite system database file not found: {path}", sourceSqliteSystemDbPath);
            return;
        }

        if (!File.Exists(sourceSqliteIdentityDbPath))
        {
            logger.LogError("SQLite identity database file not found: {path}", sourceSqliteSystemDbPath);
            return;
        }

        //
        // Load system database source and target instances
        //

        await using var sourceSqliteSystemScope = workContainer.BeginLifetimeScope(cb =>
        {
            cb.AddSqliteSystemDatabaseServices(sourceSqliteSystemDbPath);
        });

        await using var targetPgsqlSystemScope = workContainer.BeginLifetimeScope(cb =>
        {
            cb.AddPgsqlSystemDatabaseServices(config.Database.ConnectionString);
        });

        var sourceSystemDb = sourceSqliteSystemScope.Resolve<SystemDatabase>();
        var targetSystemDb = targetPgsqlSystemScope.Resolve<SystemDatabase>();
        var sourceSystemMigrator = sourceSqliteSystemScope.Resolve<SystemMigrator>();
        var targetSystemMigrator = targetPgsqlSystemScope.Resolve<SystemMigrator>();

        //
        // Check system schema versions match
        //

        var sourceVersion = await sourceSystemMigrator.GetCurrentVersionAsync();
        var targetVersion = await targetSystemMigrator.GetCurrentVersionAsync();

        if (sourceVersion != targetVersion)
        {
            throw new InvalidOperationException(
                $"System schema version mismatch: source is at {sourceVersion}, target is at {targetVersion}. " +
                "Both databases must be at the same schema version before importing data.");
        }

        //
        // Find identity in source
        //
        Guid identityId;
        {
            var registrations = await sourceSystemDb.Registrations.GetAllAsync();
            var registration = registrations.FirstOrDefault(r =>
                r.primaryDomainName.Equals(identityDomain, StringComparison.OrdinalIgnoreCase));
            if (registration == null)
            {
                throw new InvalidOperationException(
                    $"Could not find registration for domain {identityDomain} in source SQLite system database.");
            }

            identityId = registration.identityId;
        }

        //
        // Check identity does not already exist in target
        //
        {
            var registrations = await targetSystemDb.Registrations.GetAllAsync();
            var registration = registrations.FirstOrDefault(r =>
                r.identityId == identityId || r.primaryDomainName.Equals(identityDomain, StringComparison.OrdinalIgnoreCase));
            if (registration != null)
            {
                throw new InvalidOperationException(identityDomain +
                    " already exists in target PostgreSQL system database with identity ID " + registration.identityId);
            }
        }

        //
        // Load identity database source and target instances
        //

        await using var sourceSqliteIdentityScope = workContainer.BeginLifetimeScope(cb =>
        {
            cb.RegisterInstance(new OdinIdentity(identityId, identityDomain)).SingleInstance();
            cb.AddSqliteIdentityDatabaseServices(identityId, sourceSqliteIdentityDbPath);
        });

        await using var targetPgsqlIdentityScope = workContainer.BeginLifetimeScope(cb =>
        {
            cb.RegisterInstance(new OdinIdentity(identityId, identityDomain)).SingleInstance();
            cb.AddPgsqlIdentityDatabaseServices(identityId, config.Database.ConnectionString);
        });

        var sourceIdentityDb = sourceSqliteIdentityScope.Resolve<IdentityDatabase>();
        var targetIdentityDb = targetPgsqlIdentityScope.Resolve<IdentityDatabase>();
        var sourceIdentityMigrator = sourceSqliteIdentityScope.Resolve<IdentityMigrator>();
        var targetIdentityMigrator = targetPgsqlIdentityScope.Resolve<IdentityMigrator>();

        //
        // Check identity schema versions match
        //

        sourceVersion = await sourceIdentityMigrator.GetCurrentVersionAsync();
        targetVersion = await targetIdentityMigrator.GetCurrentVersionAsync();

        if (sourceVersion != targetVersion)
        {
            throw new InvalidOperationException(
                $"Identity schema version mismatch: source is at {sourceVersion}, target is at {targetVersion}. " +
                "Both databases must be at the same schema version before importing data.");
        }

        await DataImporter.ImportIdentityAsync(
            logger,
            identityDomain,
            sourceSystemDb,
            targetSystemDb,
            sourceIdentityDb,
            targetIdentityDb,
            commit);
    }

    //
    // Imports the entire SQLite source (system database + every identity database it
    // references) into the configured PostgreSQL target.
    //
    // Identity database paths are derived from the registration record's identity id,
    // following the on-disk convention:
    //
    //   <sourceSqliteTenantsRootPath>/registrations/<identity-id>/headers/identity.db
    //
    internal static async Task ImportAllAsync(
        IServiceProvider services,
        string sourceSqliteSystemDbPath,
        string sourceSqliteTenantsRootPath,
        bool commit
    )
    {
        var logger = services.GetRequiredService<ILogger<CommandLine>>();
        var config = services.GetRequiredService<OdinConfiguration>();
        var workContainer = services.GetRequiredService<IMultiTenantContainer>();

        if (config.Database.Type != DatabaseType.Postgres)
        {
            logger.LogError("Target database type is not PostgreSQL. Current type: {type}", config.Database.Type);
            return;
        }

        if (!File.Exists(sourceSqliteSystemDbPath))
        {
            logger.LogError("SQLite system database file not found: {path}", sourceSqliteSystemDbPath);
            return;
        }

        if (!Directory.Exists(sourceSqliteTenantsRootPath))
        {
            logger.LogError("SQLite tenants root directory not found: {path}", sourceSqliteTenantsRootPath);
            return;
        }

        //
        // Load system database source and target instances
        //

        await using var sourceSqliteSystemScope = workContainer.BeginLifetimeScope(cb =>
        {
            cb.AddSqliteSystemDatabaseServices(sourceSqliteSystemDbPath);
        });

        await using var targetPgsqlSystemScope = workContainer.BeginLifetimeScope(cb =>
        {
            cb.AddPgsqlSystemDatabaseServices(config.Database.ConnectionString);
        });

        var sourceSystemDb = sourceSqliteSystemScope.Resolve<SystemDatabase>();
        var targetSystemDb = targetPgsqlSystemScope.Resolve<SystemDatabase>();
        var sourceSystemMigrator = sourceSqliteSystemScope.Resolve<SystemMigrator>();
        var targetSystemMigrator = targetPgsqlSystemScope.Resolve<SystemMigrator>();

        //
        // Check system schema versions match
        //

        var sourceSystemVersion = await sourceSystemMigrator.GetCurrentVersionAsync();
        var targetSystemVersion = await targetSystemMigrator.GetCurrentVersionAsync();

        if (sourceSystemVersion != targetSystemVersion)
        {
            throw new InvalidOperationException(
                $"System schema version mismatch: source is at {sourceSystemVersion}, target is at {targetSystemVersion}. " +
                "Both databases must be at the same schema version before importing data.");
        }

        //
        // Build the list of identities from source registrations
        //

        var registrations = await sourceSystemDb.Registrations.GetAllAsync();
        logger.LogInformation("Found {count} registration(s) in source SQLite system database", registrations.Count);

        //
        // Resolve and verify each identity database file before touching the target
        //

        var identityJobs = registrations
            .Select(r => new
            {
                r.identityId,
                identityDomain = r.primaryDomainName,
                identityDbPath = Path.Combine(
                    sourceSqliteTenantsRootPath,
                    TenantPathManager.RegistrationsFolder,
                    r.identityId.ToString(),
                    TenantPathManager.HeadersFolder,
                    "identity.db"),
            })
            .ToList();

        foreach (var job in identityJobs)
        {
            if (!File.Exists(job.identityDbPath))
            {
                throw new InvalidOperationException(
                    $"SQLite identity database file not found for {job.identityDomain} ({job.identityId}): {job.identityDbPath}");
            }
        }

        //
        // Import all system tables
        //

        await DataImporter.ImportAllSystemDataAsync(logger, sourceSystemDb, targetSystemDb, commit);

        //
        // Import each identity database
        //

        var identityIndex = 0;
        foreach (var job in identityJobs)
        {
            identityIndex++;
            logger.LogInformation("[{index}/{total}] Importing identity {identityDomain} ({identityId})",
                identityIndex, identityJobs.Count, job.identityDomain, job.identityId);

            await using var sourceSqliteIdentityScope = workContainer.BeginLifetimeScope(cb =>
            {
                cb.RegisterInstance(new OdinIdentity(job.identityId, job.identityDomain)).SingleInstance();
                cb.AddSqliteIdentityDatabaseServices(job.identityId, job.identityDbPath);
            });

            await using var targetPgsqlIdentityScope = workContainer.BeginLifetimeScope(cb =>
            {
                cb.RegisterInstance(new OdinIdentity(job.identityId, job.identityDomain)).SingleInstance();
                cb.AddPgsqlIdentityDatabaseServices(job.identityId, config.Database.ConnectionString);
            });

            var sourceIdentityDb = sourceSqliteIdentityScope.Resolve<IdentityDatabase>();
            var targetIdentityDb = targetPgsqlIdentityScope.Resolve<IdentityDatabase>();
            var sourceIdentityMigrator = sourceSqliteIdentityScope.Resolve<IdentityMigrator>();
            var targetIdentityMigrator = targetPgsqlIdentityScope.Resolve<IdentityMigrator>();

            var sourceIdentityVersion = await sourceIdentityMigrator.GetCurrentVersionAsync();
            var targetIdentityVersion = await targetIdentityMigrator.GetCurrentVersionAsync();

            if (sourceIdentityVersion != targetIdentityVersion)
            {
                throw new InvalidOperationException(
                    $"Identity schema version mismatch for {job.identityDomain}: source is at {sourceIdentityVersion}, target is at {targetIdentityVersion}. " +
                    "Both databases must be at the same schema version before importing data.");
            }

            await DataImporter.ImportIdentityOnlyAsync(
                logger,
                job.identityDomain,
                sourceIdentityDb,
                targetIdentityDb,
                commit);
        }
    }

}
