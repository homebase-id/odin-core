using System;
using System.Data;
using System.Data.Common;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Odin.Core.Time;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Attestation.Connection;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.KeyChain.Connection;
using Odin.Core.Storage.Database.Notary.Connection;
using Odin.Core.Storage.Database.System.Connection;
using Odin.Core.Storage.Factory;
using Odin.Core.Storage;
using Odin.Core.Util;
using Odin.Core.Storage.Exceptions;
using Odin.Core.Storage.SQLite; //added for homebase social sync

// THIS FILE IS AUTO GENERATED - DO NOT EDIT

namespace Odin.Core.Storage.Database.Identity.Table
{
    public record DriveMainIndexRecord
    {
        public Int64 rowId { get; set; }
        public Guid identityId { get; set; }
        public Guid driveId { get; set; }
        public Guid fileId { get; set; }
        public Guid? globalTransitId { get; set; }
        public Int32 fileState { get; set; }
        public Int32 requiredSecurityGroup { get; set; }
        public Int32 fileSystemType { get; set; }
        public UnixTimeUtc userDate { get; set; }
        public Int32 fileType { get; set; }
        public Int32 dataType { get; set; }
        public Int32 archivalStatus { get; set; }
        public Int32 historyStatus { get; set; }
        public string senderId { get; set; }
        public Guid? groupId { get; set; }
        public Guid? uniqueId { get; set; }
        public Int64 byteCount { get; set; }
        public string hdrEncryptedKeyHeader { get; set; }
        public Guid hdrVersionTag { get; set; }
        public string hdrAppData { get; set; }
        public Guid? hdrLocalVersionTag { get; set; }
        public string hdrLocalAppData { get; set; }
        public string hdrReactionSummary { get; set; }
        public string hdrServerData { get; set; }
        public string hdrTransferHistory { get; set; }
        public string hdrFileMetaData { get; set; }
        public Guid hdrTmpDriveAlias { get; set; }
        public Guid hdrTmpDriveType { get; set; }
        public UnixTimeUtc created { get; set; }
        public UnixTimeUtc modified { get; set; }
        public void Validate()
        {
            identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            driveId.AssertGuidNotEmpty("Guid parameter driveId cannot be set to Empty GUID.");
            fileId.AssertGuidNotEmpty("Guid parameter fileId cannot be set to Empty GUID.");
            globalTransitId.AssertGuidNotEmpty("Guid parameter globalTransitId cannot be set to Empty GUID.");
            if (senderId?.Length < 0) throw new OdinDatabaseValidationException($"Too short senderId, was {senderId.Length} (min 0)");
            if (senderId?.Length > 256) throw new OdinDatabaseValidationException($"Too long senderId, was {senderId.Length} (max 256)");
            groupId.AssertGuidNotEmpty("Guid parameter groupId cannot be set to Empty GUID.");
            uniqueId.AssertGuidNotEmpty("Guid parameter uniqueId cannot be set to Empty GUID.");
            if (hdrEncryptedKeyHeader == null) throw new OdinDatabaseValidationException("Cannot be null hdrEncryptedKeyHeader");
            if (hdrEncryptedKeyHeader?.Length < 16) throw new OdinDatabaseValidationException($"Too short hdrEncryptedKeyHeader, was {hdrEncryptedKeyHeader.Length} (min 16)");
            if (hdrEncryptedKeyHeader?.Length > 512) throw new OdinDatabaseValidationException($"Too long hdrEncryptedKeyHeader, was {hdrEncryptedKeyHeader.Length} (max 512)");
            hdrVersionTag.AssertGuidNotEmpty("Guid parameter hdrVersionTag cannot be set to Empty GUID.");
            if (hdrAppData == null) throw new OdinDatabaseValidationException("Cannot be null hdrAppData");
            if (hdrAppData?.Length < 0) throw new OdinDatabaseValidationException($"Too short hdrAppData, was {hdrAppData.Length} (min 0)");
            if (hdrAppData?.Length > 21504) throw new OdinDatabaseValidationException($"Too long hdrAppData, was {hdrAppData.Length} (max 21504)");
            hdrLocalVersionTag.AssertGuidNotEmpty("Guid parameter hdrLocalVersionTag cannot be set to Empty GUID.");
            if (hdrLocalAppData?.Length < 0) throw new OdinDatabaseValidationException($"Too short hdrLocalAppData, was {hdrLocalAppData.Length} (min 0)");
            if (hdrLocalAppData?.Length > 4096) throw new OdinDatabaseValidationException($"Too long hdrLocalAppData, was {hdrLocalAppData.Length} (max 4096)");
            if (hdrReactionSummary?.Length < 0) throw new OdinDatabaseValidationException($"Too short hdrReactionSummary, was {hdrReactionSummary.Length} (min 0)");
            if (hdrReactionSummary?.Length > 4096) throw new OdinDatabaseValidationException($"Too long hdrReactionSummary, was {hdrReactionSummary.Length} (max 4096)");
            if (hdrServerData == null) throw new OdinDatabaseValidationException("Cannot be null hdrServerData");
            if (hdrServerData?.Length < 0) throw new OdinDatabaseValidationException($"Too short hdrServerData, was {hdrServerData.Length} (min 0)");
            if (hdrServerData?.Length > 16384) throw new OdinDatabaseValidationException($"Too long hdrServerData, was {hdrServerData.Length} (max 16384)");
            if (hdrTransferHistory?.Length < 0) throw new OdinDatabaseValidationException($"Too short hdrTransferHistory, was {hdrTransferHistory.Length} (min 0)");
            if (hdrTransferHistory?.Length > 16384) throw new OdinDatabaseValidationException($"Too long hdrTransferHistory, was {hdrTransferHistory.Length} (max 16384)");
            if (hdrFileMetaData == null) throw new OdinDatabaseValidationException("Cannot be null hdrFileMetaData");
            if (hdrFileMetaData?.Length < 0) throw new OdinDatabaseValidationException($"Too short hdrFileMetaData, was {hdrFileMetaData.Length} (min 0)");
            if (hdrFileMetaData?.Length > 60000) throw new OdinDatabaseValidationException($"Too long hdrFileMetaData, was {hdrFileMetaData.Length} (max 60000)");
            hdrTmpDriveAlias.AssertGuidNotEmpty("Guid parameter hdrTmpDriveAlias cannot be set to Empty GUID.");
            hdrTmpDriveType.AssertGuidNotEmpty("Guid parameter hdrTmpDriveType cannot be set to Empty GUID.");
        }
    } // End of record DriveMainIndexRecord

