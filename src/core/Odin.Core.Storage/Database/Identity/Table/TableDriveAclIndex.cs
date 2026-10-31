using System;
using System.Collections.Generic;
using System.Data;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Connection;

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

    internal async Task InsertRowsAsync(Guid driveId, Guid fileId, List<Guid> accessControlList)
    {
        if (accessControlList == null)
            return;

        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var tx = await cn.BeginStackedTransactionAsync();

        // permissions keeps the record's default, 1 (DrivePermission.Read): until Selective Share writes its own,
        // every row means "may read"
        var item = new DriveAclIndexRecord { identityId = odinIdentity, driveId = driveId, fileId = fileId };

        foreach (var memberId in accessControlList)
        {
            item.aclMemberId = memberId;
            await base.InsertAsync(item);
        }

        tx.Commit();
    }

    /// <summary>
    /// Makes the file's rows match the list: deletes the rows of members no longer in it and inserts the new
    /// ones, in one transaction. A row whose member stays is not touched, so it keeps its key header, its
    /// permissions, its expiry and its created and modified (docs/selective-share-plan.md). A member listed
    /// twice gets one row.
    /// </summary>
    internal async Task ReconcileRowsAsync(Guid driveId, Guid fileId, List<Guid> accessControlList)
    {
        if (accessControlList == null || accessControlList.Count == 0)
        {
            await DeleteAllRowsAsync(driveId, fileId);
            return;
        }

        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var tx = await cn.BeginStackedTransactionAsync();

        await using (var deleteCommand = cn.CreateCommand())
        {
            var members = new List<string>();
            for (var i = 0; i < accessControlList.Count; i++)
            {
                members.Add($"@member{i}");
                deleteCommand.AddParameter($"@member{i}", DbType.Binary, accessControlList[i]);
            }

            deleteCommand.CommandText = "DELETE FROM DriveAclIndex " +
                                        "WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId " +
                                        $"AND aclMemberId NOT IN ({string.Join(",", members)});";
            deleteCommand.AddParameter("@identityId", DbType.Binary, odinIdentity.IdentityId);
            deleteCommand.AddParameter("@driveId", DbType.Binary, driveId);
            deleteCommand.AddParameter("@fileId", DbType.Binary, fileId);
            await deleteCommand.ExecuteNonQueryAsync();
        }

        // permissions keeps the record's default, 1 (DrivePermission.Read), as in InsertRowsAsync
        var item = new DriveAclIndexRecord { identityId = odinIdentity, driveId = driveId, fileId = fileId };
        foreach (var memberId in accessControlList)
        {
            item.aclMemberId = memberId;
            await base.TryInsertAsync(item);
        }

        tx.Commit();
    }

    internal async Task DeleteRowAsync(Guid driveId, Guid fileId, List<Guid> accessControlList)
    {
        if (accessControlList == null)
            return;

        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var tx = await cn.BeginStackedTransactionAsync();

        foreach (var memberId in accessControlList)
        {
            await base.DeleteAsync(odinIdentity, driveId, fileId, memberId);
        }

        tx.Commit();
    }

    public async Task<(List<DriveAclIndexRecord>, Int64? nextCursor)> PagingByRowIdAsync(int count, Int64? inCursor)
    {
        return await base.PagingByRowIdAsync(count, odinIdentity.IdentityId, inCursor);
    }
}