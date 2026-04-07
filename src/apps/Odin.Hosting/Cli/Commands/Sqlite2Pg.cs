using System;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.Database.System.Connection;
using Odin.Core.Storage.DatabaseImport;
using Odin.Core.Storage.Factory;
using Odin.Core.Tasks;
using Odin.Services.Configuration;
using Odin.Services.Registry;
using Odin.Services.Tenant.Container;

namespace Odin.Hosting.Cli.Commands;

#nullable enable

public static class Sqlite2Pg
{
    internal static async Task ImportIdentityAsync(
        IServiceProvider services,
        string identityDomain,
        string sourceSqliteSystemDbPath,
        string sourceSqliteIdentityDbPath
    )
    {
        var logger = services.GetRequiredService<ILogger<CommandLine>>();
        var config = services.GetRequiredService<OdinConfiguration>();
        var registry = services.GetRequiredService<IIdentityRegistry>();
        var workContainer = services.GetRequiredService<IMultiTenantContainer>();

        if (config.Database.Type != DatabaseType.Postgres)
        {
            logger.LogError("Target database type is not PostgreSQL. Current type: {type}", config.Database.Type);
            return;
        }

        if (!System.IO.File.Exists(sourceSqliteSystemDbPath))
        {
            logger.LogError("SQLite system database file not found: {path}", sourceSqliteSystemDbPath);
            return;
        }

        if (!System.IO.File.Exists(sourceSqliteIdentityDbPath))
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

        await IdentityDataImporter.ImportAsync(
            logger,
            identityDomain,
            sourceSystemDb,
            targetSystemDb,
            sourceIdentityDb,
            targetIdentityDb,
            dryRun: false);
    }

}
