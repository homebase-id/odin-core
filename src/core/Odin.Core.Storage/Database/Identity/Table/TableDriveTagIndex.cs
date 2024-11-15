using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableDriveTagIndex(
    CacheHelper cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    IdentityKey identityKey)
    : TableDriveTagIndexCRUD(cache, scopedConnectionFactory), ITableMigrator
{
    private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;
    public Guid IdentityId { get; } = identityKey.Id;

    public new async Task<DriveTagIndexRecord> GetAsync(Guid driveId, Guid fileId, Guid tagId)
    {
        return await base.GetAsync(IdentityId, driveId, fileId, tagId);
    }

    public async Task<List<Guid>> GetAsync(Guid driveId, Guid fileId)
    {
        return await base.GetAsync(IdentityId, driveId, fileId);
    }

    public override async Task<int> InsertAsync(DriveTagIndexRecord item)
    {
        item.identityId = IdentityId;
        return await base.InsertAsync(item);
    }

    public async Task<int> DeleteAllRowsAsync(Guid driveId, Guid fileId)
    {
        return await base.DeleteAllRowsAsync(IdentityId, driveId, fileId);
    }

    public async Task InsertRowsAsync(Guid driveId, Guid fileId, List<Guid> tagIdList)
    {
        if (tagIdList == null)
            return;

        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var tx = await cn.BeginStackedTransactionAsync();

        var item = new DriveTagIndexRecord() { identityId = IdentityId, driveId = driveId, fileId = fileId };

        foreach (var tagId in tagIdList)
        {
            item.tagId = tagId;
            await base.InsertAsync(item);
        }

        await tx.CommitAsync();
    }

    public async Task DeleteRowAsync(Guid driveId, Guid fileId, List<Guid> tagIdList)
    {
        if (tagIdList == null)
            return;

        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var tx = await cn.BeginStackedTransactionAsync();

        foreach (var tagId in tagIdList)
        {
            await base.DeleteAsync(IdentityId, driveId, fileId, tagId);
        }

        await tx.CommitAsync();
    }
}