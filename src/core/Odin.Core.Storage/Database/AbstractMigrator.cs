using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Storage.Concurrency;
using Odin.Core.Storage.Factory;

[assembly: InternalsVisibleTo("Odin.Core.Storage.Tests")]

namespace Odin.Core.Storage.Database;

public abstract class AbstractMigrator
{
    internal enum Direction
    {
        Nowhere,
        Up,
        Down
    }

    public abstract List<MigrationBase> SortedMigrations { get; }

    private readonly string _migratorId;
    private readonly ILogger _logger;
    private readonly IScopedConnectionFactory _scopedConnectionFactory;
    private readonly INodeLock _nodeLock;

    //

    protected AbstractMigrator(ILogger logger, IScopedConnectionFactory scopedConnectionFactory, INodeLock nodeLock)
    {
        _logger = logger;
        _scopedConnectionFactory = scopedConnectionFactory;
        _nodeLock = nodeLock;
        _migratorId = GetType().FullName;

        // Sanity
        ArgumentException.ThrowIfNullOrWhiteSpace(_migratorId);
    }

    //

    public async Task MigrateAsync(long requestedVersion = long.MaxValue)
    {
        _logger.LogDebug("Acquiring lock for database migration with ID {MigratorId}", _migratorId);

        await using var _ = await _nodeLock.LockAsync(
            NodeLockKey.Create("database-migrator"),
            timeout: TimeSpan.FromMinutes(5));

        _logger.LogDebug("Starting database migration with ID {MigratorId}", _migratorId);

        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();

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
            _logger.LogDebug("Nothing to migrate for ID {MigratorId}", _migratorId);
            return;
        }

        //
        // Collect migrations by version
        //

        var groupedMigrations = GroupMigrationsByVersion();
        if (groupedMigrations.Count == 0)
        {
            _logger.LogDebug("Nothing to migrate for ID {MigratorId}", _migratorId);
            return;
        }

        // Sanity
        var latestVersion = groupedMigrations.Last().Key;
        if (currentVersion > latestVersion)
        {
            throw new MigrationException(
                $"Current database version {currentVersion} is higher than the latest known migration version " +
                $"{latestVersion}. This likely indicates that the database was migrated with a newer version of " +
                "the software. Please update the software to the latest version.");
        }

        //
        // Adjust requested version
        //

        if (requestedVersion is < 0 or long.MaxValue)
        {
            requestedVersion = direction == Direction.Up ? groupedMigrations.Last().Key : groupedMigrations.First().Key;
        }

        // Sanity
        if (!groupedMigrations.Exists(x => x.Key == requestedVersion))
        {
            throw new MigrationException(
                $"Requested migration version {requestedVersion} does not exist in the migration set.");
        }

        //
        // Filter and order migrations by version according to the direction
        //

        var filteredMigrations = FilterMigrationsByVersion(
            groupedMigrations, direction, currentVersion, requestedVersion);
        if (filteredMigrations.Count == 0)
        {
            _logger.LogDebug("Nothing to migrate for ID {MigratorId}", _migratorId);
            return;
        }

        _logger.LogInformation("Migrating database from version {CurrentVersion} to {RequestedVersion}",
            currentVersion, requestedVersion);

        //
        // Run collected migrations by version group
        //

        foreach (var (version, migrations) in filteredMigrations)
        {
            _logger.LogInformation("Running {Count} migration(s) for version {Version} ({Direction})",
                migrations.Count, version, direction == Direction.Up ? "UP" : "DOWN");

            await using var tx = await cn.BeginStackedTransactionAsync();
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
            tx.Commit();
        }

        // One final update to the current version in case we're migrating down
        if (direction == Direction.Down)
        {
            await SetCurrentVersionAsync(cn, requestedVersion);
        }
    }

    //

    internal List<KeyValuePair<long, List<MigrationBase>>> GroupMigrationsByVersion()
    {
        // Group and order by version
        return SortedMigrations
            .GroupBy(x => x.MigrationVersion)
            .OrderBy(g => g.Key)
            .Select(g => new KeyValuePair<long, List<MigrationBase>>(g.Key, g.ToList()))
            .ToList();
    }

    //

    internal List<KeyValuePair<long, List<MigrationBase>>> FilterMigrationsByVersion(
        List<KeyValuePair<long, List<MigrationBase>>> groupMigrations,
        Direction direction,
        long currentVersion,
        long requestedVersion)
    {
        if (direction == Direction.Up)
        {
            // Filter migrations with version greater than the current version and less than or equal to the requested version
            // Order by ascending version
            return groupMigrations
                .Where(g => g.Key > currentVersion && g.Key <= requestedVersion)
                .OrderBy(g => g.Key)
                .ToList();
        }

        if (direction == Direction.Down)
        {
            // Filter migrations with version greater than the requested version and less than or equal to the current database version
            // Order by descending version
            return groupMigrations
                .Where(g => g.Key > requestedVersion && g.Key <= currentVersion)
                .OrderByDescending(g => g.Key)
                .ToList();
        }

        throw new MigrationException("Cannot filter migrations by version when direction is Nowhere");
    }

    //

    internal async Task<long> GetCurrentVersionAsync()
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        return await GetCurrentVersionAsync(cn);
    }

    //

    private async Task<long> GetCurrentVersionAsync(IConnectionWrapper cn)
    {
        const string sql =
            """
            SELECT COALESCE((
                SELECT version FROM VersionInfo
                WHERE id = @id), -1) AS version;
            """;

        await using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        var idParam = cmd.CreateParameter();
        idParam.DbType = DbType.String;
        idParam.ParameterName = "@id";
        idParam.Value = _migratorId;
        cmd.Parameters.Add(idParam);

        var rs = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(rs);
    }

    //

    internal async Task SetCurrentVersionAsync(long version)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await SetCurrentVersionAsync(cn, version);
    }

    //

    private async Task SetCurrentVersionAsync(IConnectionWrapper cn, long version)
    {
        _logger.LogDebug("Setting current version to {Version} for migrator ID {MigratorId}", version, _migratorId);

        const string sql =
            """
            INSERT INTO VersionInfo (id, version)
            VALUES (@id, @version)
            ON CONFLICT (id) DO UPDATE
            SET version = EXCLUDED.version;
            """;

        await using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;

        var versionParam = cmd.CreateParameter();
        versionParam.DbType = DbType.Int64;
        versionParam.ParameterName = "@version";
        versionParam.Value = version;
        cmd.Parameters.Add(versionParam);

        var idParam = cmd.CreateParameter();
        idParam.DbType = DbType.String;
        idParam.ParameterName = "@id";
        idParam.Value = _migratorId;
        cmd.Parameters.Add(idParam);

        await cmd.ExecuteNonQueryAsync();
    }

    //

    public async Task EnsureVersionInfoTable()
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await EnsureVersionInfoTable(cn);
    }

    //

    private async Task EnsureVersionInfoTable(IConnectionWrapper cn)
    {
        _logger.LogDebug("Ensuring VersionInfo table exists");

        await using var cmd = cn.CreateCommand();
        cmd.CommandText =
            """
            CREATE TABLE IF NOT EXISTS VersionInfo (
                id TEXT PRIMARY KEY,
                version BIGINT NOT NULL
            );
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    //

}

//

