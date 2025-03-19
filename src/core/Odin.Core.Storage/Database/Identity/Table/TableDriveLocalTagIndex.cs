using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Factory;
using Odin.Core.Time;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableDriveLocalTagIndex(
    CacheHelper cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    IdentityKey identityKey)
    : TableDriveLocalTagIndexCRUD(cache, scopedConnectionFactory: scopedConnectionFactory), ITableMigrator
{
    private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;

    /// <summary>
    /// Updates only the local tags table used when Querying the files (querybatch, query-modified, and futures)
    /// </summary>
    public async Task UpdateLocalTagsAsync(Guid driveId, Guid fileId, List<Guid> tags)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var tx = await cn.BeginStackedTransactionAsync();

        await this.DeleteAllRowsAsync(driveId, fileId);
        await this.InsertRowsAsync(driveId, fileId, tags);

        tx.Commit();
    }

    public async Task<int> UpdateLocalAppMetadataAsync(Guid driveId, Guid fileId, Guid newVersionTag, string localMetadataJson)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var updateCommand = cn.CreateCommand();

        string sqlNowStr;
        if (_scopedConnectionFactory.DatabaseType == DatabaseType.Sqlite)
            sqlNowStr = "CAST((julianday('now') - 2440587.5) * 86400000 AS INTEGER)";
        else
            sqlNowStr = "EXTRACT(EPOCH FROM NOW() AT TIME ZONE 'UTC') * 1000";
        
        updateCommand.CommandText = $"UPDATE driveMainIndex " +
                                    $"SET hdrLocalVersionTag=@hdrLocalVersionTag,hdrLocalAppData=@hdrLocalAppData,modified={sqlNowStr} " +
                                    $"WHERE identityId=@identityId AND driveid=@driveId AND fileId=@fileId;";

        var sparam1 = updateCommand.CreateParameter();
        var sparam2 = updateCommand.CreateParameter();
        var sparam3 = updateCommand.CreateParameter();
        var versionTagParam = updateCommand.CreateParameter();
        var contentParam = updateCommand.CreateParameter();

        sparam1.ParameterName = "@identityId";
        sparam2.ParameterName = "@driveId";
        sparam3.ParameterName = "@fileId";
        versionTagParam.ParameterName = "@hdrLocalVersionTag";
        contentParam.ParameterName = "@hdrLocalAppData";

        updateCommand.Parameters.Add(sparam1);
        updateCommand.Parameters.Add(sparam2);
        updateCommand.Parameters.Add(sparam3);
        updateCommand.Parameters.Add(versionTagParam);
        updateCommand.Parameters.Add(contentParam);

        sparam1.Value = identityKey.ToByteArray();
        sparam2.Value = driveId.ToByteArray();
        sparam3.Value = fileId.ToByteArray();
        versionTagParam.Value = newVersionTag.ToByteArray();
        contentParam.Value = localMetadataJson;

        return await updateCommand.ExecuteNonQueryAsync();
    }

    public new async Task<DriveLocalTagIndexRecord> GetAsync(Guid driveId, Guid fileId, Guid tagId)
    {
        return await base.GetAsync(identityKey, driveId, fileId, tagId);
    }

    public async Task<List<Guid>> GetAsync(Guid driveId, Guid fileId)
    {
        return await base.GetAsync(identityKey, driveId, fileId);
    }

    public new async Task<int> InsertAsync(DriveLocalTagIndexRecord item)
    {
        item.identityId = identityKey;
        return await base.InsertAsync(item);
    }

    public async Task<int> DeleteAllRowsAsync(Guid driveId, Guid fileId)
    {
        return await base.DeleteAllRowsAsync(identityKey, driveId, fileId);
    }

    public async Task InsertRowsAsync(Guid driveId, Guid fileId, List<Guid> tagIdList)
    {
        if (tagIdList == null)
            return;

        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var tx = await cn.BeginStackedTransactionAsync();

        var item = new DriveLocalTagIndexRecord() { identityId = identityKey, driveId = driveId, fileId = fileId };

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
            await base.DeleteAsync(identityKey, driveId, fileId, tagId);
        }

        tx.Commit();
    }
}