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

    /// <summary>Every row of the file, whole: on a Selective Share drive, its shares, withdrawn ones included.</summary>
    public async Task<List<DriveAclIndexRecord>> GetFileRowsAsync(Guid driveId, Guid fileId)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT rowId,identityId,driveId,fileId,aclMemberId,expiresAt,permissions,lockedFileKey,created,modified " +
                          "FROM DriveAclIndex WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId ORDER BY rowId;";
        cmd.AddParameter("@identityId", DbType.Binary, odinIdentity.IdentityId);
        cmd.AddParameter("@driveId", DbType.Binary, driveId);
        cmd.AddParameter("@fileId", DbType.Binary, fileId);

        return await ReadRowsAsync(cmd);
    }

    private async Task<List<DriveAclIndexRecord>> ReadRowsAsync(ICommandWrapper cmd)
    {
        var rows = new List<DriveAclIndexRecord>();
        await using var rdr = await cmd.ExecuteReaderAsync(CommandBehavior.Default);
        while (await rdr.ReadAsync())
        {
            rows.Add(ReadRecordFromReaderAll(rdr));
        }

        return rows;
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
    /// The live rows of <paramref name="fileIds"/>: the Read bit set (DrivePermission.Read, 1) and not expired at
    /// <paramref name="now"/> (docs/file-access.md, "live row"); only those of <paramref name="memberIds"/> when given.
    /// One read for a page of files, whatever its size.
    /// </summary>
    public async Task<List<DriveAclIndexRecord>> GetLiveRowsAsync(Guid driveId, IReadOnlyCollection<Guid> fileIds,
        IReadOnlyCollection<Guid> memberIds, UnixTimeUtc now)
    {
        if (fileIds.Count == 0 || memberIds?.Count == 0)
        {
            return [];
        }

        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();
        var members = memberIds == null ? "" : $"AND aclMemberId IN ({string.Join(",", memberIds.Select((_, i) => $"@m{i}"))}) ";
        cmd.CommandText = "SELECT rowId,identityId,driveId,fileId,aclMemberId,expiresAt,permissions,lockedFileKey,created,modified " +
                          "FROM DriveAclIndex WHERE identityId = @identityId AND driveId = @driveId " +
                          $"AND fileId IN ({string.Join(",", fileIds.Select((_, i) => $"@f{i}"))}) {members}" +
                          "AND (permissions & 1) = 1 AND (expiresAt IS NULL OR expiresAt > @now) ORDER BY rowId;";
        cmd.AddParameter("@identityId", DbType.Binary, odinIdentity.IdentityId);
        cmd.AddParameter("@driveId", DbType.Binary, driveId);
        cmd.AddParameter("@now", DbType.Int64, now.milliseconds);
        foreach (var (fileId, i) in fileIds.Select((f, i) => (f, i)))
        {
            cmd.AddParameter($"@f{i}", DbType.Binary, fileId);
        }

        foreach (var (memberId, i) in (memberIds ?? []).Select((m, i) => (m, i)))
        {
            cmd.AddParameter($"@m{i}", DbType.Binary, memberId);
        }

        return await ReadRowsAsync(cmd);
    }

    public async Task<(List<DriveAclIndexRecord>, Int64? nextCursor)> PagingByRowIdAsync(int count, Int64? inCursor)
    {
        return await base.PagingByRowIdAsync(count, odinIdentity.IdentityId, inCursor);
    }
}