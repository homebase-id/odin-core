using System;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Tasks;
using Odin.Services.Configuration;
using Odin.Services.Registry;
using Odin.Services.Tenant.Container;

namespace Odin.Hosting.Cli.Commands;

#nullable enable

public static class Sqlite2Pg
{
    internal static async Task ExecuteAsync(IServiceProvider services, string sqliteDbPath, string identityDomain)
    {
        var logger = services.GetRequiredService<ILogger<CommandLine>>();
        var config = services.GetRequiredService<OdinConfiguration>();
        var registry = services.GetRequiredService<IIdentityRegistry>();
        var tenantContainer = services.GetRequiredService<IMultiTenantContainer>();

        logger.LogInformation("Starting SQLite to PostgreSQL migration for {identity} from {path}",
            identityDomain, sqliteDbPath);

        if (!System.IO.File.Exists(sqliteDbPath))
        {
            logger.LogError("SQLite database file not found: {path}", sqliteDbPath);
            return;
        }

        // The identity must NOT already exist in the registry
        registry.LoadRegistrations().BlockingWait();
        var allTenants = await registry.GetTenants();
        var existing = allTenants.FirstOrDefault(t =>
            t.PrimaryDomainName.Equals(identityDomain, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            logger.LogError("Identity already exists in registry: {identity}", identityDomain);
            return;
        }

        // Read the identityId (Guid) from the SQLite file
        var identityId = ReadIdentityIdFromSqlite(sqliteDbPath);
        if (identityId == Guid.Empty)
        {
            logger.LogError("Could not determine identityId from SQLite database: {path}", sqliteDbPath);
            return;
        }

        logger.LogInformation("Found identityId {id} in SQLite database", identityId);

        // Source: SQLite
        await using var sqliteScope = tenantContainer.BeginLifetimeScope(cb =>
        {
            cb.RegisterInstance(new OdinIdentity(identityId, identityDomain)).SingleInstance();
            cb.AddSqliteIdentityDatabaseServices(identityId, sqliteDbPath);
        });

        // Target: PostgreSQL
        await using var pgScope = tenantContainer.BeginLifetimeScope(cb =>
        {
            cb.RegisterInstance(new OdinIdentity(identityId, identityDomain)).SingleInstance();
            cb.AddPgsqlIdentityDatabaseServices(identityId, config.Database.ConnectionString);
        });

        var sourceDb = sqliteScope.Resolve<IdentityDatabase>();
        var targetDb = pgScope.Resolve<IdentityDatabase>();
        var sourceMigrator = sqliteScope.Resolve<IdentityMigrator>();
        var targetMigrator = pgScope.Resolve<IdentityMigrator>();

        // Ensure target schema is up to date
        await targetDb.MigrateDatabaseAsync();

        await IdentityDataImporter.ImportAsync(sourceDb, targetDb, sourceMigrator, targetMigrator, logger);

        logger.LogInformation("Migration complete for {identity}", identityDomain);
    }

    //

    private static Guid ReadIdentityIdFromSqlite(string sqliteDbPath)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = sqliteDbPath,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT identityId FROM Drives LIMIT 1";

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            var bytes = (byte[])reader["identityId"];
            return new Guid(bytes);
        }

        return Guid.Empty;
    }
}
