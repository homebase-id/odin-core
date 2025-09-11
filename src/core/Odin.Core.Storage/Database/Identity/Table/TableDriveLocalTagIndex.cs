using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Factory;
using Odin.Core.Util;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableDriveLocalTagIndex(
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    OdinIdentity odinIdentity)
    : TableDriveLocalTagIndexCRUD(scopedConnectionFactory: scopedConnectionFactory)
{
    private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;

    /// <summary>
    /// Updates only the local tags table used when Querying the files (querybatch, query-modified, and futures)
    /// </summary>
    internal async Task UpdateLocalTagsAsync(Guid driveId, Guid fileId, List<Guid> tags)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var tx = await cn.BeginStackedTransactionAsync();

        await this.DeleteAllRowsAsync(driveId, fileId);
        await this.InsertRowsAsync(driveId, fileId, tags);

        tx.Commit();
    }

    internal new async Task<DriveLocalTagIndexRecord> GetAsync(Guid driveId, Guid fileId, Guid tagId)
    {
        return await base.GetAsync(odinIdentity, driveId, fileId, tagId);
    }

    internal async Task<List<Guid>> GetAsync(Guid driveId, Guid fileId)
    {
        return await base.GetAsync(odinIdentity, driveId, fileId);
    }

    internal new async Task<int> InsertAsync(DriveLocalTagIndexRecord item)
    {
        item.identityId = odinIdentity;
        return await base.InsertAsync(item);
    }

    internal async Task<int> DeleteAllRowsAsync(Guid driveId, Guid fileId)
    {
        return await base.DeleteAllRowsAsync(odinIdentity, driveId, fileId);
    }

    internal async Task InsertRowsAsync(Guid driveId, Guid fileId, List<Guid> tagIdList)
    {
        if (tagIdList == null)
            return;

        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var tx = await cn.BeginStackedTransactionAsync();

        var item = new DriveLocalTagIndexRecord() { identityId = odinIdentity, driveId = driveId, fileId = fileId };

        foreach (var tagId in tagIdList)
        {
            item.tagId = tagId;
            await base.InsertAsync(item);
        }

        tx.Commit();
    }

    internal async Task DeleteRowAsync(Guid driveId, Guid fileId, List<Guid> tagIdList)
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