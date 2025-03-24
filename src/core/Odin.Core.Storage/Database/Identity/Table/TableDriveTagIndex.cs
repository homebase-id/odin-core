using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableDriveTagIndex(
    CacheHelper cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    OdinIdentity odinIdentity)
    : TableDriveTagIndexCRUD(cache, scopedConnectionFactory), ITableMigrator
{
    private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;

    public new async Task<DriveTagIndexRecord> GetAsync(Guid driveId, Guid fileId, Guid tagId)
    {
        return await base.GetAsync(odinIdentity, driveId, fileId, tagId);
    }

    public async Task<List<Guid>> GetAsync(Guid driveId, Guid fileId)
    {
        return await base.GetAsync(odinIdentity, driveId, fileId);
    }

    public new async Task<int> InsertAsync(DriveTagIndexRecord item)
    {
        item.identityId = odinIdentity;
        return await base.InsertAsync(item);
    }

    public async Task<int> DeleteAllRowsAsync(Guid driveId, Guid fileId)
    {
        return await base.DeleteAllRowsAsync(odinIdentity, driveId, fileId);
    }

    public async Task InsertRowsAsync(Guid driveId, Guid fileId, List<Guid> tagIdList)
    {
        if (tagIdList == null)
            return;

        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var tx = await cn.BeginStackedTransactionAsync();

        var item = new DriveTagIndexRecord() { identityId = odinIdentity, driveId = driveId, fileId = fileId };

        foreach (var tagId in tagIdList)
        {
            item.tagId = tagId;
            await base.InsertAsync(item);
        }

        tx.Commit();
    }

    public async Task DeleteRowAsync(Guid driveId, Guid fileId, List<Guid> tagIdList)
    {
        if (tagIdList == null)
            return;

        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var tx = await cn.BeginStackedTransactionAsync();

        foreach (var tagId in tagIdList)
        {
            await base.DeleteAsync(odinIdentity, driveId, fileId, tagId);
        }

        tx.Commit();
    }
}