    public abstract class TableDriveMainIndexCRUD : TableBase
    {
        private ScopedIdentityConnectionFactory _scopedConnectionFactory { get; init; }
        public override string TableName { get; } = "DriveMainIndex";

        protected TableDriveMainIndexCRUD(ScopedIdentityConnectionFactory scopedConnectionFactory)
        {
            _scopedConnectionFactory = scopedConnectionFactory;
        }


        public override async Task EnsureTableExistsAsync(bool dropExisting = false)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            if (dropExisting)
                await SqlHelper.DeleteTableAsync(cn, "DriveMainIndex");
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE DriveMainIndex IS '{ \"Version\": 202507191211 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS DriveMainIndex( -- { \"Version\": 202507191211 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"driveId BYTEA NOT NULL, "
                   +"fileId BYTEA NOT NULL, "
                   +"globalTransitId BYTEA , "
                   +"fileState BIGINT NOT NULL, "
                   +"requiredSecurityGroup BIGINT NOT NULL, "
                   +"fileSystemType BIGINT NOT NULL, "
                   +"userDate BIGINT NOT NULL, "
                   +"fileType BIGINT NOT NULL, "
                   +"dataType BIGINT NOT NULL, "
                   +"archivalStatus BIGINT NOT NULL, "
                   +"historyStatus BIGINT NOT NULL, "
                   +"senderId TEXT , "
                   +"groupId BYTEA , "
                   +"uniqueId BYTEA , "
                   +"byteCount BIGINT NOT NULL, "
                   +"hdrEncryptedKeyHeader TEXT NOT NULL, "
                   +"hdrVersionTag BYTEA NOT NULL, "
                   +"hdrAppData TEXT NOT NULL, "
                   +"hdrLocalVersionTag BYTEA , "
                   +"hdrLocalAppData TEXT , "
                   +"hdrReactionSummary TEXT , "
                   +"hdrServerData TEXT NOT NULL, "
                   +"hdrTransferHistory TEXT , "
                   +"hdrFileMetaData TEXT NOT NULL, "
                   +"hdrTmpDriveAlias BYTEA NOT NULL, "
                   +"hdrTmpDriveType BYTEA NOT NULL, "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +", UNIQUE(identityId,driveId,fileId)"
                   +", UNIQUE(identityId,driveId,uniqueId)"
                   +", UNIQUE(identityId,driveId,globalTransitId)"
                   +", UNIQUE(identityId,hdrVersionTag)"
                   +$"){wori};"
                   +"CREATE INDEX IF NOT EXISTS Idx0DriveMainIndex ON DriveMainIndex(identityId,driveId,fileSystemType,requiredSecurityGroup,created,rowId);"
                   +"CREATE INDEX IF NOT EXISTS Idx1DriveMainIndex ON DriveMainIndex(identityId,driveId,fileSystemType,requiredSecurityGroup,modified,rowId);"
                   +"CREATE INDEX IF NOT EXISTS Idx2DriveMainIndex ON DriveMainIndex(identityId,driveId,fileSystemType,requiredSecurityGroup,userDate,rowId);"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "DriveMainIndex", createSql, commentSql);
        }

        protected virtual async Task<int> InsertAsync(DriveMainIndexRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr = insertCommand.SqlNow();
                insertCommand.CommandText = "INSERT INTO DriveMainIndex (identityId,driveId,fileId,globalTransitId,fileState,requiredSecurityGroup,fileSystemType,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,uniqueId,byteCount,hdrEncryptedKeyHeader,hdrVersionTag,hdrAppData,hdrLocalVersionTag,hdrLocalAppData,hdrReactionSummary,hdrServerData,hdrTransferHistory,hdrFileMetaData,hdrTmpDriveAlias,hdrTmpDriveType,created,modified) " +
                                           $"VALUES (@identityId,@driveId,@fileId,@globalTransitId,@fileState,@requiredSecurityGroup,@fileSystemType,@userDate,@fileType,@dataType,@archivalStatus,@historyStatus,@senderId,@groupId,@uniqueId,@byteCount,@hdrEncryptedKeyHeader,@hdrVersionTag,@hdrAppData,@hdrLocalVersionTag,@hdrLocalAppData,@hdrReactionSummary,@hdrServerData,@hdrTransferHistory,@hdrFileMetaData,@hdrTmpDriveAlias,@hdrTmpDriveType,{sqlNowStr},{sqlNowStr})"+
                                            "RETURNING created,modified,rowId;";
                insertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                insertCommand.AddParameter("@driveId", DbType.Binary, item.driveId);
                insertCommand.AddParameter("@fileId", DbType.Binary, item.fileId);
                insertCommand.AddParameter("@globalTransitId", DbType.Binary, item.globalTransitId);
                insertCommand.AddParameter("@fileState", DbType.Int32, item.fileState);
                insertCommand.AddParameter("@requiredSecurityGroup", DbType.Int32, item.requiredSecurityGroup);
                insertCommand.AddParameter("@fileSystemType", DbType.Int32, item.fileSystemType);
                insertCommand.AddParameter("@userDate", DbType.Int64, item.userDate.milliseconds);
                insertCommand.AddParameter("@fileType", DbType.Int32, item.fileType);
                insertCommand.AddParameter("@dataType", DbType.Int32, item.dataType);
                insertCommand.AddParameter("@archivalStatus", DbType.Int32, item.archivalStatus);
                insertCommand.AddParameter("@historyStatus", DbType.Int32, item.historyStatus);
                insertCommand.AddParameter("@senderId", DbType.String, item.senderId);
                insertCommand.AddParameter("@groupId", DbType.Binary, item.groupId);
                insertCommand.AddParameter("@uniqueId", DbType.Binary, item.uniqueId);
                insertCommand.AddParameter("@byteCount", DbType.Int64, item.byteCount);
                insertCommand.AddParameter("@hdrEncryptedKeyHeader", DbType.String, item.hdrEncryptedKeyHeader);
                insertCommand.AddParameter("@hdrVersionTag", DbType.Binary, item.hdrVersionTag);
                insertCommand.AddParameter("@hdrAppData", DbType.String, item.hdrAppData);
                insertCommand.AddParameter("@hdrLocalVersionTag", DbType.Binary, item.hdrLocalVersionTag);
                insertCommand.AddParameter("@hdrLocalAppData", DbType.String, item.hdrLocalAppData);
                insertCommand.AddParameter("@hdrReactionSummary", DbType.String, item.hdrReactionSummary);
                insertCommand.AddParameter("@hdrServerData", DbType.String, item.hdrServerData);
                insertCommand.AddParameter("@hdrTransferHistory", DbType.String, item.hdrTransferHistory);
                insertCommand.AddParameter("@hdrFileMetaData", DbType.String, item.hdrFileMetaData);
                insertCommand.AddParameter("@hdrTmpDriveAlias", DbType.Binary, item.hdrTmpDriveAlias);
                insertCommand.AddParameter("@hdrTmpDriveType", DbType.Binary, item.hdrTmpDriveType);
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    item.created = new UnixTimeUtc(created);
                    long modified = (long) rdr[1];
                    item.modified = new UnixTimeUtc((long)modified);
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<bool> TryInsertAsync(DriveMainIndexRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr = insertCommand.SqlNow();
                insertCommand.CommandText = "INSERT INTO DriveMainIndex (identityId,driveId,fileId,globalTransitId,fileState,requiredSecurityGroup,fileSystemType,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,uniqueId,byteCount,hdrEncryptedKeyHeader,hdrVersionTag,hdrAppData,hdrLocalVersionTag,hdrLocalAppData,hdrReactionSummary,hdrServerData,hdrTransferHistory,hdrFileMetaData,hdrTmpDriveAlias,hdrTmpDriveType,created,modified) " +
                                            $"VALUES (@identityId,@driveId,@fileId,@globalTransitId,@fileState,@requiredSecurityGroup,@fileSystemType,@userDate,@fileType,@dataType,@archivalStatus,@historyStatus,@senderId,@groupId,@uniqueId,@byteCount,@hdrEncryptedKeyHeader,@hdrVersionTag,@hdrAppData,@hdrLocalVersionTag,@hdrLocalAppData,@hdrReactionSummary,@hdrServerData,@hdrTransferHistory,@hdrFileMetaData,@hdrTmpDriveAlias,@hdrTmpDriveType,{sqlNowStr},{sqlNowStr}) " +
                                            "ON CONFLICT DO NOTHING "+
                                            "RETURNING created,modified,rowId;";
                insertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                insertCommand.AddParameter("@driveId", DbType.Binary, item.driveId);
                insertCommand.AddParameter("@fileId", DbType.Binary, item.fileId);
                insertCommand.AddParameter("@globalTransitId", DbType.Binary, item.globalTransitId);
                insertCommand.AddParameter("@fileState", DbType.Int32, item.fileState);
                insertCommand.AddParameter("@requiredSecurityGroup", DbType.Int32, item.requiredSecurityGroup);
                insertCommand.AddParameter("@fileSystemType", DbType.Int32, item.fileSystemType);
                insertCommand.AddParameter("@userDate", DbType.Int64, item.userDate.milliseconds);
                insertCommand.AddParameter("@fileType", DbType.Int32, item.fileType);
                insertCommand.AddParameter("@dataType", DbType.Int32, item.dataType);
                insertCommand.AddParameter("@archivalStatus", DbType.Int32, item.archivalStatus);
                insertCommand.AddParameter("@historyStatus", DbType.Int32, item.historyStatus);
                insertCommand.AddParameter("@senderId", DbType.String, item.senderId);
                insertCommand.AddParameter("@groupId", DbType.Binary, item.groupId);
                insertCommand.AddParameter("@uniqueId", DbType.Binary, item.uniqueId);
                insertCommand.AddParameter("@byteCount", DbType.Int64, item.byteCount);
                insertCommand.AddParameter("@hdrEncryptedKeyHeader", DbType.String, item.hdrEncryptedKeyHeader);
                insertCommand.AddParameter("@hdrVersionTag", DbType.Binary, item.hdrVersionTag);
                insertCommand.AddParameter("@hdrAppData", DbType.String, item.hdrAppData);
                insertCommand.AddParameter("@hdrLocalVersionTag", DbType.Binary, item.hdrLocalVersionTag);
                insertCommand.AddParameter("@hdrLocalAppData", DbType.String, item.hdrLocalAppData);
                insertCommand.AddParameter("@hdrReactionSummary", DbType.String, item.hdrReactionSummary);
                insertCommand.AddParameter("@hdrServerData", DbType.String, item.hdrServerData);
                insertCommand.AddParameter("@hdrTransferHistory", DbType.String, item.hdrTransferHistory);
                insertCommand.AddParameter("@hdrFileMetaData", DbType.String, item.hdrFileMetaData);
                insertCommand.AddParameter("@hdrTmpDriveAlias", DbType.Binary, item.hdrTmpDriveAlias);
                insertCommand.AddParameter("@hdrTmpDriveType", DbType.Binary, item.hdrTmpDriveType);
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    item.created = new UnixTimeUtc(created);
                    long modified = (long) rdr[1];
                    item.modified = new UnixTimeUtc((long)modified);
                    item.rowId = (long) rdr[2];
                    return true;
                }
                return false;
            }
        }

        protected virtual async Task<int> UpsertAsync(DriveMainIndexRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                string sqlNowStr = upsertCommand.SqlNow();
                upsertCommand.CommandText = "INSERT INTO DriveMainIndex (identityId,driveId,fileId,globalTransitId,fileState,requiredSecurityGroup,fileSystemType,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,uniqueId,byteCount,hdrEncryptedKeyHeader,hdrVersionTag,hdrAppData,hdrLocalVersionTag,hdrLocalAppData,hdrReactionSummary,hdrServerData,hdrTransferHistory,hdrFileMetaData,hdrTmpDriveAlias,hdrTmpDriveType,created,modified) " +
                                            $"VALUES (@identityId,@driveId,@fileId,@globalTransitId,@fileState,@requiredSecurityGroup,@fileSystemType,@userDate,@fileType,@dataType,@archivalStatus,@historyStatus,@senderId,@groupId,@uniqueId,@byteCount,@hdrEncryptedKeyHeader,@hdrVersionTag,@hdrAppData,@hdrLocalVersionTag,@hdrLocalAppData,@hdrReactionSummary,@hdrServerData,@hdrTransferHistory,@hdrFileMetaData,@hdrTmpDriveAlias,@hdrTmpDriveType,{sqlNowStr},{sqlNowStr})"+
                                            "ON CONFLICT (identityId,driveId,fileId) DO UPDATE "+
                                            $"SET globalTransitId = @globalTransitId,fileState = @fileState,requiredSecurityGroup = @requiredSecurityGroup,fileSystemType = @fileSystemType,userDate = @userDate,fileType = @fileType,dataType = @dataType,archivalStatus = @archivalStatus,historyStatus = @historyStatus,senderId = @senderId,groupId = @groupId,uniqueId = @uniqueId,byteCount = @byteCount,hdrEncryptedKeyHeader = @hdrEncryptedKeyHeader,hdrVersionTag = @hdrVersionTag,hdrAppData = @hdrAppData,hdrLocalVersionTag = @hdrLocalVersionTag,hdrLocalAppData = @hdrLocalAppData,hdrReactionSummary = @hdrReactionSummary,hdrServerData = @hdrServerData,hdrTransferHistory = @hdrTransferHistory,hdrFileMetaData = @hdrFileMetaData,hdrTmpDriveAlias = @hdrTmpDriveAlias,hdrTmpDriveType = @hdrTmpDriveType,modified = {upsertCommand.SqlMax()}(DriveMainIndex.modified+1,{sqlNowStr}) "+
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
                upsertCommand.AddParameter("@hdrLocalVersionTag", DbType.Binary, item.hdrLocalVersionTag);
                upsertCommand.AddParameter("@hdrLocalAppData", DbType.String, item.hdrLocalAppData);
                upsertCommand.AddParameter("@hdrReactionSummary", DbType.String, item.hdrReactionSummary);
                upsertCommand.AddParameter("@hdrServerData", DbType.String, item.hdrServerData);
                upsertCommand.AddParameter("@hdrTransferHistory", DbType.String, item.hdrTransferHistory);
                upsertCommand.AddParameter("@hdrFileMetaData", DbType.String, item.hdrFileMetaData);
                upsertCommand.AddParameter("@hdrTmpDriveAlias", DbType.Binary, item.hdrTmpDriveAlias);
                upsertCommand.AddParameter("@hdrTmpDriveType", DbType.Binary, item.hdrTmpDriveType);
                await using var rdr = await upsertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    item.created = new UnixTimeUtc(created);
                    long modified = (long) rdr[1];
                    item.modified = new UnixTimeUtc((long)modified);
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<int> UpdateAsync(DriveMainIndexRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                string sqlNowStr = updateCommand.SqlNow();
                updateCommand.CommandText = "UPDATE DriveMainIndex " +
                                            $"SET globalTransitId = @globalTransitId,fileState = @fileState,requiredSecurityGroup = @requiredSecurityGroup,fileSystemType = @fileSystemType,userDate = @userDate,fileType = @fileType,dataType = @dataType,archivalStatus = @archivalStatus,historyStatus = @historyStatus,senderId = @senderId,groupId = @groupId,uniqueId = @uniqueId,byteCount = @byteCount,hdrEncryptedKeyHeader = @hdrEncryptedKeyHeader,hdrVersionTag = @hdrVersionTag,hdrAppData = @hdrAppData,hdrLocalVersionTag = @hdrLocalVersionTag,hdrLocalAppData = @hdrLocalAppData,hdrReactionSummary = @hdrReactionSummary,hdrServerData = @hdrServerData,hdrTransferHistory = @hdrTransferHistory,hdrFileMetaData = @hdrFileMetaData,hdrTmpDriveAlias = @hdrTmpDriveAlias,hdrTmpDriveType = @hdrTmpDriveType,modified = {updateCommand.SqlMax()}(DriveMainIndex.modified+1,{sqlNowStr}) "+
                                            "WHERE (identityId = @identityId AND driveId = @driveId AND fileId = @fileId) "+
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
                updateCommand.AddParameter("@hdrLocalVersionTag", DbType.Binary, item.hdrLocalVersionTag);
                updateCommand.AddParameter("@hdrLocalAppData", DbType.String, item.hdrLocalAppData);
                updateCommand.AddParameter("@hdrReactionSummary", DbType.String, item.hdrReactionSummary);
                updateCommand.AddParameter("@hdrServerData", DbType.String, item.hdrServerData);
                updateCommand.AddParameter("@hdrTransferHistory", DbType.String, item.hdrTransferHistory);
                updateCommand.AddParameter("@hdrFileMetaData", DbType.String, item.hdrFileMetaData);
                updateCommand.AddParameter("@hdrTmpDriveAlias", DbType.Binary, item.hdrTmpDriveAlias);
                updateCommand.AddParameter("@hdrTmpDriveType", DbType.Binary, item.hdrTmpDriveType);
                await using var rdr = await updateCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    item.created = new UnixTimeUtc(created);
                    long modified = (long) rdr[1];
                    item.modified = new UnixTimeUtc((long)modified);
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        protected new async Task<int> GetCountAsync()
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getCountCommand = cn.CreateCommand();
            {
                 // TODO: this is SQLite specific
                getCountCommand.CommandText = "SELECT COUNT(*) FROM DriveMainIndex;";
                var count = await getCountCommand.ExecuteScalarAsync();
                if (count == null || count == DBNull.Value || !(count is int || count is long))
                    return -1;
                else
                    return Convert.ToInt32(count);
            }
        }

        public new static List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("rowId");
            sl.Add("identityId");
            sl.Add("driveId");
            sl.Add("fileId");
            sl.Add("globalTransitId");
            sl.Add("fileState");
            sl.Add("requiredSecurityGroup");
            sl.Add("fileSystemType");
            sl.Add("userDate");
            sl.Add("fileType");
            sl.Add("dataType");
            sl.Add("archivalStatus");
            sl.Add("historyStatus");
            sl.Add("senderId");
            sl.Add("groupId");
            sl.Add("uniqueId");
            sl.Add("byteCount");
            sl.Add("hdrEncryptedKeyHeader");
            sl.Add("hdrVersionTag");
            sl.Add("hdrAppData");
            sl.Add("hdrLocalVersionTag");
            sl.Add("hdrLocalAppData");
            sl.Add("hdrReactionSummary");
            sl.Add("hdrServerData");
            sl.Add("hdrTransferHistory");
            sl.Add("hdrFileMetaData");
            sl.Add("hdrTmpDriveAlias");
            sl.Add("hdrTmpDriveType");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        protected virtual async Task<int> GetDriveCountAsync(Guid driveId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getCountDriveCommand = cn.CreateCommand();
            {
                 // TODO: this is SQLite specific
                getCountDriveCommand.CommandText = "SELECT COUNT(*) FROM DriveMainIndex WHERE driveId = $driveId;";
                getCountDriveCommand.AddParameter("$driveId", DbType.Binary, driveId);
                var count = await getCountDriveCommand.ExecuteScalarAsync();
                if (count == null || count == DBNull.Value || !(count is int || count is long))
                    return -1;
                else
                    return Convert.ToInt32(count);
            } // using
        }

        // SELECT rowId,identityId,driveId,fileId,globalTransitId,fileState,requiredSecurityGroup,fileSystemType,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,uniqueId,byteCount,hdrEncryptedKeyHeader,hdrVersionTag,hdrAppData,hdrLocalVersionTag,hdrLocalAppData,hdrReactionSummary,hdrServerData,hdrTransferHistory,hdrFileMetaData,hdrTmpDriveAlias,hdrTmpDriveType,created,modified
        protected DriveMainIndexRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<DriveMainIndexRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new DriveMainIndexRecord();
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.identityId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.driveId = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[2]);
            item.fileId = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[3]);
            item.globalTransitId = (rdr[4] == DBNull.Value) ? null : new Guid((byte[])rdr[4]);
            item.fileState = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[5];
            item.requiredSecurityGroup = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[6];
            item.fileSystemType = (rdr[7] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[7];
            item.userDate = (rdr[8] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[8]);
            item.fileType = (rdr[9] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[9];
            item.dataType = (rdr[10] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[10];
            item.archivalStatus = (rdr[11] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[11];
            item.historyStatus = (rdr[12] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[12];
            item.senderId = (rdr[13] == DBNull.Value) ? null : (string)rdr[13];
            item.groupId = (rdr[14] == DBNull.Value) ? null : new Guid((byte[])rdr[14]);
            item.uniqueId = (rdr[15] == DBNull.Value) ? null : new Guid((byte[])rdr[15]);
            item.byteCount = (rdr[16] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[16];
            item.hdrEncryptedKeyHeader = (rdr[17] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[17];
            item.hdrVersionTag = (rdr[18] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[18]);
            item.hdrAppData = (rdr[19] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[19];
            item.hdrLocalVersionTag = (rdr[20] == DBNull.Value) ? null : new Guid((byte[])rdr[20]);
            item.hdrLocalAppData = (rdr[21] == DBNull.Value) ? null : (string)rdr[21];
            item.hdrReactionSummary = (rdr[22] == DBNull.Value) ? null : (string)rdr[22];
            item.hdrServerData = (rdr[23] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[23];
            item.hdrTransferHistory = (rdr[24] == DBNull.Value) ? null : (string)rdr[24];
            item.hdrFileMetaData = (rdr[25] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[25];
            item.hdrTmpDriveAlias = (rdr[26] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[26]);
            item.hdrTmpDriveType = (rdr[27] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[27]);
            item.created = (rdr[28] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[28]);
            item.modified = (rdr[29] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[29]);
            return item;
       }

        protected virtual async Task<int> DeleteAsync(Guid identityId,Guid driveId,Guid fileId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM DriveMainIndex " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId";

                delete0Command.AddParameter("@identityId", DbType.Binary, identityId);
                delete0Command.AddParameter("@driveId", DbType.Binary, driveId);
                delete0Command.AddParameter("@fileId", DbType.Binary, fileId);
                var count = await delete0Command.ExecuteNonQueryAsync();
                return count;
            }
        }

        protected virtual async Task<DriveMainIndexRecord> PopAsync(Guid identityId,Guid driveId,Guid fileId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var deleteCommand = cn.CreateCommand();
            {
                deleteCommand.CommandText = "DELETE FROM DriveMainIndex " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId " + 
                                             "RETURNING rowId,globalTransitId,fileState,requiredSecurityGroup,fileSystemType,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,uniqueId,byteCount,hdrEncryptedKeyHeader,hdrVersionTag,hdrAppData,hdrLocalVersionTag,hdrLocalAppData,hdrReactionSummary,hdrServerData,hdrTransferHistory,hdrFileMetaData,hdrTmpDriveAlias,hdrTmpDriveType,created,modified";

                deleteCommand.AddParameter("@identityId", DbType.Binary, identityId);
                deleteCommand.AddParameter("@driveId", DbType.Binary, driveId);
                deleteCommand.AddParameter("@fileId", DbType.Binary, fileId);
                using (var rdr = await deleteCommand.ExecuteReaderAsync(CommandBehavior.SingleRow))
                {
                    if (await rdr.ReadAsync())
                    {
                       return ReadRecordFromReader0(rdr,identityId,driveId,fileId);
                    }
                    else
                    {
                       return null;
                    }
                }
            }
        }

        protected DriveMainIndexRecord ReadRecordFromReader0(DbDataReader rdr,Guid identityId,Guid driveId,Guid fileId)
        {
            var result = new List<DriveMainIndexRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new DriveMainIndexRecord();
            item.identityId = identityId;
            item.driveId = driveId;
            item.fileId = fileId;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.globalTransitId = (rdr[1] == DBNull.Value) ? null : new Guid((byte[])rdr[1]);
            item.fileState = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[2];
            item.requiredSecurityGroup = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[3];
            item.fileSystemType = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[4];
            item.userDate = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[5]);
            item.fileType = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[6];
            item.dataType = (rdr[7] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[7];
            item.archivalStatus = (rdr[8] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[8];
            item.historyStatus = (rdr[9] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[9];
            item.senderId = (rdr[10] == DBNull.Value) ? null : (string)rdr[10];
            item.groupId = (rdr[11] == DBNull.Value) ? null : new Guid((byte[])rdr[11]);
            item.uniqueId = (rdr[12] == DBNull.Value) ? null : new Guid((byte[])rdr[12]);
            item.byteCount = (rdr[13] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[13];
            item.hdrEncryptedKeyHeader = (rdr[14] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[14];
            item.hdrVersionTag = (rdr[15] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[15]);
            item.hdrAppData = (rdr[16] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[16];
            item.hdrLocalVersionTag = (rdr[17] == DBNull.Value) ? null : new Guid((byte[])rdr[17]);
            item.hdrLocalAppData = (rdr[18] == DBNull.Value) ? null : (string)rdr[18];
            item.hdrReactionSummary = (rdr[19] == DBNull.Value) ? null : (string)rdr[19];
            item.hdrServerData = (rdr[20] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[20];
            item.hdrTransferHistory = (rdr[21] == DBNull.Value) ? null : (string)rdr[21];
            item.hdrFileMetaData = (rdr[22] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[22];
            item.hdrTmpDriveAlias = (rdr[23] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[23]);
            item.hdrTmpDriveType = (rdr[24] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[24]);
            item.created = (rdr[25] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[25]);
            item.modified = (rdr[26] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[26]);
            return item;
       }

        protected virtual async Task<DriveMainIndexRecord> GetAsync(Guid identityId,Guid driveId,Guid fileId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT rowId,globalTransitId,fileState,requiredSecurityGroup,fileSystemType,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,uniqueId,byteCount,hdrEncryptedKeyHeader,hdrVersionTag,hdrAppData,hdrLocalVersionTag,hdrLocalAppData,hdrReactionSummary,hdrServerData,hdrTransferHistory,hdrFileMetaData,hdrTmpDriveAlias,hdrTmpDriveType,created,modified FROM DriveMainIndex " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId LIMIT 1;"+
                                             ";";

                get0Command.AddParameter("@identityId", DbType.Binary, identityId);
                get0Command.AddParameter("@driveId", DbType.Binary, driveId);
                get0Command.AddParameter("@fileId", DbType.Binary, fileId);
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr,identityId,driveId,fileId);
                        return r;
                    } // using
                } //
            } // using
        }

        protected DriveMainIndexRecord ReadRecordFromReader1(DbDataReader rdr,Guid identityId,Guid driveId)
        {
            var result = new List<DriveMainIndexRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new DriveMainIndexRecord();
            item.identityId = identityId;
            item.driveId = driveId;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.fileId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.globalTransitId = (rdr[2] == DBNull.Value) ? null : new Guid((byte[])rdr[2]);
            item.fileState = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[3];
            item.requiredSecurityGroup = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[4];
            item.fileSystemType = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[5];
            item.userDate = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[6]);
            item.fileType = (rdr[7] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[7];
            item.dataType = (rdr[8] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[8];
            item.archivalStatus = (rdr[9] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[9];
            item.historyStatus = (rdr[10] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[10];
            item.senderId = (rdr[11] == DBNull.Value) ? null : (string)rdr[11];
            item.groupId = (rdr[12] == DBNull.Value) ? null : new Guid((byte[])rdr[12]);
            item.uniqueId = (rdr[13] == DBNull.Value) ? null : new Guid((byte[])rdr[13]);
            item.byteCount = (rdr[14] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[14];
            item.hdrEncryptedKeyHeader = (rdr[15] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[15];
            item.hdrVersionTag = (rdr[16] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[16]);
            item.hdrAppData = (rdr[17] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[17];
            item.hdrLocalVersionTag = (rdr[18] == DBNull.Value) ? null : new Guid((byte[])rdr[18]);
            item.hdrLocalAppData = (rdr[19] == DBNull.Value) ? null : (string)rdr[19];
            item.hdrReactionSummary = (rdr[20] == DBNull.Value) ? null : (string)rdr[20];
            item.hdrServerData = (rdr[21] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[21];
            item.hdrTransferHistory = (rdr[22] == DBNull.Value) ? null : (string)rdr[22];
            item.hdrFileMetaData = (rdr[23] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[23];
            item.hdrTmpDriveAlias = (rdr[24] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[24]);
            item.hdrTmpDriveType = (rdr[25] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[25]);
            item.created = (rdr[26] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[26]);
            item.modified = (rdr[27] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[27]);
            return item;
       }

        protected virtual async Task<List<DriveMainIndexRecord>> GetAllByDriveIdAsync(Guid identityId,Guid driveId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get1Command = cn.CreateCommand();
            {
                get1Command.CommandText = "SELECT rowId,fileId,globalTransitId,fileState,requiredSecurityGroup,fileSystemType,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,uniqueId,byteCount,hdrEncryptedKeyHeader,hdrVersionTag,hdrAppData,hdrLocalVersionTag,hdrLocalAppData,hdrReactionSummary,hdrServerData,hdrTransferHistory,hdrFileMetaData,hdrTmpDriveAlias,hdrTmpDriveType,created,modified FROM DriveMainIndex " +
                                             "WHERE identityId = @identityId AND driveId = @driveId;"+
                                             ";";

                get1Command.AddParameter("@identityId", DbType.Binary, identityId);
                get1Command.AddParameter("@driveId", DbType.Binary, driveId);
                {
                    using (var rdr = await get1Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return new List<DriveMainIndexRecord>();
                        }
                        var result = new List<DriveMainIndexRecord>();
                        while (true)
                        {
                            result.Add(ReadRecordFromReader1(rdr,identityId,driveId));
                            if (!await rdr.ReadAsync())
                                break;
                        }
                        return result;
                    } // using
                } //
            } // using
        }

        protected DriveMainIndexRecord ReadRecordFromReader2(DbDataReader rdr,Guid identityId,Guid driveId,Guid? uniqueId)
        {
            var result = new List<DriveMainIndexRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new DriveMainIndexRecord();
            item.identityId = identityId;
            item.driveId = driveId;
            item.uniqueId = uniqueId;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.fileId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.globalTransitId = (rdr[2] == DBNull.Value) ? null : new Guid((byte[])rdr[2]);
            item.fileState = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[3];
            item.requiredSecurityGroup = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[4];
            item.fileSystemType = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[5];
            item.userDate = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[6]);
            item.fileType = (rdr[7] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[7];
            item.dataType = (rdr[8] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[8];
            item.archivalStatus = (rdr[9] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[9];
            item.historyStatus = (rdr[10] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[10];
            item.senderId = (rdr[11] == DBNull.Value) ? null : (string)rdr[11];
            item.groupId = (rdr[12] == DBNull.Value) ? null : new Guid((byte[])rdr[12]);
            item.byteCount = (rdr[13] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[13];
            item.hdrEncryptedKeyHeader = (rdr[14] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[14];
            item.hdrVersionTag = (rdr[15] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[15]);
            item.hdrAppData = (rdr[16] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[16];
            item.hdrLocalVersionTag = (rdr[17] == DBNull.Value) ? null : new Guid((byte[])rdr[17]);
            item.hdrLocalAppData = (rdr[18] == DBNull.Value) ? null : (string)rdr[18];
            item.hdrReactionSummary = (rdr[19] == DBNull.Value) ? null : (string)rdr[19];
            item.hdrServerData = (rdr[20] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[20];
            item.hdrTransferHistory = (rdr[21] == DBNull.Value) ? null : (string)rdr[21];
            item.hdrFileMetaData = (rdr[22] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[22];
            item.hdrTmpDriveAlias = (rdr[23] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[23]);
            item.hdrTmpDriveType = (rdr[24] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[24]);
            item.created = (rdr[25] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[25]);
            item.modified = (rdr[26] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[26]);
            return item;
       }

        protected virtual async Task<DriveMainIndexRecord> GetByUniqueIdAsync(Guid identityId,Guid driveId,Guid? uniqueId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get2Command = cn.CreateCommand();
            {
                get2Command.CommandText = "SELECT rowId,fileId,globalTransitId,fileState,requiredSecurityGroup,fileSystemType,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,byteCount,hdrEncryptedKeyHeader,hdrVersionTag,hdrAppData,hdrLocalVersionTag,hdrLocalAppData,hdrReactionSummary,hdrServerData,hdrTransferHistory,hdrFileMetaData,hdrTmpDriveAlias,hdrTmpDriveType,created,modified FROM DriveMainIndex " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND uniqueId = @uniqueId LIMIT 1;"+
                                             ";";

                get2Command.AddParameter("@identityId", DbType.Binary, identityId);
                get2Command.AddParameter("@driveId", DbType.Binary, driveId);
                get2Command.AddParameter("@uniqueId", DbType.Binary, uniqueId);
                {
                    using (var rdr = await get2Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader2(rdr,identityId,driveId,uniqueId);
                        return r;
                    } // using
                } //
            } // using
        }

        protected DriveMainIndexRecord ReadRecordFromReader3(DbDataReader rdr,Guid identityId,Guid driveId,Guid? globalTransitId)
        {
            var result = new List<DriveMainIndexRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new DriveMainIndexRecord();
            item.identityId = identityId;
            item.driveId = driveId;
            item.globalTransitId = globalTransitId;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.fileId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.fileState = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[2];
            item.requiredSecurityGroup = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[3];
            item.fileSystemType = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[4];
            item.userDate = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[5]);
            item.fileType = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[6];
            item.dataType = (rdr[7] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[7];
            item.archivalStatus = (rdr[8] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[8];
            item.historyStatus = (rdr[9] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[9];
            item.senderId = (rdr[10] == DBNull.Value) ? null : (string)rdr[10];
            item.groupId = (rdr[11] == DBNull.Value) ? null : new Guid((byte[])rdr[11]);
            item.uniqueId = (rdr[12] == DBNull.Value) ? null : new Guid((byte[])rdr[12]);
            item.byteCount = (rdr[13] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[13];
            item.hdrEncryptedKeyHeader = (rdr[14] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[14];
            item.hdrVersionTag = (rdr[15] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[15]);
            item.hdrAppData = (rdr[16] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[16];
            item.hdrLocalVersionTag = (rdr[17] == DBNull.Value) ? null : new Guid((byte[])rdr[17]);
            item.hdrLocalAppData = (rdr[18] == DBNull.Value) ? null : (string)rdr[18];
            item.hdrReactionSummary = (rdr[19] == DBNull.Value) ? null : (string)rdr[19];
            item.hdrServerData = (rdr[20] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[20];
            item.hdrTransferHistory = (rdr[21] == DBNull.Value) ? null : (string)rdr[21];
            item.hdrFileMetaData = (rdr[22] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[22];
            item.hdrTmpDriveAlias = (rdr[23] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[23]);
            item.hdrTmpDriveType = (rdr[24] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[24]);
            item.created = (rdr[25] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[25]);
            item.modified = (rdr[26] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[26]);
            return item;
       }

        protected virtual async Task<DriveMainIndexRecord> GetByGlobalTransitIdAsync(Guid identityId,Guid driveId,Guid? globalTransitId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get3Command = cn.CreateCommand();
            {
                get3Command.CommandText = "SELECT rowId,fileId,fileState,requiredSecurityGroup,fileSystemType,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,uniqueId,byteCount,hdrEncryptedKeyHeader,hdrVersionTag,hdrAppData,hdrLocalVersionTag,hdrLocalAppData,hdrReactionSummary,hdrServerData,hdrTransferHistory,hdrFileMetaData,hdrTmpDriveAlias,hdrTmpDriveType,created,modified FROM DriveMainIndex " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND globalTransitId = @globalTransitId LIMIT 1;"+
                                             ";";

                get3Command.AddParameter("@identityId", DbType.Binary, identityId);
                get3Command.AddParameter("@driveId", DbType.Binary, driveId);
                get3Command.AddParameter("@globalTransitId", DbType.Binary, globalTransitId);
                {
                    using (var rdr = await get3Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader3(rdr,identityId,driveId,globalTransitId);
                        return r;
                    } // using
                } //
            } // using
        }

        protected DriveMainIndexRecord ReadRecordFromReader4(DbDataReader rdr,Guid identityId,Guid driveId)
        {
            var result = new List<DriveMainIndexRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new DriveMainIndexRecord();
            item.identityId = identityId;
            item.driveId = driveId;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.fileId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.globalTransitId = (rdr[2] == DBNull.Value) ? null : new Guid((byte[])rdr[2]);
            item.fileState = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[3];
            item.requiredSecurityGroup = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[4];
            item.fileSystemType = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[5];
            item.userDate = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[6]);
            item.fileType = (rdr[7] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[7];
            item.dataType = (rdr[8] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[8];
            item.archivalStatus = (rdr[9] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[9];
            item.historyStatus = (rdr[10] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[10];
            item.senderId = (rdr[11] == DBNull.Value) ? null : (string)rdr[11];
            item.groupId = (rdr[12] == DBNull.Value) ? null : new Guid((byte[])rdr[12]);
            item.uniqueId = (rdr[13] == DBNull.Value) ? null : new Guid((byte[])rdr[13]);
            item.byteCount = (rdr[14] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[14];
            item.hdrEncryptedKeyHeader = (rdr[15] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[15];
            item.hdrVersionTag = (rdr[16] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[16]);
            item.hdrAppData = (rdr[17] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[17];
            item.hdrLocalVersionTag = (rdr[18] == DBNull.Value) ? null : new Guid((byte[])rdr[18]);
            item.hdrLocalAppData = (rdr[19] == DBNull.Value) ? null : (string)rdr[19];
            item.hdrReactionSummary = (rdr[20] == DBNull.Value) ? null : (string)rdr[20];
            item.hdrServerData = (rdr[21] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[21];
            item.hdrTransferHistory = (rdr[22] == DBNull.Value) ? null : (string)rdr[22];
            item.hdrFileMetaData = (rdr[23] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[23];
            item.hdrTmpDriveAlias = (rdr[24] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[24]);
            item.hdrTmpDriveType = (rdr[25] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[25]);
            item.created = (rdr[26] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[26]);
            item.modified = (rdr[27] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[27]);
            return item;
       }

        protected virtual async Task<DriveMainIndexRecord> GetFullRecordAsync(Guid identityId,Guid driveId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get4Command = cn.CreateCommand();
            {
                get4Command.CommandText = "SELECT rowId,fileId,globalTransitId,fileState,requiredSecurityGroup,fileSystemType,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,uniqueId,byteCount,hdrEncryptedKeyHeader,hdrVersionTag,hdrAppData,hdrLocalVersionTag,hdrLocalAppData,hdrReactionSummary,hdrServerData,hdrTransferHistory,hdrFileMetaData,hdrTmpDriveAlias,hdrTmpDriveType,created,modified FROM DriveMainIndex " +
                                             "WHERE identityId = @identityId AND driveId = @driveId LIMIT 1;"+
                                             ";";

                get4Command.AddParameter("@identityId", DbType.Binary, identityId);
                get4Command.AddParameter("@driveId", DbType.Binary, driveId);
                {
                    using (var rdr = await get4Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader4(rdr,identityId,driveId);
                        return r;
                    } // using
                } //
            } // using
        }

        protected virtual async Task<(List<DriveMainIndexRecord>, Int64? nextCursor)> PagingByRowIdAsync(int count, Int64? inCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");
            if (count == int.MaxValue)
                count--; // avoid overflow when doing +1 on the param below
            if (inCursor == null)
                inCursor = 0;

            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getPaging0Command = cn.CreateCommand();
            {
                getPaging0Command.CommandText = "SELECT rowId,identityId,driveId,fileId,globalTransitId,fileState,requiredSecurityGroup,fileSystemType,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,uniqueId,byteCount,hdrEncryptedKeyHeader,hdrVersionTag,hdrAppData,hdrLocalVersionTag,hdrLocalAppData,hdrReactionSummary,hdrServerData,hdrTransferHistory,hdrFileMetaData,hdrTmpDriveAlias,hdrTmpDriveType,created,modified FROM DriveMainIndex " +
                                            "WHERE rowId > @rowId  ORDER BY rowId ASC  LIMIT @count;";

                getPaging0Command.AddParameter("@rowId", DbType.Int64, inCursor);
                getPaging0Command.AddParameter("@count", DbType.Int64, count+1);

                {
                    await using (var rdr = await getPaging0Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        var result = new List<DriveMainIndexRecord>();
                        Int64? nextCursor;
                        int n = 0;
                        while ((n < count) && await rdr.ReadAsync())
                        {
                            n++;
                            result.Add(ReadRecordFromReaderAll(rdr));
                        } // while
                        if ((n > 0) && await rdr.ReadAsync())
                        {
                                nextCursor = result[n - 1].rowId;
                        }
                        else
                        {
                            nextCursor = null;
                        }
                        return (result, nextCursor);
                    } // using
                } //
            } // using 
        } // PagingGet

    }
}
