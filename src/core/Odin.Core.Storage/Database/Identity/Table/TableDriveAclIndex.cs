using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Connection;

[assembly: InternalsVisibleTo("IdentityDatabase")]

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableDriveAclIndex(
    CacheHelper cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    IdentityKey identityKey)
    : TableDriveAclIndexCRUD(cache, scopedConnectionFactory), ITableMigrator
{
    private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;
    
    public new async Task<DriveAclIndexRecord> GetAsync(Guid driveId, Guid fileId, Guid aclMemberId)
    {
        return await base.GetAsync(identityKey, driveId, fileId, aclMemberId);
    }

    public async Task<List<Guid>> GetAsync(Guid driveId, Guid fileId)
    {
        return await base.GetAsync(identityKey, driveId, fileId);
    }

    public async Task<int> DeleteAllRowsAsync(Guid driveId, Guid fileId)
    {
        return await base.DeleteAllRowsAsync(identityKey, driveId, fileId);
    }

    public new async Task<int> InsertAsync(DriveAclIndexRecord item)
    {
        item.identityId = identityKey;
        return await base.InsertAsync(item);
    }

    public async Task InsertRowsAsync(Guid driveId, Guid fileId, List<Guid> accessControlList)
    {
        if (accessControlList == null)
            return;

        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var tx = await cn.BeginStackedTransactionAsync();

        var item = new DriveAclIndexRecord { identityId = identityKey, driveId = driveId, fileId = fileId };

        foreach (var memberId in accessControlList)
        {
            item.aclMemberId = memberId;
            await base.InsertAsync(item);
        }

        tx.Commit();
    }

    public async Task DeleteRowAsync(Guid driveId, Guid fileId, List<Guid> accessControlList)
    {
        if (accessControlList == null)
            return;

        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var tx = await cn.BeginStackedTransactionAsync();

        foreach (var memberId in accessControlList)
        {
            await base.DeleteAsync(identityKey, driveId, fileId, memberId);
        }

        tx.Commit();
    }
}