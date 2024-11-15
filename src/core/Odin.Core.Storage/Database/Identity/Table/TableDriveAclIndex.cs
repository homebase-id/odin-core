using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.SQLite;

[assembly: InternalsVisibleTo("IdentityDatabase")]

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableDriveAclIndex(
    CacheHelper cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    IdentityKey identityKey)
    : TableDriveAclIndexCRUD(cache, scopedConnectionFactory), ITableMigrator
{
    private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;
    public Guid IdentityId { get; } = identityKey.Id;

    public new async Task<DriveAclIndexRecord> GetAsync(Guid driveId, Guid fileId, Guid aclMemberId)
    {
        return await base.GetAsync(IdentityId, driveId, fileId, aclMemberId);
    }

    public async Task<List<Guid>> GetAsync(Guid driveId, Guid fileId)
    {
        return await base.GetAsync(IdentityId, driveId, fileId);
    }

    public async Task<int> DeleteAllRowsAsync(Guid driveId, Guid fileId)
    {
        return await base.DeleteAllRowsAsync(IdentityId, driveId, fileId);
    }

    public override async Task<int> InsertAsync(DriveAclIndexRecord item)
    {
        item.identityId = IdentityId;
        return await base.InsertAsync(item);
    }

    public async Task InsertRowsAsync(Guid driveId, Guid fileId, List<Guid> accessControlList)
    {
        if (accessControlList == null)
            return;

        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var tx = await cn.BeginStackedTransactionAsync();

        var item = new DriveAclIndexRecord { identityId = IdentityId, driveId = driveId, fileId = fileId };

        foreach (var memberId in accessControlList)
        {
            item.aclMemberId = memberId;
            await base.InsertAsync(item);
        }

        await tx.CommitAsync();
    }

    public async Task DeleteRowAsync(Guid driveId, Guid fileId, List<Guid> accessControlList)
    {
        if (accessControlList == null)
            return;

        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var tx = await cn.BeginStackedTransactionAsync();

        foreach (var memberId in accessControlList)
        {
            await base.DeleteAsync(IdentityId, driveId, fileId, memberId);
        }

        await tx.CommitAsync();
    }
}