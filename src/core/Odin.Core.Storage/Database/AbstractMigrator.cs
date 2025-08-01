using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
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
            return;
        }

        //
        // Collect and group all migrations that need to be run
        //

        var groupedMigrations = GroupMigrationsByVersion(direction, currentVersion, requestedVersion);
        if (groupedMigrations.Count > 0)
        {
            _logger.LogInformation("Migrating database from version {CurrentVersion} to {RequestedVersion}",
                currentVersion, requestedVersion);
        }

        //
        // Run collected migrations by version group
        //

        foreach (var (version, migrations) in groupedMigrations)
        {
            _logger.LogInformation("Running {Count} migration(s) for version {Version}", migrations.Count, version);

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