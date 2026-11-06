using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Factory;
using Odin.Core.Time;

[assembly: InternalsVisibleTo("IdentityDatabase")]

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableDriveAclIndex(
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    OdinIdentity odinIdentity)
    : TableDriveAclIndexCRUD(scopedConnectionFactory)
{
    private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;
    
    internal new async Task<DriveAclIndexRecord> GetAsync(Guid driveId, Guid fileId, Guid aclMemberId)
    {
        return await base.GetAsync(odinIdentity, driveId, fileId, aclMemberId);
    }

    internal async Task<List<Guid>> GetAsync(Guid driveId, Guid fileId)
    {
        return await base.GetAsync(odinIdentity, driveId, fileId);
    }

    internal async Task<int> DeleteAllRowsAsync(Guid driveId, Guid fileId)
    {
        return await base.DeleteAllRowsAsync(odinIdentity, driveId, fileId);
    }

    internal new async Task<int> InsertAsync(DriveAclIndexRecord item)
    {
        item.identityId = odinIdentity;
        return await base.InsertAsync(item);
    }

    /// <summary>Inserts the row, or updates its permissions, expiry and locked file key; created stays, modified moves.</summary>
    internal new async Task<int> UpsertAsync(DriveAclIndexRecord item)
    {
        item.identityId = odinIdentity;
        return await base.UpsertAsync(item);
    }

    /// <summary>
    /// Makes the file's rows match the list, in one transaction: deletes the rows of members no longer in it and
    /// inserts the new ones. A row whose member stays is not touched, so its created and modified stay put, and so
    /// would its key header, permissions and expiry (docs/selective-share-plan.md). A member listed twice gets
    /// one row. An unchanged list, the usual header write, costs one read.
    /// </summary>
    internal async Task ReconcileRowsAsync(Guid driveId, Guid fileId, List<Guid> accessControlList)
    {
        var wanted = accessControlList?.ToHashSet() ?? [];

        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var tx = await cn.BeginStackedTransactionAsync();

        var existing = await GetAsync(driveId, fileId);
        foreach (var memberId in existing.Where(m => !wanted.Contains(m)))
        {
            await base.DeleteAsync(odinIdentity, driveId, fileId, memberId);
        }

        // permissions keeps the record's default, 1 (DrivePermission.Read): until Selective Share writes its own,
        // every row means "may read". TryInsert, as a row added meanwhile by another writer is no conflict.
        var item = new DriveAclIndexRecord { identityId = odinIdentity, driveId = driveId, fileId = fileId };
        foreach (var memberId in wanted.Except(existing))
        {
            item.aclMemberId = memberId;
            await base.TryInsertAsync(item);
        }

        tx.Commit();
    }

    /// <summary>
    /// A live row: the Read bit set (DrivePermission.Read, 1) and not expired at <paramref name="now"/>
    /// (docs/file-access.md, "live row").
    /// </summary>
    public static bool IsLive(DriveAclIndexRecord row, UnixTimeUtc now) =>
        (row.permissions & 1) == 1 && (row.expiresAt == null || row.expiresAt.Value.milliseconds > now.milliseconds);

    /// <summary><see cref="IsLive"/> as SQL, for the rows aliased <paramref name="alias"/> in a query.</summary>
    public static string IsLiveSql(string alias, UnixTimeUtc now) =>
        $"({alias}.permissions & 1) = 1 AND ({alias}.expiresAt IS NULL OR {alias}.expiresAt > {now.milliseconds})";

    /// <summary>Whether the file has any row, live or not: one read of its member ids, by the table's own key.</summary>
    public async Task<bool> HasRowsAsync(Guid driveId, Guid fileId) => (await GetAsync(driveId, fileId)).Count > 0;

    /// <summary>Every row of <paramref name="fileIds"/>, live or not (<see cref="GetRowsAsync"/>).</summary>
    public async Task<List<DriveAclIndexRecord>> GetFileRowsAsync(Guid driveId, IReadOnlyCollection<Guid> fileIds) =>
        (await GetRowsAsync(driveId, fileIds, [], null)).Select(r => r.Row).ToList();

    /// <summary>
    /// The rows of <paramref name="fileIds"/>, and of the standard files whose global transit ids are
    /// <paramref name="globalTransitIds"/> (the file a comment's group names, docs/selective-share-pr3-plan.md, section
    /// 10), each with its file's global transit id and required security group; only those of
    /// <paramref name="memberIds"/> when given.  Live or not (<see cref="IsLive"/>).  One read for a page of files,
    /// whatever its size.
    /// </summary>
    public async Task<List<(DriveAclIndexRecord Row, Guid? GlobalTransitId, int RequiredSecurityGroup)>> GetRowsAsync(Guid driveId,
        IReadOnlyCollection<Guid> fileIds, IReadOnlyCollection<Guid> globalTransitIds, IReadOnlyCollection<Guid> memberIds)
    {
        if ((fileIds.Count == 0 && globalTransitIds.Count == 0) || memberIds?.Count == 0)
        {
            return [];
        }

        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();
        var files = new List<string>();
        if (fileIds.Count > 0)
        {
            files.Add($"r.fileId IN ({string.Join(",", fileIds.Select((_, i) => $"@f{i}"))})");
        }

        if (globalTransitIds.Count > 0)
        {
            files.Add($"(m.globalTransitId IN ({string.Join(",", globalTransitIds.Select((_, i) => $"@g{i}"))}) " +
                      "AND m.fileSystemType = @standard)");
        }

        var members = memberIds == null ? "" : $"AND r.aclMemberId IN ({string.Join(",", memberIds.Select((_, i) => $"@m{i}"))}) ";
        cmd.CommandText = "SELECT r.rowId,r.identityId,r.driveId,r.fileId,r.aclMemberId,r.expiresAt,r.permissions,r.lockedFileKey," +
                          "r.created,r.modified,m.globalTransitId,m.requiredSecurityGroup FROM DriveAclIndex r JOIN DriveMainIndex m " +
                          "ON m.identityId = r.identityId AND m.driveId = r.driveId AND m.fileId = r.fileId " +
                          $"WHERE r.identityId = @identityId AND r.driveId = @driveId {members}" +
                          $"AND ({string.Join(" OR ", files)}) ORDER BY r.rowId;";
        cmd.AddParameter("@identityId", DbType.Binary, odinIdentity.IdentityId);
        cmd.AddParameter("@driveId", DbType.Binary, driveId);
        cmd.AddParameter("@standard", DbType.Int32, (int)FileSystemType.Standard);
        foreach (var (fileId, i) in fileIds.Select((f, i) => (f, i)))
        {
            cmd.AddParameter($"@f{i}", DbType.Binary, fileId);
        }

        foreach (var (globalTransitId, i) in globalTransitIds.Select((g, i) => (g, i)))
        {
            cmd.AddParameter($"@g{i}", DbType.Binary, globalTransitId);
        }

        foreach (var (memberId, i) in (memberIds ?? []).Select((m, i) => (m, i)))
        {
            cmd.AddParameter($"@m{i}", DbType.Binary, memberId);
        }

        var rows = new List<(DriveAclIndexRecord, Guid?, int)>();
        await using var rdr = await cmd.ExecuteReaderAsync(CommandBehavior.Default);
        while (await rdr.ReadAsync())
        {
            rows.Add((ReadRecordFromReaderAll(rdr), rdr.IsDBNull(10) ? null : new Guid((byte[])rdr[10]), Convert.ToInt32(rdr[11])));
        }

        return rows;
    }

    public async Task<(List<DriveAclIndexRecord>, Int64? nextCursor)> PagingByRowIdAsync(int count, Int64? inCursor)
    {
        return await base.PagingByRowIdAsync(count, odinIdentity.IdentityId, inCursor);
    }
}