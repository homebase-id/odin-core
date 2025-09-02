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
    public async Task UpdateLocalTagsAsync(Guid driveId, Guid fileId, List<Guid> tags)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var tx = await cn.BeginStackedTransactionAsync();

        await this.DeleteAllRowsAsync(driveId, fileId);
        await this.InsertRowsAsync(driveId, fileId, tags);

        tx.Commit();
    }

    /// <summary>
    /// Updates local app metadata in the driveMainIndex table.
    /// </summary>
    /// <param name="driveId">The drive ID.</param>
    /// <param name="fileId">The file ID.</param>
    /// <param name="oldVersionTag">The expected current version tag.</param>
    /// <param name="newVersionTag">The new version tag to set.</param>
    /// <param name="localMetadataJson">The new metadata JSON.</param>
    /// <returns>Returns false if the row doesn't exist, throws an exception on version tag mismatch, and returns true if updated successfully.</returns>
    /// <exception cref="OdinClientException">Thrown if the version tag mismatches.</exception>
    /// <exception cref="ArgumentException">Thrown if newVersionTag equals oldVersionTag or is empty.</exception>
    public async Task<bool> UpdateLocalAppMetadataAsync(Guid driveId, Guid fileId, Guid oldVersionTag, Guid newVersionTag, string localMetadataJson)
    {
        newVersionTag.AssertGuidNotEmpty();

        if (oldVersionTag == newVersionTag)
            throw new ArgumentException("newVersionTag==oldVersionTag : Fy fy, skamme skamme, man må ikke snyde");


        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var tx = await cn.BeginStackedTransactionAsync(); // The SQL below requires a transaction

        string sqlNowStr;

        using (var selectCommand = cn.CreateCommand())
        {
            sqlNowStr = selectCommand.SqlNow();
            string forUpdate;
            if (_scopedConnectionFactory.DatabaseType == DatabaseType.Sqlite)
                forUpdate = "";
            else
                forUpdate = "FOR UPDATE";

            selectCommand.CommandText = $"SELECT 1 FROM driveMainIndex WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId {forUpdate};";

            var param1 = selectCommand.CreateParameter();
            var param2 = selectCommand.CreateParameter();
            var param3 = selectCommand.CreateParameter();
            param1.ParameterName = "@identityId";
            param2.ParameterName = "@driveId";
            param3.ParameterName = "@fileId";
            selectCommand.Parameters.Add(param1);
            selectCommand.Parameters.Add(param2);
            selectCommand.Parameters.Add(param3);
            param1.Value = odinIdentity.IdentityIdAsByteArray();
            param2.Value = driveId.ToByteArray();
            param3.Value = fileId.ToByteArray();

            var result = await selectCommand.ExecuteScalarAsync();
            bool rowExists = result != null;

            if (rowExists == false)
                return false; // The item doesn't exist
        }


        await using var updateCommand = cn.CreateCommand();
        // We unfortunately need the row_check to differentiate between not-found and version tag mismatch
        updateCommand.CommandText =
            $"""
            UPDATE driveMainIndex
            SET hdrLocalVersionTag = @newVersionTag, hdrLocalAppData = @hdrLocalAppData, modified = {updateCommand.SqlMax()}(driveMainIndex.modified+1,{sqlNowStr})
            WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId
                  AND COALESCE(hdrLocalVersionTag, @emptyGuid) = @hdrLocalVersionTag
            """;

        var sparam1 = updateCommand.CreateParameter();
        var sparam2 = updateCommand.CreateParameter();
        var sparam3 = updateCommand.CreateParameter();
        var sparam4 = updateCommand.CreateParameter();
        var sparam5 = updateCommand.CreateParameter();
        var newVersionTagParam = updateCommand.CreateParameter();
        var contentParam = updateCommand.CreateParameter();

        sparam1.ParameterName = "@identityId";
        sparam2.ParameterName = "@driveId";
        sparam3.ParameterName = "@fileId";
        sparam4.ParameterName = "@hdrLocalVersionTag";
        sparam5.ParameterName = "@emptyGuid";
        newVersionTagParam.ParameterName = "@newVersionTag";
        contentParam.ParameterName = "@hdrLocalAppData";


        updateCommand.Parameters.Add(sparam1);
        updateCommand.Parameters.Add(sparam2);
        updateCommand.Parameters.Add(sparam3);
        updateCommand.Parameters.Add(sparam4);
        updateCommand.Parameters.Add(sparam5);
        updateCommand.Parameters.Add(newVersionTagParam);
        updateCommand.Parameters.Add(contentParam);

        sparam1.Value = odinIdentity.IdentityIdAsByteArray();
        sparam2.Value = driveId.ToByteArray();
        sparam3.Value = fileId.ToByteArray();
        sparam4.Value = oldVersionTag.ToByteArray();
        sparam5.Value = Guid.Empty.ToByteArray();
        newVersionTagParam.Value = newVersionTag.ToByteArray();
        contentParam.Value = localMetadataJson;

        int rows = await updateCommand.ExecuteNonQueryAsync();

        if (rows < 1)
            throw new OdinClientException($"Mismatching version tag {oldVersionTag}", OdinClientErrorCode.VersionTagMismatch);

        tx.Commit();

        return true;
    }

    public new async Task<DriveLocalTagIndexRecord> GetAsync(Guid driveId, Guid fileId, Guid tagId)
    {
        return await base.GetAsync(odinIdentity, driveId, fileId, tagId);
    }

    public async Task<List<Guid>> GetAsync(Guid driveId, Guid fileId)
    {
        return await base.GetAsync(odinIdentity, driveId, fileId);
    }

    public new async Task<int> InsertAsync(DriveLocalTagIndexRecord item)
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

        var item = new DriveLocalTagIndexRecord() { identityId = odinIdentity, driveId = driveId, fileId = fileId };

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