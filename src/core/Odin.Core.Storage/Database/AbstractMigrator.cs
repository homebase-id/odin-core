using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Factory;

[assembly: InternalsVisibleTo("Odin.Core.Storage.Tests")]

namespace Odin.Core.Storage.Database;

public abstract class AbstractMigrator
{
    protected abstract List<MigrationBase> SortedMigrations { get; }

    private enum Direction
    {
        Nowhere,
        Up,
        Down
    }

    public async Task MigrateAsync(IScopedConnectionFactory scopedConnectionFactory, long requestedVersion = long.MaxValue)
    {
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

        //
        // Collect all migrations that need to be run
        //

        List<MigrationBase> migrationsToRun;
        if (direction == Direction.Up)
        {
            // Collect all migrations with
            //  version greater than the current database version and less than or equal to the requested version
            //  sort OLDEST to NEWEST
            migrationsToRun = SortedMigrations
                .Where(m => m.MigrationVersion > currentVersion && m.MigrationVersion <= requestedVersion)
                .OrderBy(m => m.MigrationVersion)
                .ToList();
        }
        else
        {
            // Collect all migrations with
            //   version greater than the requested version and less than or equal to the current database version
            //   sort NEWEST to OLDEST
            migrationsToRun = SortedMigrations
                .Where(m => m.MigrationVersion > requestedVersion && m.MigrationVersion <= currentVersion)
                .OrderBy(m => m.MigrationVersion)
                .Reverse()
                .ToList();
        }

        //
        // Run collected migrations
        //

        foreach (var migration in migrationsToRun)
        {
            if (direction == Direction.Up)
            {
                await migration.UpAsync(cn);
            }
            else
            {
                await migration.DownAsync(cn);
            }

            await SetCurrentVersionAsync(cn, migration.MigrationVersion);
        }
    }

    //

    internal async Task<long> GetCurrentVersionAsync(IScopedConnectionFactory scopedConnectionFactory)
    {
        await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
        return await GetCurrentVersionAsync(cn);
    }

    //

    internal async Task<long> GetCurrentVersionAsync(IConnectionWrapper cn)
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

    internal async Task SetCurrentVersionAsync(IScopedConnectionFactory scopedConnectionFactory, long version)
    {
        await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
        await SetCurrentVersionAsync(cn, version);
    }

    //

    internal async Task SetCurrentVersionAsync(IConnectionWrapper cn, long version)
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

    public const string SqlGetCurrentVersion =
        """
        SELECT COALESCE((SELECT version FROM VersionInfo), -1) AS version;
        """;

    public const string SqlUpsertVersion =
        """
        INSERT INTO VersionInfo (singleton, version)
        VALUES (1, @version)
        ON CONFLICT (singleton) DO UPDATE
        SET version = EXCLUDED.version;
        """;

    //

    private static async Task EnsureVersionInfoTable(IScopedConnectionFactory scopedConnectionFactory)
    {
        await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
        await EnsureVersionInfoTable(cn);
    }

    //

    private static async Task EnsureVersionInfoTable(IConnectionWrapper cn)
    {
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