using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Time;
using Odin.Core.Util;

[assembly: InternalsVisibleTo("Odin.Services.Drives.DriveCore.Storage")]

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableDriveMainIndex(
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    OdinIdentity odinIdentity)
    : TableDriveMainIndexCRUD(scopedConnectionFactory)
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

    public DriveMainIndexRecord ReadAllColumns(DbDataReader rdr, Guid driveId)
    {
        return base.ReadRecordFromReader1(rdr, odinIdentity.IdentityId, driveId);
    }

    // REMOVED TransferHistory and ReactionSummary and localAppData by hand
    public virtual async Task<int> UpsertAllButReactionsAndTransferAsync(DriveMainIndexRecord item, Guid? useThisNewVersionTag = null)
    {
        if (useThisNewVersionTag == null)
            useThisNewVersionTag = SequentialGuid.CreateGuid();
        else if (useThisNewVersionTag == Guid.Empty)
            throw new ArgumentException("useThisNewVersionTag not allowed to be an empty guid");

        if (item.hdrVersionTag == useThisNewVersionTag)
            throw new ArgumentException("useThisNewVersionTag==item.hdrVersionTag : Fy fy, skamme skamme, man må ikke snyde");

        // If it is a new file, and the caller likely didn't set a VersionTag, we'll assign it the new version
        if (item.hdrVersionTag == Guid.Empty)
            item.hdrVersionTag = useThisNewVersionTag.Value;

        item.Validate();

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
            "RETURNING created,modified,rowId;";

        upsertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
        upsertCommand.AddParameter("@driveId", DbType.Binary, item.driveId);
        upsertCommand.AddParameter("@fileId", DbType.Binary, item.fileId);
        upsertCommand.AddParameter("@globalTransitId", DbType.Binary, item.globalTransitId);
        upsertCommand.AddParameter("@fileState", DbType.Int32, item.fileState);
        upsertCommand.AddParameter("@requiredSecurityGroup", DbType.Int32, item.requiredSecurityGroup);
        upsertCommand.AddParameter("@fileSystemType", DbType.Int32, item.fileSystemType);
        upsertCommand.AddParameter("@userDate", DbType.Int64, item.userDate.milliseconds);
        upsertCommand.AddParameter("@fileType", DbType.Int32, item.fileType);
        upsertCommand.AddParameter("@dataType", DbType.Int32, item.dataType);
        upsertCommand.AddParameter("@archivalStatus", DbType.Int32, item.archivalStatus);
        upsertCommand.AddParameter("@historyStatus", DbType.Int32, item.historyStatus);
        upsertCommand.AddParameter("@senderId", DbType.String, item.senderId);
        upsertCommand.AddParameter("@groupId", DbType.Binary, item.groupId);
        upsertCommand.AddParameter("@uniqueId", DbType.Binary, item.uniqueId);
        upsertCommand.AddParameter("@byteCount", DbType.Int64, item.byteCount);
        upsertCommand.AddParameter("@hdrEncryptedKeyHeader", DbType.String, item.hdrEncryptedKeyHeader);
        upsertCommand.AddParameter("@hdrVersionTag", DbType.Binary, item.hdrVersionTag);
        upsertCommand.AddParameter("@hdrAppData", DbType.String, item.hdrAppData);
        // upsertCommand.AddParameter("@hdrLocalVersionTag", DbType.Binary, item.hdrLocalVersionTag);
        // upsertCommand.AddParameter("@hdrLocalAppData", DbType.String, item.hdrLocalAppData);
        // upsertCommand.AddParameter("@hdrReactionSummary", DbType.String, item.hdrReactionSummary);
        upsertCommand.AddParameter("@hdrServerData", DbType.String, item.hdrServerData);
        // upsertCommand.AddParameter("@hdrTransferHistory", DbType.String, item.hdrTransferHistory);
        upsertCommand.AddParameter("@hdrFileMetaData", DbType.String, item.hdrFileMetaData);
        upsertCommand.AddParameter("@hdrTmpDriveAlias", DbType.Binary, item.hdrTmpDriveAlias);
        upsertCommand.AddParameter("@hdrTmpDriveType", DbType.Binary, item.hdrTmpDriveType);
        upsertCommand.AddParameter("@newVersionTag", DbType.Binary, useThisNewVersionTag.Value);

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

        updateCommand.AddParameter("@identityId", DbType.Binary, odinIdentity.IdentityId);
        updateCommand.AddParameter("@driveId", DbType.Binary, driveId);
        updateCommand.AddParameter("@fileId", DbType.Binary, fileId);
        updateCommand.AddParameter("@hdrReactionSummary", DbType.String, reactionSummary);

        return await updateCommand.ExecuteNonQueryAsync();
    }

    public async Task<(int, long)> UpdateTransferSummaryAsync(Guid driveId, Guid fileId, string transferHistory)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var updateCommand = cn.CreateCommand();

        string sqlNowStr = updateCommand.SqlNow();

        updateCommand.CommandText = $"UPDATE driveMainIndex SET modified={updateCommand.SqlMax()}(driveMainIndex.modified+1,{sqlNowStr}), hdrTransferHistory=@hdrTransferHistory " +
                                    $"WHERE identityId=@identityId AND driveid=@driveId AND fileId=@fileId RETURNING driveMainIndex.modified;";

        updateCommand.AddParameter("@identityId", DbType.Binary, odinIdentity.IdentityId);
        updateCommand.AddParameter("@driveId", DbType.Binary, driveId);
        updateCommand.AddParameter("@fileId", DbType.Binary, fileId);
        updateCommand.AddParameter("@hdrTransferHistory", DbType.String, transferHistory);

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

        sizeCommand.AddParameter("@identityId", DbType.Binary, odinIdentity.IdentityId);
        sizeCommand.AddParameter("@driveId", DbType.Binary, driveId);

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

        cmd.AddParameter("@identityId", DbType.Binary, odinIdentity.IdentityId);

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
            $"UPDATE drivemainindex SET byteCount=@bcount WHERE identityId = @identityId AND driveId = @driveId AND fileid = @fileId;";

        cmd.AddParameter("@identityId", DbType.Binary, odinIdentity.IdentityId);
        cmd.AddParameter("@driveId", DbType.Binary, driveId);
        cmd.AddParameter("@fileId", DbType.Binary, fileId);
        cmd.AddParameter("@bcount", DbType.Int64, byteCount);

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
            $"UPDATE drivemainindex SET modified={touchCommand.SqlMax()}(driveMainIndex.modified+1,{sqlNowStr}) WHERE identityId = @identityId AND driveId = @driveId AND fileid = @fileId RETURNING modified;";

        touchCommand.AddParameter("@identityId", DbType.Binary, odinIdentity.IdentityId);
        touchCommand.AddParameter("@driveId", DbType.Binary, driveId);
        touchCommand.AddParameter("@fileId", DbType.Binary, fileId);

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

    // For defragmenter only (that's why it's internal)
    // Copy of UpdateAsync with Validation() and modified time removed
    // It does NOT update reactionSummary and transferHistory and localAppData! 
    // (for the same reason the regular one doesn't)
    public async Task<int> RawUpdateAsync(DriveMainIndexRecord item)
    {
        // Skip vaildation deliberately: item.Validate();
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var updateCommand = cn.CreateCommand();
        {
            // item.Validate();

            string sqlNowStr = updateCommand.SqlNow();
            updateCommand.CommandText = "UPDATE DriveMainIndex " +
                                        $"SET globalTransitId = @globalTransitId,fileState = @fileState,requiredSecurityGroup = @requiredSecurityGroup,fileSystemType = @fileSystemType,userDate = @userDate,fileType = @fileType,dataType = @dataType,archivalStatus = @archivalStatus,historyStatus = @historyStatus,senderId = @senderId,groupId = @groupId,uniqueId = @uniqueId,byteCount = @byteCount,hdrEncryptedKeyHeader = @hdrEncryptedKeyHeader,hdrVersionTag = @hdrVersionTag,hdrAppData = @hdrAppData,hdrServerData = @hdrServerData,hdrFileMetaData = @hdrFileMetaData,hdrTmpDriveAlias = @hdrTmpDriveAlias,hdrTmpDriveType = @hdrTmpDriveType " +
                                        "WHERE (identityId = @identityId AND driveId = @driveId AND fileId = @fileId) " +
                                        "RETURNING created,modified,rowId;";

            updateCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
            updateCommand.AddParameter("@driveId", DbType.Binary, item.driveId);
            updateCommand.AddParameter("@fileId", DbType.Binary, item.fileId);
            updateCommand.AddParameter("@globalTransitId", DbType.Binary, item.globalTransitId);
            updateCommand.AddParameter("@fileState", DbType.Int32, item.fileState);
            updateCommand.AddParameter("@requiredSecurityGroup", DbType.Int32, item.requiredSecurityGroup);
            updateCommand.AddParameter("@fileSystemType", DbType.Int32, item.fileSystemType);
            updateCommand.AddParameter("@userDate", DbType.Int64, item.userDate.milliseconds);
            updateCommand.AddParameter("@fileType", DbType.Int32, item.fileType);
            updateCommand.AddParameter("@dataType", DbType.Int32, item.dataType);
            updateCommand.AddParameter("@archivalStatus", DbType.Int32, item.archivalStatus);
            updateCommand.AddParameter("@historyStatus", DbType.Int32, item.historyStatus);
            updateCommand.AddParameter("@senderId", DbType.String, item.senderId);
            updateCommand.AddParameter("@groupId", DbType.Binary, item.groupId);
            updateCommand.AddParameter("@uniqueId", DbType.Binary, item.uniqueId);
            updateCommand.AddParameter("@byteCount", DbType.Int64, item.byteCount);
            updateCommand.AddParameter("@hdrEncryptedKeyHeader", DbType.String, item.hdrEncryptedKeyHeader);
            updateCommand.AddParameter("@hdrVersionTag", DbType.Binary, item.hdrVersionTag);
            updateCommand.AddParameter("@hdrAppData", DbType.String, item.hdrAppData);
            // updateCommand.AddParameter("@hdrLocalVersionTag", DbType.Binary, item.hdrLocalVersionTag);
            // updateCommand.AddParameter("@hdrLocalAppData", DbType.String, item.hdrLocalAppData);
            // updateCommand.AddParameter("@hdrReactionSummary", DbType.String, item.hdrReactionSummary);
            updateCommand.AddParameter("@hdrServerData", DbType.String, item.hdrServerData);
            // updateCommand.AddParameter("@hdrTransferHistory", DbType.String, item.hdrTransferHistory);
            updateCommand.AddParameter("@hdrFileMetaData", DbType.String, item.hdrFileMetaData);
            updateCommand.AddParameter("@hdrTmpDriveAlias", DbType.Binary, item.hdrTmpDriveAlias);
            updateCommand.AddParameter("@hdrTmpDriveType", DbType.Binary, item.hdrTmpDriveType);

            await using var rdr = await updateCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
            if (await rdr.ReadAsync())
            {
                long created = (long)rdr[0];
                item.created = new UnixTimeUtc(created);
                long modified = (long)rdr[1];
                item.modified = new UnixTimeUtc((long)modified);
                item.rowId = (long)rdr[2];
                return 1;
            }
            return 0;
        }
    }
}