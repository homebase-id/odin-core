using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Odin.Core.Storage.Database.System.Connection;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database.System;

#nullable enable

/// <summary>
/// What one identity holds, as counted by a single pass over the whole database.
/// </summary>
public sealed record IdentityStorageRow(
    Guid IdentityId,
    long Files,
    long TotalBytes,
    long ActiveBytes,
    int DriveCount);

/// <summary>
/// Counts what every identity in the database holds, in one pass.
///
/// WHY THIS EXISTS - two reasons, and the second is the one that is easy to miss:
///
/// 1. IT SEES IDENTITIES THE REGISTRY CANNOT NAME. Totalling one identity's storage is just
///    <c>SUM(byteCount) WHERE identityId = @id</c>, and that is all a registered tenant needs. But
///    you can only do that for identities you can *enumerate*, and the enumeration comes from the
///    registry - so an identity whose Registrations row is gone can never be asked about. That is
///    not hypothetical: measured on na-metal on 2026-09-12, Registrations held 3 tenants while
///    drivemainindex held 4 distinct identityIds, two of them carrying ~166 KB with no
///    registration at all, residue from tenants deleted two days earlier. A report built only from
///    registrations would not merely miss that - it would hide it.
///
/// 2. IT IS ONE QUERY INSTEAD OF N. The per-identity sum has to be issued once per tenant, each
///    needing its own lifetime scope and round trip. Grouping does the whole node in a single
///    pass, which is the difference between two queries and several thousand sequential ones on a
///    busy node.
///
/// LIMITS: Postgres only. There, every tenant shares one database and rows are told apart by the
/// identityId column, so the whole population can be grouped at once. On SQLite each tenant has
/// its own database file and an orphaned file is not reachable from any other, so there is nothing
/// to group over: <see cref="IsSupported"/> is false, the census is empty, and the caller falls
/// back to per-tenant sums plus a scan of the registration directories.
///
/// It reads identity tables from a SYSTEM-scoped connection on purpose: an identity scope is bound
/// to a single tenant's OdinIdentity, which is precisely the assumption being checked here.
/// </summary>
public class IdentityStorageCensus(ScopedSystemConnectionFactory scopedConnectionFactory)
{
    // FileState.Active. The enum lives in Odin.Services, which this layer cannot reference upward.
    private const int ActiveFileState = 1;

    public bool IsSupported => scopedConnectionFactory.DatabaseType == DatabaseType.Postgres;

    /// <summary>
    /// Every identity that owns at least one file row or one drive row, registered or not.
    /// Empty when <see cref="IsSupported"/> is false -- the caller must then fall back to
    /// per-tenant sums, and will not see unregistered identities.
    /// </summary>
    public async Task<List<IdentityStorageRow>> GetAllAsync()
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
            // No index covers byteCount, so this is a full scan. Acceptable: the consumer reads
            // this once a day, and the spec is explicit that an hourly figure is fine.
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

        // Drives are counted separately: an identity can hold drive rows and no files.
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

        var result = new List<IdentityStorageRow>(identityIds.Count);
        foreach (var identityId in identityIds)
        {
            var (fileCount, totalBytes, activeBytes) = files.GetValueOrDefault(identityId);
            result.Add(new IdentityStorageRow(
                identityId,
                fileCount,
                totalBytes,
                activeBytes,
                drives.GetValueOrDefault(identityId)));
        }

        return result;
    }
}
