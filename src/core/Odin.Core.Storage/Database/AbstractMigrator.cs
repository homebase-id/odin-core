using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Factory;

[assembly: InternalsVisibleTo("Odin.Core.Storage.Tests")]

namespace Odin.Core.Storage.Database;

public abstract class AbstractMigrator(ILogger logger, IScopedConnectionFactory scopedConnectionFactory)
{
    public abstract List<MigrationBase> SortedMigrations { get; }

    internal enum Direction
    {
        Nowhere,
        Up,
        Down
    }

    public async Task MigrateAsync(long requestedVersion = long.MaxValue)
    {
        // SEB:TODO distributed lock (ach... we need to move INodeLock to Odin.Core.Storage first)

        await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();

        await EnsureVersionInfoTable(cn);

        var currentVersion = await GetCurrentVersionAsync(cn);

        var direction = requestedVersion.CompareTo(currentVersion) switch
        {
            > 0 => Direction.Up,
            < 0 => Direction.Down,
            0 => Direction.Nowhere
        };

        if (direction == Direction.Nowhere)
        {
            return;
        }

        logger.LogInformation("Migrating database from version {CurrentVersion} to {RequestedVersion} ({Direction})",
            currentVersion, requestedVersion, direction);

        //
        // Collect and group all migrations that need to be run
        //

        var groupedMigrations = GroupMigrationsByVersion(direction, currentVersion, requestedVersion);

        //
        // Run collected migrations by version group
        //

        foreach (var (version, migrations) in groupedMigrations)
        {
            logger.LogInformation("Running migrations for version {Version} ({Count})", version, migrations.Count);

            await using var trx = await cn.BeginStackedTransactionAsync();
            foreach (var migration in migrations)
            {
                if (direction == Direction.Up)
                {
                    await migration.UpAsync(cn);
                }
                else
                {
                    await migration.DownAsync(cn);
                }
            }

            await SetCurrentVersionAsync(cn, version);
            trx.Commit();
        }
    }

    //

    internal List<KeyValuePair<long, List<MigrationBase>>> GroupMigrationsByVersion(
        Direction direction,
        long currentVersion,
        long requestedVersion)
    {
        if (direction == Direction.Up)
        {
            // Collect all migrations with
            //  version greater than the current database version and less than or equal to the requested version
            var filteredMigrations = SortedMigrations
                .Where(m => m.MigrationVersion > currentVersion && m.MigrationVersion <= requestedVersion)
                .ToList();

            // Group by version and order by version ascending
            var groupedMigrations = filteredMigrations
                .GroupBy(x => x.MigrationVersion)
                .OrderBy(g => g.Key)  // or OrderByDescending for reverse
                .Select(g => new KeyValuePair<long, List<MigrationBase>>(g.Key, g.ToList()))
                .ToList();

            return groupedMigrations;
        }

        if (direction == Direction.Down)
        {
            // Collect all migrations with
            //   version greater than the requested version and less than or equal to the current database version
            var filteredMigrations = SortedMigrations
                .Where(m => m.MigrationVersion > requestedVersion && m.MigrationVersion <= currentVersion)
                .ToList();

            // Group by version and order by version descending
            var groupedMigrations = filteredMigrations
                .GroupBy(x => x.MigrationVersion)
                .OrderByDescending(g => g.Key)
                .Select(g => new KeyValuePair<long, List<MigrationBase>>(g.Key, g.ToList()))
                .ToList();

            return groupedMigrations;
        }

        throw new OdinSystemException("Cannot group migrations by version when direction is Nowhere");
    }

    //

    internal async Task<long> GetCurrentVersionAsync()
    {
        await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
        return await GetCurrentVersionAsync(cn);
    }

    //

    private async Task<long> GetCurrentVersionAsync(IConnectionWrapper cn)
    {
        await using var cmd = cn.CreateCommand();

        cmd.CommandText = SqlGetCurrentVersion;
        await using var rdr = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow);

        // Sanity
        if (await rdr.ReadAsync() == false)
        {
            throw new OdinSystemException("Failed to read current version from VersionInfo table.");
        }

        return (long)rdr[0];
    }

    //

    internal async Task SetCurrentVersionAsync(long version)
    {
        await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
        await SetCurrentVersionAsync(cn, version);
    }

    //

    private async Task SetCurrentVersionAsync(IConnectionWrapper cn, long version)
    {
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = SqlUpsertVersion;

        var versionParam = cmd.CreateParameter();
        versionParam.DbType = DbType.Int64;
        versionParam.ParameterName = "@version";
        versionParam.Value = version;
        cmd.Parameters.Add(versionParam);

        await cmd.ExecuteNonQueryAsync();
    }

    //

    private const string SqlGetCurrentVersion =
        """
        SELECT COALESCE((SELECT version FROM VersionInfo), -1) AS version;
        """;

    private const string SqlUpsertVersion =
        """
        INSERT INTO VersionInfo (singleton, version)
        VALUES (1, @version)
        ON CONFLICT (singleton) DO UPDATE
        SET version = EXCLUDED.version;
        """;

    //

    private async Task EnsureVersionInfoTable(IConnectionWrapper cn)
    {
        logger.LogDebug("Ensuring VersionInfo table exists");

        await using var cmd = cn.CreateCommand();
        cmd.CommandText =
            """
            CREATE TABLE IF NOT EXISTS VersionInfo (
                singleton INTEGER PRIMARY KEY CHECK (singleton = 1),
                version BIGINT NOT NULL
            );
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    //

}