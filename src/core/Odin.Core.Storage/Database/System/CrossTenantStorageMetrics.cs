using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Odin.Core.Storage.Database.System.Connection;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database.System;

#nullable enable

/// <summary>
/// Storage totals for one identity, as seen from a cross-tenant scan of the identity tables.
/// </summary>
public sealed record CrossTenantIdentityStorage(
    Guid IdentityId,
    long Files,
    long TotalBytes,
    long ActiveBytes,
    int DriveCount);

/// <summary>
/// Aggregates the identity tables across ALL tenants in one query.
///
/// This only works on Postgres, where every tenant shares a single database and rows are
/// discriminated by the identityId column. On SQLite each tenant has its own database file,
/// so there is nothing to group over and <see cref="IsSupported"/> is false; callers must fall
/// back to looping tenant scopes.
///
/// Note this deliberately reads identity tables from a SYSTEM-scoped connection. An identity
/// scope is bound to one tenant's OdinIdentity and, on SQLite, to that tenant's file.
/// </summary>
public class CrossTenantStorageMetrics(ScopedSystemConnectionFactory scopedConnectionFactory)
{
    // FileState.Active. The enum lives in Odin.Services, which this layer cannot reference upward.
    private const int ActiveFileState = 1;

    public bool IsSupported => scopedConnectionFactory.DatabaseType == DatabaseType.Postgres;

    /// <summary>
    /// Every identity that owns at least one drivemainindex row or one drives row, whether or not
    /// it still has a Registrations row. Returns an empty list when <see cref="IsSupported"/> is false.
    /// </summary>
    public async Task<List<CrossTenantIdentityStorage>> GetAllIdentityStorageAsync()
    {
        if (!IsSupported)
        {
            return [];
        }

        await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();

        var files = new Dictionary<Guid, (long Files, long TotalBytes, long ActiveBytes)>();
        var drives = new Dictionary<Guid, int>();

        await using (var cmd = cn.CreateCommand())
        {
            // No index covers byteCount, so this is a full scan. Acceptable: the consumer runs
            // this once a day, and a cached figure refreshed hourly is explicitly fine.
            cmd.CommandText =
                """
                SELECT identityId,
                       COUNT(*),
                       CAST(COALESCE(SUM(byteCount), 0) AS BIGINT),
                       CAST(COALESCE(SUM(CASE WHEN fileState=@fileState THEN byteCount ELSE 0 END), 0) AS BIGINT)
                FROM drivemainindex
                GROUP BY identityId;
                """;
            cmd.AddParameter("@fileState", DbType.Int32, ActiveFileState);

            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                var identityId = new Guid((byte[])rdr[0]);
                files[identityId] = (
                    rdr[1] == DBNull.Value ? 0 : (long)rdr[1],
                    rdr[2] == DBNull.Value ? 0 : (long)rdr[2],
                    rdr[3] == DBNull.Value ? 0 : (long)rdr[3]);
            }
        }

        await using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText =
                """
                SELECT identityId, COUNT(*)
                FROM drives
                GROUP BY identityId;
                """;

            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                var identityId = new Guid((byte[])rdr[0]);
                drives[identityId] = rdr[1] == DBNull.Value ? 0 : Convert.ToInt32(rdr[1]);
            }
        }

        var identityIds = new HashSet<Guid>(files.Keys);
        identityIds.UnionWith(drives.Keys);

        var result = new List<CrossTenantIdentityStorage>(identityIds.Count);
        foreach (var identityId in identityIds)
        {
            var (fileCount, totalBytes, activeBytes) = files.GetValueOrDefault(identityId);
            result.Add(new CrossTenantIdentityStorage(
                identityId,
                fileCount,
                totalBytes,
                activeBytes,
                drives.GetValueOrDefault(identityId)));
        }

        return result;
    }
}
