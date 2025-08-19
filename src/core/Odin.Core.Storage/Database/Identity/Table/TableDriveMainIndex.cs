using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Time;
using Odin.Core.Util;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableDriveMainIndex(
    CacheHelper cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    OdinIdentity odinIdentity)
    : TableDriveMainIndexCRUD(cache, scopedConnectionFactory)
{
    private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;

    public async Task<List<DriveMainIndexRecord>> GetAllByDriveIdAsync(Guid driveId)
    {
        return await base.GetAllByDriveIdAsync(odinIdentity, driveId);
    }

    public async Task<DriveMainIndexRecord> GetByUniqueIdAsync(Guid driveId, Guid? uniqueId)
    {
        return await base.GetByUniqueIdAsync(odinIdentity, driveId, uniqueId);
    }

    public async Task<DriveMainIndexRecord> GetByGlobalTransitIdAsync(Guid driveId, Guid? globalTransitId)
    {
        return await base.GetByGlobalTransitIdAsync(odinIdentity, driveId, globalTransitId);
    }

    public async Task<DriveMainIndexRecord> GetAsync(Guid driveId, Guid fileId)
    {
        return await base.GetAsync(odinIdentity, driveId, fileId);
    }

    public new async Task<int> InsertAsync(DriveMainIndexRecord item)
    {
        item.identityId = odinIdentity;
        return await base.InsertAsync(item);
    }

    public async Task<int> DeleteAsync(Guid driveId, Guid fileId)
    {
        return await base.DeleteAsync(odinIdentity, driveId, fileId);
    }

    //public new async Task<int> UpdateAsync(DriveMainIndexRecord item)
    //{
    //    item.identityId = odinIdentity;
    //    return await base.UpdateAsync(item);
    //}

    //public new async Task<int> UpsertAsync(DriveMainIndexRecord item)
    //{
    //    item.identityId = odinIdentity;
    //    return await base.UpsertAsync(item);
    //}

    public DriveMainIndexRecord ReadAllColumns(DbDataReader rdr, Guid driveId)
    {
        return base.ReadRecordFromReader1(rdr, odinIdentity.Id, driveId);
    }

    // REMOVED TransferHistory and ReactionSummary and localAppData by hand
    public virtual async Task<int> UpsertAllButReactionsAndTransferAsync(DriveMainIndexRecord item, Guid? useThisNewVersionTag = null)
    {
        item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
        item.driveId.AssertGuidNotEmpty("Guid parameter driveId cannot be set to Empty GUID.");
        item.fileId.AssertGuidNotEmpty("Guid parameter fileId cannot be set to Empty GUID.");
        item.globalTransitId.AssertGuidNotEmpty("Guid parameter globalTransitId cannot be set to Empty GUID.");
        item.groupId.AssertGuidNotEmpty("Guid parameter groupId cannot be set to Empty GUID.");
        item.uniqueId.AssertGuidNotEmpty("Guid parameter uniqueId cannot be set to Empty GUID.");

        if (useThisNewVersionTag == null)
            useThisNewVersionTag = SequentialGuid.CreateGuid();
        else if (useThisNewVersionTag == Guid.Empty)
            throw new ArgumentException("useThisNewVersionTag not allowed to be an empty guid");


        if (item.hdrVersionTag == useThisNewVersionTag)
            throw new ArgumentException("useThisNewVersionTag==item.hdrVersionTag : Fy fy, skamme skamme, man må ikke snyde");

        // If it is a new file, and the caller likely didn't set a VersionTag, we'll assign it the new version
        if (item.hdrVersionTag == Guid.Empty)
            item.hdrVersionTag = useThisNewVersionTag.Value;

        item.hdrTmpDriveAlias.AssertGuidNotEmpty("Guid parameter hdrTmpDriveAlias cannot be set to Empty GUID.");
        item.hdrTmpDriveType.AssertGuidNotEmpty("Guid parameter hdrTmpDriveType cannot be set to Empty GUID.");

        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var upsertCommand = cn.CreateCommand();
        await using var tx = await cn.BeginStackedTransactionAsync(); // The SQL below requires a transaction

        string sqlNowStr = upsertCommand.SqlNow();

        upsertCommand.CommandText =
            "INSERT INTO driveMainIndex (identityId,driveId,fileId,globalTransitId,fileState,requiredSecurityGroup,fileSystemType,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,uniqueId,byteCount,hdrEncryptedKeyHeader,hdrVersionTag,hdrAppData,hdrServerData,hdrFileMetaData,hdrTmpDriveAlias,hdrTmpDriveType,created,modified) " +
            $"VALUES (@identityId,@driveId,@fileId,@globalTransitId,@fileState,@requiredSecurityGroup,@fileSystemType,@userDate,@fileType,@dataType,@archivalStatus,@historyStatus,@senderId,@groupId,@uniqueId,@byteCount,@hdrEncryptedKeyHeader,@hdrVersionTag,@hdrAppData,@hdrServerData,@hdrFileMetaData,@hdrTmpDriveAlias,@hdrTmpDriveType,{sqlNowStr},{sqlNowStr}) " +
            "ON CONFLICT (identityId,driveId,fileId) DO UPDATE " +
            $"SET globalTransitId=COALESCE(driveMainIndex.globalTransitId, @globalTransitId),fileState = @fileState,requiredSecurityGroup = @requiredSecurityGroup,fileSystemType = @fileSystemType,userDate = @userDate,fileType = @fileType,dataType = @dataType,archivalStatus = @archivalStatus,historyStatus = @historyStatus,senderId = @senderId,groupId = @groupId,uniqueId = @uniqueId,byteCount = @byteCount,hdrEncryptedKeyHeader = @hdrEncryptedKeyHeader,hdrVersionTag = @newVersionTag,hdrAppData = @hdrAppData,hdrServerData = @hdrServerData,hdrFileMetaData = @hdrFileMetaData,hdrTmpDriveAlias = @hdrTmpDriveAlias,hdrTmpDriveType = @hdrTmpDriveType,modified = {upsertCommand.SqlMax()}(driveMainIndex.modified+1,{sqlNowStr}) " +
            "WHERE driveMainIndex.hdrVersionTag = @hdrVersionTag " +
            "RETURNING created, driveMainIndex.modified, rowid;";

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
        upsertParam28.ParameterName = "@newVersionTag";
        upsertCommand.Parameters.Add(upsertParam28);

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
        upsertParam28.Value = useThisNewVersionTag.Value.ToByteArray();

        using (var rdr = await upsertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow))
        {
            tx.Commit();
            if (await rdr.ReadAsync())
            {
                long created = (long)rdr[0];
                long modified = (long)rdr[1];
                item.created = new UnixTimeUtc(created);
                item.modified = new UnixTimeUtc(modified);

                if (modified != created)
                    item.hdrVersionTag = useThisNewVersionTag.Value;

                item.rowId = (long)rdr[2];
                return 1;
            }
            else
            {
                throw new OdinClientException($"Mismatching version tag {item.hdrVersionTag}", OdinClientErrorCode.VersionTagMismatch);
            }
        }

        // Unreachable return 0;
    }
    
    public async Task<int> UpdateReactionSummaryAsync(Guid driveId, Guid fileId, string reactionSummary)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var updateCommand = cn.CreateCommand();

        string sqlNowStr = updateCommand.SqlNow();
        updateCommand.CommandText =
            $"UPDATE driveMainIndex SET modified={updateCommand.SqlMax()}(driveMainIndex.modified+1,{sqlNowStr}),hdrReactionSummary=@hdrReactionSummary WHERE identityId=@identityId AND driveid=@driveId AND fileId=@fileId;";

        var sparam1 = updateCommand.CreateParameter();
        var sparam2 = updateCommand.CreateParameter();
        var sparam3 = updateCommand.CreateParameter();
        var sparam4 = updateCommand.CreateParameter();

        sparam1.ParameterName = "@identityId";
        sparam2.ParameterName = "@driveId";
        sparam3.ParameterName = "@fileId";
        sparam4.ParameterName = "@hdrReactionSummary";

        updateCommand.Parameters.Add(sparam1);
        updateCommand.Parameters.Add(sparam2);
        updateCommand.Parameters.Add(sparam3);
        updateCommand.Parameters.Add(sparam4);

        sparam1.Value = odinIdentity.IdAsByteArray();
        sparam2.Value = driveId.ToByteArray();
        sparam3.Value = fileId.ToByteArray();
        sparam4.Value = reactionSummary;

        return await updateCommand.ExecuteNonQueryAsync();
    }

    public async Task<(int, long)> UpdateTransferSummaryAsync(Guid driveId, Guid fileId, string transferHistory)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var updateCommand = cn.CreateCommand();

        string sqlNowStr = updateCommand.SqlNow();

        updateCommand.CommandText = $"UPDATE driveMainIndex SET modified={updateCommand.SqlMax()}(driveMainIndex.modified+1,{sqlNowStr}), hdrTransferHistory=@hdrTransferHistory " +
                                    $"WHERE identityId=@identityId AND driveid=@driveId AND fileId=@fileId RETURNING driveMainIndex.modified;";

        var sparam1 = updateCommand.CreateParameter();
        var sparam2 = updateCommand.CreateParameter();
        var sparam3 = updateCommand.CreateParameter();
        var sparam4 = updateCommand.CreateParameter();

        sparam1.ParameterName = "@identityId";
        sparam2.ParameterName = "@driveId";
        sparam3.ParameterName = "@fileId";
        sparam4.ParameterName = "@hdrTransferHistory";

        updateCommand.Parameters.Add(sparam1);
        updateCommand.Parameters.Add(sparam2);
        updateCommand.Parameters.Add(sparam3);
        updateCommand.Parameters.Add(sparam4);

        sparam1.Value = odinIdentity.IdAsByteArray();
        sparam2.Value = driveId.ToByteArray();
        sparam3.Value = fileId.ToByteArray();
        sparam4.Value = transferHistory;

        using (var rdr = await updateCommand.ExecuteReaderAsync(CommandBehavior.Default))
        {
            if (await rdr.ReadAsync())
            {
                var modified = (rdr[0] == DBNull.Value) ? 0 : (Int64)rdr[0];
                return (1, modified);
            }
        }

        return (0, 0);
    }

    public async Task<(Int64, Int64)> GetDriveSizeAsync(Guid driveId)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var sizeCommand = cn.CreateCommand();

        // Eh - byteCount shouldn't be able to be zero.... 
        sizeCommand.CommandText =
            """
            SELECT count(*), CAST(COALESCE(SUM(byteCount), 0) AS BIGINT)
            FROM drivemainindex
            WHERE identityId=@identityId AND driveid=@driveId;
            """;

        var sparam1 = sizeCommand.CreateParameter();
        sparam1.ParameterName = "@driveId";
        sizeCommand.Parameters.Add(sparam1);

        var sparam2 = sizeCommand.CreateParameter();
        sparam2.ParameterName = "@identityId";
        sizeCommand.Parameters.Add(sparam2);

        sparam1.Value = driveId.ToByteArray();
        sparam2.Value = odinIdentity.IdAsByteArray();

        using (var rdr = await sizeCommand.ExecuteReaderAsync(CommandBehavior.Default))
        {
            if (await rdr.ReadAsync())
            {
                var count = (rdr[0] == DBNull.Value) ? 0 : (Int64)rdr[0];
                var size = (rdr[1] == DBNull.Value) ? 0 : (Int64)rdr[1];
                return (count, size);
            }
        }

        return (-1, -1);
    }

    public async Task<long> GetTotalSizeAllDrivesAsync()
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();

        cmd.CommandText =
            """
            SELECT CAST(COALESCE(SUM(byteCount), 0) AS BIGINT)
            FROM DriveMainIndex
            WHERE identityId=@identityId;
            """;

        var identityId = cmd.CreateParameter();
        identityId.ParameterName = "@identityId";
        identityId.Value = odinIdentity.IdAsByteArray();
        cmd.Parameters.Add(identityId);

        var size = 0L;
        await using (var rdr = await cmd.ExecuteReaderAsync())
        {
            if (await rdr.ReadAsync())
            {
                size = rdr[0] == DBNull.Value ? 0 : (long)rdr[0];
            }
        }

        return size;
    }


    /// <summary>
    /// For defragmenter only. Updates the byteCount column in the DB.
    /// </summary>
    public async Task<int> UpdateByteCountAsync(Guid driveId, Guid fileId, long byteCount)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();

        cmd.CommandText =
            $"UPDATE drivemainindex SET byteCount=@bcount WHERE identityId = @identityId AND driveId = @driveId AND fileid = @fileid;";

        var tparam1 = cmd.CreateParameter();
        var tparam2 = cmd.CreateParameter();
        var tparam3 = cmd.CreateParameter();
        var tparam4 = cmd.CreateParameter();

        tparam1.ParameterName = "@fileid";
        tparam2.ParameterName = "@driveId";
        tparam3.ParameterName = "@identityId";
        tparam3.ParameterName = "@bcount";

        cmd.Parameters.Add(tparam1);
        cmd.Parameters.Add(tparam2);
        cmd.Parameters.Add(tparam3);
        cmd.Parameters.Add(tparam4);

        tparam1.Value = fileId.ToByteArray();
        tparam2.Value = driveId.ToByteArray();
        tparam3.Value = odinIdentity.IdAsByteArray();
        tparam4.Value = byteCount;

        return await cmd.ExecuteNonQueryAsync();
    }


    /// <summary>
    /// For testing only. Updates the updatedTimestamp for the supplied item.
    /// </summary>
    /// <param name="fileId">Item to touch</param>
    internal async Task<(int, long)> TestTouchAsync(Guid driveId, Guid fileId)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var touchCommand = cn.CreateCommand();

        string sqlNowStr = touchCommand.SqlNow();

        touchCommand.CommandText =
            $"UPDATE drivemainindex SET modified={touchCommand.SqlMax()}(driveMainIndex.modified+1,{sqlNowStr}) WHERE identityId = @identityId AND driveId = @driveId AND fileid = @fileid RETURNING modified;";

        var tparam1 = touchCommand.CreateParameter();
        var tparam3 = touchCommand.CreateParameter();
        var tparam4 = touchCommand.CreateParameter();

        tparam1.ParameterName = "@fileid";
        tparam3.ParameterName = "@driveId";
        tparam4.ParameterName = "@identityId";

        touchCommand.Parameters.Add(tparam1);
        touchCommand.Parameters.Add(tparam3);
        touchCommand.Parameters.Add(tparam4);

        tparam1.Value = fileId.ToByteArray();
        tparam3.Value = driveId.ToByteArray();
        tparam4.Value = odinIdentity.IdAsByteArray();

        using (var rdr = await touchCommand.ExecuteReaderAsync(CommandBehavior.Default))
        {
            if (await rdr.ReadAsync())
            {
                var modified = (long)rdr[0];
                return (1, modified);
            }
        }

        return (0, 0);
    }
}