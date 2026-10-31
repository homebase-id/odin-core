using System;
using System.Collections.Generic;
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
    /// <summary>
    /// DrivePermission.Read, the column's default.  A record starts at 0 in C# whatever the column's DEFAULT
    /// says (the generated insert always sends the value), so every writer here sets it.
    /// </summary>
    public const int ReadPermission = 1;

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
        // A row granting nothing would hide the file from the member it names once queries check the bits
        if (item.permissions == 0)
        {
            throw new ArgumentException("An ACL row must grant at least one permission", nameof(item));
        }

        item.identityId = odinIdentity;
        return await base.InsertAsync(item);
    }

    internal async Task InsertRowsAsync(Guid driveId, Guid fileId, List<Guid> accessControlList)
    {
        if (accessControlList == null)
            return;

        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var tx = await cn.BeginStackedTransactionAsync();

        // Until Selective Share writes its own, every row means "may read"
        var item = new DriveAclIndexRecord { identityId = odinIdentity, driveId = driveId, fileId = fileId, permissions = ReadPermission };

        foreach (var memberId in accessControlList)
        {
            item.aclMemberId = memberId;
            await base.InsertAsync(item);
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