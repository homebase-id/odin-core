using System;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Time;
using Odin.Core.Util;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableDriveMainIndex(
    CacheHelper cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    IdentityKey identityKey)
    : TableDriveMainIndexCRUD(cache, scopedConnectionFactory), ITableMigrator
{
    private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;

    public async Task<DriveMainIndexRecord> GetByUniqueIdAsync(Guid driveId, Guid? uniqueId)
    {
        return await base.GetByUniqueIdAsync(identityKey, driveId, uniqueId);
    }

    public async Task<DriveMainIndexRecord> GetByGlobalTransitIdAsync(Guid driveId, Guid? globalTransitId)
    {
        return await base.GetByGlobalTransitIdAsync(identityKey, driveId, globalTransitId);
    }

    public async Task<DriveMainIndexRecord> GetAsync(Guid driveId, Guid fileId)
    {
        return await base.GetAsync(identityKey, driveId, fileId);
    }

    public new async Task<int> InsertAsync(DriveMainIndexRecord item)
    {
        item.identityId = identityKey;
        return await base.InsertAsync(item);
    }

    public async Task<int> DeleteAsync(Guid driveId, Guid fileId)
    {
        return await base.DeleteAsync(identityKey, driveId, fileId);
    }

    public new async Task<int> UpdateAsync(DriveMainIndexRecord item)
    {
        item.identityId = identityKey;
        return await base.UpdateAsync(item);
    }

    public new async Task<int> UpsertAsync(DriveMainIndexRecord item)
    {
        item.identityId = identityKey;
        return await base.UpsertAsync(item);
    }

    public DriveMainIndexRecord ReadAllColumns(DbDataReader rdr, Guid driveId)
    {
        return base.ReadRecordFromReader2(rdr, identityKey.Id, driveId);
    }

    // REMOVED TransferHistory and ReactionSummary by hand
    public virtual async Task<int> UpsertAllButReactionsAndTransferAsync(DriveMainIndexRecord item)
    {
        item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
        item.driveId.AssertGuidNotEmpty("Guid parameter driveId cannot be set to Empty GUID.");
        item.fileId.AssertGuidNotEmpty("Guid parameter fileId cannot be set to Empty GUID.");
        item.globalTransitId.AssertGuidNotEmpty("Guid parameter globalTransitId cannot be set to Empty GUID.");
        item.groupId.AssertGuidNotEmpty("Guid parameter groupId cannot be set to Empty GUID.");
        item.uniqueId.AssertGuidNotEmpty("Guid parameter uniqueId cannot be set to Empty GUID.");
        item.hdrVersionTag.AssertGuidNotEmpty("Guid parameter hdrVersionTag cannot be set to Empty GUID.");
        item.hdrTmpDriveAlias.AssertGuidNotEmpty("Guid parameter hdrTmpDriveAlias cannot be set to Empty GUID.");
        item.hdrTmpDriveType.AssertGuidNotEmpty("Guid parameter hdrTmpDriveType cannot be set to Empty GUID.");

        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var upsertCommand = cn.CreateCommand();

        upsertCommand.CommandText =
            "INSERT INTO driveMainIndex (identityId,driveId,fileId,globalTransitId,fileState,requiredSecurityGroup,fileSystemType,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,uniqueId,byteCount,hdrEncryptedKeyHeader,hdrVersionTag,hdrAppData,hdrServerData,hdrFileMetaData,hdrTmpDriveAlias,hdrTmpDriveType,created) " +
            "VALUES (@identityId,@driveId,@fileId,@globalTransitId,@fileState,@requiredSecurityGroup,@fileSystemType,@userDate,@fileType,@dataType,@archivalStatus,@historyStatus,@senderId,@groupId,@uniqueId,@byteCount,@hdrEncryptedKeyHeader,@hdrVersionTag,@hdrAppData,@hdrServerData,@hdrFileMetaData,@hdrTmpDriveAlias,@hdrTmpDriveType,@created)" +
            "ON CONFLICT (identityId,driveId,fileId) DO UPDATE " +
            "SET globalTransitId = @globalTransitId,fileState = @fileState,requiredSecurityGroup = @requiredSecurityGroup,fileSystemType = @fileSystemType,userDate = @userDate,fileType = @fileType,dataType = @dataType,archivalStatus = @archivalStatus,historyStatus = @historyStatus,senderId = @senderId,groupId = @groupId,uniqueId = @uniqueId,byteCount = @byteCount,hdrEncryptedKeyHeader = @hdrEncryptedKeyHeader,hdrVersionTag = @hdrVersionTag,hdrAppData = @hdrAppData,hdrServerData = @hdrServerData,hdrFileMetaData = @hdrFileMetaData,hdrTmpDriveAlias = @hdrTmpDriveAlias,hdrTmpDriveType = @hdrTmpDriveType,modified = @modified " +
            "RETURNING created, modified;";
        var upsertParam1 = upsertCommand.CreateParameter();
        upsertParam1.ParameterName = "@identityId";
        upsertCommand.Parameters.Add(upsertParam1);
        var upsertParam2 = upsertCommand.CreateParameter();
        upsertParam2.ParameterName = "@driveId";
        upsertCommand.Parameters.Add(upsertParam2);
        var upsertParam3 = upsertCommand.CreateParameter();
        upsertParam3.ParameterName = "@fileId";
        upsertCommand.Parameters.Add(upsertParam3);
        var upsertParam4 = upsertCommand.CreateParameter();
        upsertParam4.ParameterName = "@globalTransitId";
        upsertCommand.Parameters.Add(upsertParam4);
        var upsertParam5 = upsertCommand.CreateParameter();
        upsertParam5.ParameterName = "@fileState";
        upsertCommand.Parameters.Add(upsertParam5);
        var upsertParam6 = upsertCommand.CreateParameter();
        upsertParam6.ParameterName = "@requiredSecurityGroup";
        upsertCommand.Parameters.Add(upsertParam6);
        var upsertParam7 = upsertCommand.CreateParameter();
        upsertParam7.ParameterName = "@fileSystemType";
        upsertCommand.Parameters.Add(upsertParam7);
        var upsertParam8 = upsertCommand.CreateParameter();
        upsertParam8.ParameterName = "@userDate";
        upsertCommand.Parameters.Add(upsertParam8);
        var upsertParam9 = upsertCommand.CreateParameter();
        upsertParam9.ParameterName = "@fileType";
        upsertCommand.Parameters.Add(upsertParam9);
        var upsertParam10 = upsertCommand.CreateParameter();
        upsertParam10.ParameterName = "@dataType";
        upsertCommand.Parameters.Add(upsertParam10);
        var upsertParam11 = upsertCommand.CreateParameter();
        upsertParam11.ParameterName = "@archivalStatus";
        upsertCommand.Parameters.Add(upsertParam11);
        var upsertParam12 = upsertCommand.CreateParameter();
        upsertParam12.ParameterName = "@historyStatus";
        upsertCommand.Parameters.Add(upsertParam12);
        var upsertParam13 = upsertCommand.CreateParameter();
        upsertParam13.ParameterName = "@senderId";
        upsertCommand.Parameters.Add(upsertParam13);
        var upsertParam14 = upsertCommand.CreateParameter();
        upsertParam14.ParameterName = "@groupId";
        upsertCommand.Parameters.Add(upsertParam14);
        var upsertParam15 = upsertCommand.CreateParameter();
        upsertParam15.ParameterName = "@uniqueId";
        upsertCommand.Parameters.Add(upsertParam15);
        var upsertParam16 = upsertCommand.CreateParameter();
        upsertParam16.ParameterName = "@byteCount";
        upsertCommand.Parameters.Add(upsertParam16);
        var upsertParam17 = upsertCommand.CreateParameter();
        upsertParam17.ParameterName = "@hdrEncryptedKeyHeader";
        upsertCommand.Parameters.Add(upsertParam17);
        var upsertParam18 = upsertCommand.CreateParameter();
        upsertParam18.ParameterName = "@hdrVersionTag";
        upsertCommand.Parameters.Add(upsertParam18);
        var upsertParam19 = upsertCommand.CreateParameter();
        upsertParam19.ParameterName = "@hdrAppData";
        upsertCommand.Parameters.Add(upsertParam19);
        // var upsertParam20 = upsertCommand.CreateParameter();
        // upsertParam20.ParameterName = "@hdrLocalVersionTag";
        // upsertCommand.Parameters.Add(upsertParam20);
        // var upsertParam21 = upsertCommand.CreateParameter();
        // upsertParam21.ParameterName = "@hdrLocalAppData";
        // upsertCommand.Parameters.Add(upsertParam21);
        //var upsertParam22 = upsertCommand.CreateParameter();
        //upsertParam22.ParameterName = "@hdrReactionSummary";
        //upsertCommand.Parameters.Add(upsertParam22);
        var upsertParam23 = upsertCommand.CreateParameter();
        upsertParam23.ParameterName = "@hdrServerData";
        upsertCommand.Parameters.Add(upsertParam23);
        //var upsertParam24 = upsertCommand.CreateParameter();
        //upsertParam24.ParameterName = "@hdrTransferHistory";
        //upsertCommand.Parameters.Add(upsertParam24);
        var upsertParam25 = upsertCommand.CreateParameter();
        upsertParam25.ParameterName = "@hdrFileMetaData";
        upsertCommand.Parameters.Add(upsertParam25);
        var upsertParam26 = upsertCommand.CreateParameter();
        upsertParam26.ParameterName = "@hdrTmpDriveAlias";
        upsertCommand.Parameters.Add(upsertParam26);
        var upsertParam27 = upsertCommand.CreateParameter();
        upsertParam27.ParameterName = "@hdrTmpDriveType";
        upsertCommand.Parameters.Add(upsertParam27);
        var upsertParam28 = upsertCommand.CreateParameter();
        upsertParam28.ParameterName = "@created";
        upsertCommand.Parameters.Add(upsertParam28);
        var upsertParam29 = upsertCommand.CreateParameter();
        upsertParam29.ParameterName = "@modified";
        upsertCommand.Parameters.Add(upsertParam29);
        var now = UnixTimeUtcUnique.Now();
        upsertParam1.Value = item.identityId.ToByteArray();
        upsertParam2.Value = item.driveId.ToByteArray();
        upsertParam3.Value = item.fileId.ToByteArray();
        upsertParam4.Value = item.globalTransitId?.ToByteArray() ?? (object)DBNull.Value;
        upsertParam5.Value = item.fileState;
        upsertParam6.Value = item.requiredSecurityGroup;
        upsertParam7.Value = item.fileSystemType;
        upsertParam8.Value = item.userDate.milliseconds;
        upsertParam9.Value = item.fileType;
        upsertParam10.Value = item.dataType;
        upsertParam11.Value = item.archivalStatus;
        upsertParam12.Value = item.historyStatus;
        upsertParam13.Value = item.senderId ?? (object)DBNull.Value;
        upsertParam14.Value = item.groupId?.ToByteArray() ?? (object)DBNull.Value;
        upsertParam15.Value = item.uniqueId?.ToByteArray() ?? (object)DBNull.Value;
        upsertParam16.Value = item.byteCount;
        upsertParam17.Value = item.hdrEncryptedKeyHeader;
        upsertParam18.Value = item.hdrVersionTag.ToByteArray();
        upsertParam19.Value = item.hdrAppData;
        // hdrLocalAppData and hdrLocalVersionTag are set in a specific method
        // upsertParam20.Value = item.hdrLocalVersionTag?.ToByteArray() ?? (object)DBNull.Value;
        // upsertParam21.Value = item.hdrLocalAppData ?? (object)DBNull.Value;
        //upsertParam22.Value = item.hdrReactionSummary ?? (object)DBNull.Value;
        upsertParam23.Value = item.hdrServerData;
        //upsertParam24.Value = item.hdrTransferHistory ?? (object)DBNull.Value;
        upsertParam25.Value = item.hdrFileMetaData;
        upsertParam26.Value = item.hdrTmpDriveAlias.ToByteArray();
        upsertParam27.Value = item.hdrTmpDriveType.ToByteArray();
        upsertParam28.Value = now.uniqueTime;
        upsertParam29.Value = now.uniqueTime;

        using (var rdr = await upsertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow))
        {
            if (await rdr.ReadAsync())
            {
                long created = rdr.GetInt64(0);
                long? modified = rdr.IsDBNull(1) ? null : rdr.GetInt64(1);
                item.created = new UnixTimeUtcUnique(created);
                if (modified != null)
                    item.modified = new UnixTimeUtcUnique((long)modified);
                else
                    item.modified = null;
                return 1;
            }
        }

        return 0;
    }

    public async Task<int> UpdateLocalAppMetadata(Guid driveId, Guid fileId, Guid versionTag, string content)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var updateCommand = cn.CreateCommand();

        //modified=@modified
        updateCommand.CommandText = $"UPDATE driveMainIndex " +
                                    $"SET hdrLocalVersionTag=@hdrLocalVersionTag,hdrLocalAppData=@hdrLocalAppData " +
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
        versionTagParam.Value = versionTag;
        contentParam.Value = content;
        // modifiedParam.Value = UnixTimeUtcUnique.Now().uniqueTime; //modified

        return await updateCommand.ExecuteNonQueryAsync();
    }

    public async Task<int> UpdateReactionSummaryAsync(Guid driveId, Guid fileId, string reactionSummary)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var updateCommand = cn.CreateCommand();

        updateCommand.CommandText =
            $"UPDATE driveMainIndex SET modified=@modified,hdrReactionSummary=@hdrReactionSummary WHERE identityId=@identityId AND driveid=@driveId AND fileId=@fileId;";

        var sparam1 = updateCommand.CreateParameter();
        var sparam2 = updateCommand.CreateParameter();
        var sparam3 = updateCommand.CreateParameter();
        var sparam4 = updateCommand.CreateParameter();
        var sparam5 = updateCommand.CreateParameter();

        sparam1.ParameterName = "@identityId";
        sparam2.ParameterName = "@driveId";
        sparam3.ParameterName = "@fileId";
        sparam4.ParameterName = "@hdrReactionSummary";
        sparam5.ParameterName = "@modified";

        updateCommand.Parameters.Add(sparam1);
        updateCommand.Parameters.Add(sparam2);
        updateCommand.Parameters.Add(sparam3);
        updateCommand.Parameters.Add(sparam4);
        updateCommand.Parameters.Add(sparam5);

        sparam1.Value = identityKey.ToByteArray();
        sparam2.Value = driveId.ToByteArray();
        sparam3.Value = fileId.ToByteArray();
        sparam4.Value = reactionSummary;
        sparam5.Value = UnixTimeUtcUnique.Now().uniqueTime;

        return await updateCommand.ExecuteNonQueryAsync();
    }

    public async Task<int> UpdateTransferHistoryAsync(Guid driveId, Guid fileId, string transferHistory)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var updateCommand = cn.CreateCommand();

        updateCommand.CommandText =
            $"UPDATE driveMainIndex SET modified=@modified,hdrTransferHistory=@hdrTransferHistory WHERE identityId=@identityId AND driveid=@driveId AND fileId=@fileId;";

        var sparam1 = updateCommand.CreateParameter();
        var sparam2 = updateCommand.CreateParameter();
        var sparam3 = updateCommand.CreateParameter();
        var sparam4 = updateCommand.CreateParameter();
        var sparam5 = updateCommand.CreateParameter();

        sparam1.ParameterName = "@identityId";
        sparam2.ParameterName = "@driveId";
        sparam3.ParameterName = "@fileId";
        sparam4.ParameterName = "@hdrTransferHistory";
        sparam5.ParameterName = "@modified";

        updateCommand.Parameters.Add(sparam1);
        updateCommand.Parameters.Add(sparam2);
        updateCommand.Parameters.Add(sparam3);
        updateCommand.Parameters.Add(sparam4);
        updateCommand.Parameters.Add(sparam5);

        sparam1.Value = identityKey.ToByteArray();
        sparam2.Value = driveId.ToByteArray();
        sparam3.Value = fileId.ToByteArray();
        sparam4.Value = transferHistory;
        sparam5.Value = UnixTimeUtcUnique.Now().uniqueTime;

        return await updateCommand.ExecuteNonQueryAsync();
    }


    public async Task<(Int64, Int64)> GetDriveSizeDirtyAsync(Guid driveId)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var sizeCommand = cn.CreateCommand();

        sizeCommand.CommandText =
            $"SELECT count(*), sum(byteCount) FROM drivemainindex WHERE identityId=@identityId AND driveid=@driveId;";

        var sparam1 = sizeCommand.CreateParameter();
        sparam1.ParameterName = "@driveId";
        sizeCommand.Parameters.Add(sparam1);

        var sparam2 = sizeCommand.CreateParameter();
        sparam2.ParameterName = "@identityId";
        sizeCommand.Parameters.Add(sparam2);

        sparam1.Value = driveId.ToByteArray();
        sparam2.Value = identityKey.ToByteArray();

        using (var rdr = await sizeCommand.ExecuteReaderAsync(CommandBehavior.Default))
        {
            if (await rdr.ReadAsync())
            {
                var count = rdr.IsDBNull(0) ? 0 : rdr.GetInt64(0);
                var size = rdr.IsDBNull(1) ? 0 : rdr.GetInt64(1);
                return (count, size);
            }
        }

        return (-1, -1);
    }


    /// <summary>
    /// For testing only. Updates the updatedTimestamp for the supplied item.
    /// </summary>
    /// <param name="fileId">Item to touch</param>
    internal async Task<int> TestTouchAsync(Guid driveId, Guid fileId)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var touchCommand = cn.CreateCommand();

        touchCommand.CommandText =
            $"UPDATE drivemainindex SET modified=@modified WHERE identityId = @identityId AND driveId = @driveId AND fileid = @fileid;";

        var tparam1 = touchCommand.CreateParameter();
        var tparam2 = touchCommand.CreateParameter();
        var tparam3 = touchCommand.CreateParameter();
        var tparam4 = touchCommand.CreateParameter();

        tparam1.ParameterName = "@fileid";
        tparam2.ParameterName = "@modified";
        tparam3.ParameterName = "@driveId";
        tparam4.ParameterName = "@identityId";

        touchCommand.Parameters.Add(tparam1);
        touchCommand.Parameters.Add(tparam2);
        touchCommand.Parameters.Add(tparam3);
        touchCommand.Parameters.Add(tparam4);

        tparam1.Value = fileId.ToByteArray();
        tparam2.Value = UnixTimeUtcUniqueGenerator.Generator().uniqueTime;
        tparam3.Value = driveId.ToByteArray();
        tparam4.Value = identityKey.ToByteArray();

        return await touchCommand.ExecuteNonQueryAsync();
    }
}