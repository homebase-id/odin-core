using System;
using System.Data;
using System.Data.Common;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Odin.Core.Time;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.System.Connection;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Factory;
using Odin.Core.Util;
using Odin.Core.Storage.Exceptions;
using Odin.Core.Storage.SQLite;

// THIS FILE IS AUTO GENERATED - DO NOT EDIT

namespace Odin.Core.Storage.Database.Identity.Table
{
    public record DriveMainIndexRecord
    {
        private Int64 _rowId;
        public Int64 rowId
        {
           get {
                   return _rowId;
               }
           set {
                  _rowId = value;
               }
        }
        private Guid _identityId;
        public Guid identityId
        {
           get {
                   return _identityId;
               }
           set {
                  _identityId = value;
               }
        }
        private Guid _driveId;
        public Guid driveId
        {
           get {
                   return _driveId;
               }
           set {
                  _driveId = value;
               }
        }
        private Guid _fileId;
        public Guid fileId
        {
           get {
                   return _fileId;
               }
           set {
                  _fileId = value;
               }
        }
        private Guid? _globalTransitId;
        public Guid? globalTransitId
        {
           get {
                   return _globalTransitId;
               }
           set {
                  _globalTransitId = value;
               }
        }
        private Int32 _fileState;
        public Int32 fileState
        {
           get {
                   return _fileState;
               }
           set {
                  _fileState = value;
               }
        }
        private Int32 _requiredSecurityGroup;
        public Int32 requiredSecurityGroup
        {
           get {
                   return _requiredSecurityGroup;
               }
           set {
                  _requiredSecurityGroup = value;
               }
        }
        private Int32 _fileSystemType;
        public Int32 fileSystemType
        {
           get {
                   return _fileSystemType;
               }
           set {
                  _fileSystemType = value;
               }
        }
        private UnixTimeUtc _userDate;
        public UnixTimeUtc userDate
        {
           get {
                   return _userDate;
               }
           set {
                  _userDate = value;
               }
        }
        private Int32 _fileType;
        public Int32 fileType
        {
           get {
                   return _fileType;
               }
           set {
                  _fileType = value;
               }
        }
        private Int32 _dataType;
        public Int32 dataType
        {
           get {
                   return _dataType;
               }
           set {
                  _dataType = value;
               }
        }
        private Int32 _archivalStatus;
        public Int32 archivalStatus
        {
           get {
                   return _archivalStatus;
               }
           set {
                  _archivalStatus = value;
               }
        }
        private Int32 _historyStatus;
        public Int32 historyStatus
        {
           get {
                   return _historyStatus;
               }
           set {
                  _historyStatus = value;
               }
        }
        private string _senderId;
        public string senderId
        {
           get {
                   return _senderId;
               }
           set {
                    if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short senderId, was {value.Length} (min 0)");
                    if (value?.Length > 256) throw new OdinDatabaseValidationException($"Too long senderId, was {value.Length} (max 256)");
                  _senderId = value;
               }
        }
        internal string senderIdNoLengthCheck
        {
           get {
                   return _senderId;
               }
           set {
                    if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short senderId, was {value.Length} (min 0)");
                  _senderId = value;
               }
        }
        private Guid? _groupId;
        public Guid? groupId
        {
           get {
                   return _groupId;
               }
           set {
                  _groupId = value;
               }
        }
        private Guid? _uniqueId;
        public Guid? uniqueId
        {
           get {
                   return _uniqueId;
               }
           set {
                  _uniqueId = value;
               }
        }
        private Int64 _byteCount;
        public Int64 byteCount
        {
           get {
                   return _byteCount;
               }
           set {
                  _byteCount = value;
               }
        }
        private string _hdrEncryptedKeyHeader;
        public string hdrEncryptedKeyHeader
        {
           get {
                   return _hdrEncryptedKeyHeader;
               }
           set {
                    if (value == null) throw new OdinDatabaseValidationException("Cannot be null hdrEncryptedKeyHeader");
                    if (value?.Length < 16) throw new OdinDatabaseValidationException($"Too short hdrEncryptedKeyHeader, was {value.Length} (min 16)");
                    if (value?.Length > 512) throw new OdinDatabaseValidationException($"Too long hdrEncryptedKeyHeader, was {value.Length} (max 512)");
                  _hdrEncryptedKeyHeader = value;
               }
        }
        internal string hdrEncryptedKeyHeaderNoLengthCheck
        {
           get {
                   return _hdrEncryptedKeyHeader;
               }
           set {
                    if (value == null) throw new OdinDatabaseValidationException("Cannot be null hdrEncryptedKeyHeader");
                    if (value?.Length < 16) throw new OdinDatabaseValidationException($"Too short hdrEncryptedKeyHeader, was {value.Length} (min 16)");
                  _hdrEncryptedKeyHeader = value;
               }
        }
        private Guid _hdrVersionTag;
        public Guid hdrVersionTag
        {
           get {
                   return _hdrVersionTag;
               }
           set {
                  _hdrVersionTag = value;
               }
        }
        private string _hdrAppData;
        public string hdrAppData
        {
           get {
                   return _hdrAppData;
               }
           set {
                    if (value == null) throw new OdinDatabaseValidationException("Cannot be null hdrAppData");
                    if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short hdrAppData, was {value.Length} (min 0)");
                    if (value?.Length > 21504) throw new OdinDatabaseValidationException($"Too long hdrAppData, was {value.Length} (max 21504)");
                  _hdrAppData = value;
               }
        }
        internal string hdrAppDataNoLengthCheck
        {
           get {
                   return _hdrAppData;
               }
           set {
                    if (value == null) throw new OdinDatabaseValidationException("Cannot be null hdrAppData");
                    if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short hdrAppData, was {value.Length} (min 0)");
                  _hdrAppData = value;
               }
        }
        private Guid? _hdrLocalVersionTag;
        public Guid? hdrLocalVersionTag
        {
           get {
                   return _hdrLocalVersionTag;
               }
           set {
                  _hdrLocalVersionTag = value;
               }
        }
        private string _hdrLocalAppData;
        public string hdrLocalAppData
        {
           get {
                   return _hdrLocalAppData;
               }
           set {
                    if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short hdrLocalAppData, was {value.Length} (min 0)");
                    if (value?.Length > 4096) throw new OdinDatabaseValidationException($"Too long hdrLocalAppData, was {value.Length} (max 4096)");
                  _hdrLocalAppData = value;
               }
        }
        internal string hdrLocalAppDataNoLengthCheck
        {
           get {
                   return _hdrLocalAppData;
               }
           set {
                    if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short hdrLocalAppData, was {value.Length} (min 0)");
                  _hdrLocalAppData = value;
               }
        }
        private string _hdrReactionSummary;
        public string hdrReactionSummary
        {
           get {
                   return _hdrReactionSummary;
               }
           set {
                    if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short hdrReactionSummary, was {value.Length} (min 0)");
                    if (value?.Length > 4096) throw new OdinDatabaseValidationException($"Too long hdrReactionSummary, was {value.Length} (max 4096)");
                  _hdrReactionSummary = value;
               }
        }
        internal string hdrReactionSummaryNoLengthCheck
        {
           get {
                   return _hdrReactionSummary;
               }
           set {
                    if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short hdrReactionSummary, was {value.Length} (min 0)");
                  _hdrReactionSummary = value;
               }
        }
        private string _hdrServerData;
        public string hdrServerData
        {
           get {
                   return _hdrServerData;
               }
           set {
                    if (value == null) throw new OdinDatabaseValidationException("Cannot be null hdrServerData");
                    if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short hdrServerData, was {value.Length} (min 0)");
                    if (value?.Length > 16384) throw new OdinDatabaseValidationException($"Too long hdrServerData, was {value.Length} (max 16384)");
                  _hdrServerData = value;
               }
        }
        internal string hdrServerDataNoLengthCheck
        {
           get {
                   return _hdrServerData;
               }
           set {
                    if (value == null) throw new OdinDatabaseValidationException("Cannot be null hdrServerData");
                    if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short hdrServerData, was {value.Length} (min 0)");
                  _hdrServerData = value;
               }
        }
        private string _hdrTransferHistory;
        public string hdrTransferHistory
        {
           get {
                   return _hdrTransferHistory;
               }
           set {
                    if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short hdrTransferHistory, was {value.Length} (min 0)");
                    if (value?.Length > 16384) throw new OdinDatabaseValidationException($"Too long hdrTransferHistory, was {value.Length} (max 16384)");
                  _hdrTransferHistory = value;
               }
        }
        internal string hdrTransferHistoryNoLengthCheck
        {
           get {
                   return _hdrTransferHistory;
               }
           set {
                    if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short hdrTransferHistory, was {value.Length} (min 0)");
                  _hdrTransferHistory = value;
               }
        }
        private string _hdrFileMetaData;
        public string hdrFileMetaData
        {
           get {
                   return _hdrFileMetaData;
               }
           set {
                    if (value == null) throw new OdinDatabaseValidationException("Cannot be null hdrFileMetaData");
                    if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short hdrFileMetaData, was {value.Length} (min 0)");
                    if (value?.Length > 60000) throw new OdinDatabaseValidationException($"Too long hdrFileMetaData, was {value.Length} (max 60000)");
                  _hdrFileMetaData = value;
               }
        }
        internal string hdrFileMetaDataNoLengthCheck
        {
           get {
                   return _hdrFileMetaData;
               }
           set {
                    if (value == null) throw new OdinDatabaseValidationException("Cannot be null hdrFileMetaData");
                    if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short hdrFileMetaData, was {value.Length} (min 0)");
                  _hdrFileMetaData = value;
               }
        }
        private Guid _hdrTmpDriveAlias;
        public Guid hdrTmpDriveAlias
        {
           get {
                   return _hdrTmpDriveAlias;
               }
           set {
                  _hdrTmpDriveAlias = value;
               }
        }
        private Guid _hdrTmpDriveType;
        public Guid hdrTmpDriveType
        {
           get {
                   return _hdrTmpDriveType;
               }
           set {
                  _hdrTmpDriveType = value;
               }
        }
        private UnixTimeUtc _created;
        public UnixTimeUtc created
        {
           get {
                   return _created;
               }
           set {
                  _created = value;
               }
        }
        private UnixTimeUtc _modified;
        public UnixTimeUtc modified
        {
           get {
                   return _modified;
               }
           set {
                  _modified = value;
               }
        }
    } // End of record DriveMainIndexRecord

    public abstract class TableDriveMainIndexCRUD
    {
        private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory;

        protected TableDriveMainIndexCRUD(CacheHelper cache, ScopedIdentityConnectionFactory scopedConnectionFactory)
        {
            _scopedConnectionFactory = scopedConnectionFactory;
        }


        public virtual async Task EnsureTableExistsAsync(bool dropExisting = false)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var cmd = cn.CreateCommand();
            if (dropExisting)
            {
                cmd.CommandText = "DROP TABLE IF EXISTS DriveMainIndex;";
                await cmd.ExecuteNonQueryAsync();
            }
            var rowid = "";
            if (_scopedConnectionFactory.DatabaseType == DatabaseType.Postgres)
               rowid = "rowid BIGSERIAL PRIMARY KEY,";
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            cmd.CommandText =
                "CREATE TABLE IF NOT EXISTS DriveMainIndex("
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
            await cmd.ExecuteNonQueryAsync();
        }

        protected virtual async Task<int> InsertAsync(DriveMainIndexRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.driveId.AssertGuidNotEmpty("Guid parameter driveId cannot be set to Empty GUID.");
            item.fileId.AssertGuidNotEmpty("Guid parameter fileId cannot be set to Empty GUID.");
            item.globalTransitId.AssertGuidNotEmpty("Guid parameter globalTransitId cannot be set to Empty GUID.");
            item.groupId.AssertGuidNotEmpty("Guid parameter groupId cannot be set to Empty GUID.");
            item.uniqueId.AssertGuidNotEmpty("Guid parameter uniqueId cannot be set to Empty GUID.");
            item.hdrVersionTag.AssertGuidNotEmpty("Guid parameter hdrVersionTag cannot be set to Empty GUID.");
            item.hdrLocalVersionTag.AssertGuidNotEmpty("Guid parameter hdrLocalVersionTag cannot be set to Empty GUID.");
            item.hdrTmpDriveAlias.AssertGuidNotEmpty("Guid parameter hdrTmpDriveAlias cannot be set to Empty GUID.");
            item.hdrTmpDriveType.AssertGuidNotEmpty("Guid parameter hdrTmpDriveType cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr = insertCommand.SqlNow();
                insertCommand.CommandText = "INSERT INTO DriveMainIndex (identityId,driveId,fileId,globalTransitId,fileState,requiredSecurityGroup,fileSystemType,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,uniqueId,byteCount,hdrEncryptedKeyHeader,hdrVersionTag,hdrAppData,hdrLocalVersionTag,hdrLocalAppData,hdrReactionSummary,hdrServerData,hdrTransferHistory,hdrFileMetaData,hdrTmpDriveAlias,hdrTmpDriveType,created,modified) " +
                                           $"VALUES (@identityId,@driveId,@fileId,@globalTransitId,@fileState,@requiredSecurityGroup,@fileSystemType,@userDate,@fileType,@dataType,@archivalStatus,@historyStatus,@senderId,@groupId,@uniqueId,@byteCount,@hdrEncryptedKeyHeader,@hdrVersionTag,@hdrAppData,@hdrLocalVersionTag,@hdrLocalAppData,@hdrReactionSummary,@hdrServerData,@hdrTransferHistory,@hdrFileMetaData,@hdrTmpDriveAlias,@hdrTmpDriveType,{sqlNowStr},{sqlNowStr})"+
                                            "RETURNING created,modified,rowId;";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.DbType = DbType.Binary;
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.DbType = DbType.Binary;
                insertParam2.ParameterName = "@driveId";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.DbType = DbType.Binary;
                insertParam3.ParameterName = "@fileId";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.DbType = DbType.Binary;
                insertParam4.ParameterName = "@globalTransitId";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.DbType = DbType.Int32;
                insertParam5.ParameterName = "@fileState";
                insertCommand.Parameters.Add(insertParam5);
                var insertParam6 = insertCommand.CreateParameter();
                insertParam6.DbType = DbType.Int32;
                insertParam6.ParameterName = "@requiredSecurityGroup";
                insertCommand.Parameters.Add(insertParam6);
                var insertParam7 = insertCommand.CreateParameter();
                insertParam7.DbType = DbType.Int32;
                insertParam7.ParameterName = "@fileSystemType";
                insertCommand.Parameters.Add(insertParam7);
                var insertParam8 = insertCommand.CreateParameter();
                insertParam8.DbType = DbType.Int64;
                insertParam8.ParameterName = "@userDate";
                insertCommand.Parameters.Add(insertParam8);
                var insertParam9 = insertCommand.CreateParameter();
                insertParam9.DbType = DbType.Int32;
                insertParam9.ParameterName = "@fileType";
                insertCommand.Parameters.Add(insertParam9);
                var insertParam10 = insertCommand.CreateParameter();
                insertParam10.DbType = DbType.Int32;
                insertParam10.ParameterName = "@dataType";
                insertCommand.Parameters.Add(insertParam10);
                var insertParam11 = insertCommand.CreateParameter();
                insertParam11.DbType = DbType.Int32;
                insertParam11.ParameterName = "@archivalStatus";
                insertCommand.Parameters.Add(insertParam11);
                var insertParam12 = insertCommand.CreateParameter();
                insertParam12.DbType = DbType.Int32;
                insertParam12.ParameterName = "@historyStatus";
                insertCommand.Parameters.Add(insertParam12);
                var insertParam13 = insertCommand.CreateParameter();
                insertParam13.DbType = DbType.String;
                insertParam13.ParameterName = "@senderId";
                insertCommand.Parameters.Add(insertParam13);
                var insertParam14 = insertCommand.CreateParameter();
                insertParam14.DbType = DbType.Binary;
                insertParam14.ParameterName = "@groupId";
                insertCommand.Parameters.Add(insertParam14);
                var insertParam15 = insertCommand.CreateParameter();
                insertParam15.DbType = DbType.Binary;
                insertParam15.ParameterName = "@uniqueId";
                insertCommand.Parameters.Add(insertParam15);
                var insertParam16 = insertCommand.CreateParameter();
                insertParam16.DbType = DbType.Int64;
                insertParam16.ParameterName = "@byteCount";
                insertCommand.Parameters.Add(insertParam16);
                var insertParam17 = insertCommand.CreateParameter();
                insertParam17.DbType = DbType.String;
                insertParam17.ParameterName = "@hdrEncryptedKeyHeader";
                insertCommand.Parameters.Add(insertParam17);
                var insertParam18 = insertCommand.CreateParameter();
                insertParam18.DbType = DbType.Binary;
                insertParam18.ParameterName = "@hdrVersionTag";
                insertCommand.Parameters.Add(insertParam18);
                var insertParam19 = insertCommand.CreateParameter();
                insertParam19.DbType = DbType.String;
                insertParam19.ParameterName = "@hdrAppData";
                insertCommand.Parameters.Add(insertParam19);
                var insertParam20 = insertCommand.CreateParameter();
                insertParam20.DbType = DbType.Binary;
                insertParam20.ParameterName = "@hdrLocalVersionTag";
                insertCommand.Parameters.Add(insertParam20);
                var insertParam21 = insertCommand.CreateParameter();
                insertParam21.DbType = DbType.String;
                insertParam21.ParameterName = "@hdrLocalAppData";
                insertCommand.Parameters.Add(insertParam21);
                var insertParam22 = insertCommand.CreateParameter();
                insertParam22.DbType = DbType.String;
                insertParam22.ParameterName = "@hdrReactionSummary";
                insertCommand.Parameters.Add(insertParam22);
                var insertParam23 = insertCommand.CreateParameter();
                insertParam23.DbType = DbType.String;
                insertParam23.ParameterName = "@hdrServerData";
                insertCommand.Parameters.Add(insertParam23);
                var insertParam24 = insertCommand.CreateParameter();
                insertParam24.DbType = DbType.String;
                insertParam24.ParameterName = "@hdrTransferHistory";
                insertCommand.Parameters.Add(insertParam24);
                var insertParam25 = insertCommand.CreateParameter();
                insertParam25.DbType = DbType.String;
                insertParam25.ParameterName = "@hdrFileMetaData";
                insertCommand.Parameters.Add(insertParam25);
                var insertParam26 = insertCommand.CreateParameter();
                insertParam26.DbType = DbType.Binary;
                insertParam26.ParameterName = "@hdrTmpDriveAlias";
                insertCommand.Parameters.Add(insertParam26);
                var insertParam27 = insertCommand.CreateParameter();
                insertParam27.DbType = DbType.Binary;
                insertParam27.ParameterName = "@hdrTmpDriveType";
                insertCommand.Parameters.Add(insertParam27);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.driveId.ToByteArray();
                insertParam3.Value = item.fileId.ToByteArray();
                insertParam4.Value = item.globalTransitId?.ToByteArray() ?? (object)DBNull.Value;
                insertParam5.Value = item.fileState;
                insertParam6.Value = item.requiredSecurityGroup;
                insertParam7.Value = item.fileSystemType;
                insertParam8.Value = item.userDate.milliseconds;
                insertParam9.Value = item.fileType;
                insertParam10.Value = item.dataType;
                insertParam11.Value = item.archivalStatus;
                insertParam12.Value = item.historyStatus;
                insertParam13.Value = item.senderId ?? (object)DBNull.Value;
                insertParam14.Value = item.groupId?.ToByteArray() ?? (object)DBNull.Value;
                insertParam15.Value = item.uniqueId?.ToByteArray() ?? (object)DBNull.Value;
                insertParam16.Value = item.byteCount;
                insertParam17.Value = item.hdrEncryptedKeyHeader;
                insertParam18.Value = item.hdrVersionTag.ToByteArray();
                insertParam19.Value = item.hdrAppData;
                insertParam20.Value = item.hdrLocalVersionTag?.ToByteArray() ?? (object)DBNull.Value;
                insertParam21.Value = item.hdrLocalAppData ?? (object)DBNull.Value;
                insertParam22.Value = item.hdrReactionSummary ?? (object)DBNull.Value;
                insertParam23.Value = item.hdrServerData;
                insertParam24.Value = item.hdrTransferHistory ?? (object)DBNull.Value;
                insertParam25.Value = item.hdrFileMetaData;
                insertParam26.Value = item.hdrTmpDriveAlias.ToByteArray();
                insertParam27.Value = item.hdrTmpDriveType.ToByteArray();
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    long modified = (long) rdr[1];
                    item.created = new UnixTimeUtc(created);
                    item.modified = new UnixTimeUtc((long)modified);
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<bool> TryInsertAsync(DriveMainIndexRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.driveId.AssertGuidNotEmpty("Guid parameter driveId cannot be set to Empty GUID.");
            item.fileId.AssertGuidNotEmpty("Guid parameter fileId cannot be set to Empty GUID.");
            item.globalTransitId.AssertGuidNotEmpty("Guid parameter globalTransitId cannot be set to Empty GUID.");
            item.groupId.AssertGuidNotEmpty("Guid parameter groupId cannot be set to Empty GUID.");
            item.uniqueId.AssertGuidNotEmpty("Guid parameter uniqueId cannot be set to Empty GUID.");
            item.hdrVersionTag.AssertGuidNotEmpty("Guid parameter hdrVersionTag cannot be set to Empty GUID.");
            item.hdrLocalVersionTag.AssertGuidNotEmpty("Guid parameter hdrLocalVersionTag cannot be set to Empty GUID.");
            item.hdrTmpDriveAlias.AssertGuidNotEmpty("Guid parameter hdrTmpDriveAlias cannot be set to Empty GUID.");
            item.hdrTmpDriveType.AssertGuidNotEmpty("Guid parameter hdrTmpDriveType cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr = insertCommand.SqlNow();
                insertCommand.CommandText = "INSERT INTO DriveMainIndex (identityId,driveId,fileId,globalTransitId,fileState,requiredSecurityGroup,fileSystemType,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,uniqueId,byteCount,hdrEncryptedKeyHeader,hdrVersionTag,hdrAppData,hdrLocalVersionTag,hdrLocalAppData,hdrReactionSummary,hdrServerData,hdrTransferHistory,hdrFileMetaData,hdrTmpDriveAlias,hdrTmpDriveType,created,modified) " +
                                            $"VALUES (@identityId,@driveId,@fileId,@globalTransitId,@fileState,@requiredSecurityGroup,@fileSystemType,@userDate,@fileType,@dataType,@archivalStatus,@historyStatus,@senderId,@groupId,@uniqueId,@byteCount,@hdrEncryptedKeyHeader,@hdrVersionTag,@hdrAppData,@hdrLocalVersionTag,@hdrLocalAppData,@hdrReactionSummary,@hdrServerData,@hdrTransferHistory,@hdrFileMetaData,@hdrTmpDriveAlias,@hdrTmpDriveType,{sqlNowStr},{sqlNowStr}) " +
                                            "ON CONFLICT DO NOTHING "+
                                            "RETURNING created,modified,rowId;";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.DbType = DbType.Binary;
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.DbType = DbType.Binary;
                insertParam2.ParameterName = "@driveId";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.DbType = DbType.Binary;
                insertParam3.ParameterName = "@fileId";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.DbType = DbType.Binary;
                insertParam4.ParameterName = "@globalTransitId";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.DbType = DbType.Int32;
                insertParam5.ParameterName = "@fileState";
                insertCommand.Parameters.Add(insertParam5);
                var insertParam6 = insertCommand.CreateParameter();
                insertParam6.DbType = DbType.Int32;
                insertParam6.ParameterName = "@requiredSecurityGroup";
                insertCommand.Parameters.Add(insertParam6);
                var insertParam7 = insertCommand.CreateParameter();
                insertParam7.DbType = DbType.Int32;
                insertParam7.ParameterName = "@fileSystemType";
                insertCommand.Parameters.Add(insertParam7);
                var insertParam8 = insertCommand.CreateParameter();
                insertParam8.DbType = DbType.Int64;
                insertParam8.ParameterName = "@userDate";
                insertCommand.Parameters.Add(insertParam8);
                var insertParam9 = insertCommand.CreateParameter();
                insertParam9.DbType = DbType.Int32;
                insertParam9.ParameterName = "@fileType";
                insertCommand.Parameters.Add(insertParam9);
                var insertParam10 = insertCommand.CreateParameter();
                insertParam10.DbType = DbType.Int32;
                insertParam10.ParameterName = "@dataType";
                insertCommand.Parameters.Add(insertParam10);
                var insertParam11 = insertCommand.CreateParameter();
                insertParam11.DbType = DbType.Int32;
                insertParam11.ParameterName = "@archivalStatus";
                insertCommand.Parameters.Add(insertParam11);
                var insertParam12 = insertCommand.CreateParameter();
                insertParam12.DbType = DbType.Int32;
                insertParam12.ParameterName = "@historyStatus";
                insertCommand.Parameters.Add(insertParam12);
                var insertParam13 = insertCommand.CreateParameter();
                insertParam13.DbType = DbType.String;
                insertParam13.ParameterName = "@senderId";
                insertCommand.Parameters.Add(insertParam13);
                var insertParam14 = insertCommand.CreateParameter();
                insertParam14.DbType = DbType.Binary;
                insertParam14.ParameterName = "@groupId";
                insertCommand.Parameters.Add(insertParam14);
                var insertParam15 = insertCommand.CreateParameter();
                insertParam15.DbType = DbType.Binary;
                insertParam15.ParameterName = "@uniqueId";
                insertCommand.Parameters.Add(insertParam15);
                var insertParam16 = insertCommand.CreateParameter();
                insertParam16.DbType = DbType.Int64;
                insertParam16.ParameterName = "@byteCount";
                insertCommand.Parameters.Add(insertParam16);
                var insertParam17 = insertCommand.CreateParameter();
                insertParam17.DbType = DbType.String;
                insertParam17.ParameterName = "@hdrEncryptedKeyHeader";
                insertCommand.Parameters.Add(insertParam17);
                var insertParam18 = insertCommand.CreateParameter();
                insertParam18.DbType = DbType.Binary;
                insertParam18.ParameterName = "@hdrVersionTag";
                insertCommand.Parameters.Add(insertParam18);
                var insertParam19 = insertCommand.CreateParameter();
                insertParam19.DbType = DbType.String;
                insertParam19.ParameterName = "@hdrAppData";
                insertCommand.Parameters.Add(insertParam19);
                var insertParam20 = insertCommand.CreateParameter();
                insertParam20.DbType = DbType.Binary;
                insertParam20.ParameterName = "@hdrLocalVersionTag";
                insertCommand.Parameters.Add(insertParam20);
                var insertParam21 = insertCommand.CreateParameter();
                insertParam21.DbType = DbType.String;
                insertParam21.ParameterName = "@hdrLocalAppData";
                insertCommand.Parameters.Add(insertParam21);
                var insertParam22 = insertCommand.CreateParameter();
                insertParam22.DbType = DbType.String;
                insertParam22.ParameterName = "@hdrReactionSummary";
                insertCommand.Parameters.Add(insertParam22);
                var insertParam23 = insertCommand.CreateParameter();
                insertParam23.DbType = DbType.String;
                insertParam23.ParameterName = "@hdrServerData";
                insertCommand.Parameters.Add(insertParam23);
                var insertParam24 = insertCommand.CreateParameter();
                insertParam24.DbType = DbType.String;
                insertParam24.ParameterName = "@hdrTransferHistory";
                insertCommand.Parameters.Add(insertParam24);
                var insertParam25 = insertCommand.CreateParameter();
                insertParam25.DbType = DbType.String;
                insertParam25.ParameterName = "@hdrFileMetaData";
                insertCommand.Parameters.Add(insertParam25);
                var insertParam26 = insertCommand.CreateParameter();
                insertParam26.DbType = DbType.Binary;
                insertParam26.ParameterName = "@hdrTmpDriveAlias";
                insertCommand.Parameters.Add(insertParam26);
                var insertParam27 = insertCommand.CreateParameter();
                insertParam27.DbType = DbType.Binary;
                insertParam27.ParameterName = "@hdrTmpDriveType";
                insertCommand.Parameters.Add(insertParam27);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.driveId.ToByteArray();
                insertParam3.Value = item.fileId.ToByteArray();
                insertParam4.Value = item.globalTransitId?.ToByteArray() ?? (object)DBNull.Value;
                insertParam5.Value = item.fileState;
                insertParam6.Value = item.requiredSecurityGroup;
                insertParam7.Value = item.fileSystemType;
                insertParam8.Value = item.userDate.milliseconds;
                insertParam9.Value = item.fileType;
                insertParam10.Value = item.dataType;
                insertParam11.Value = item.archivalStatus;
                insertParam12.Value = item.historyStatus;
                insertParam13.Value = item.senderId ?? (object)DBNull.Value;
                insertParam14.Value = item.groupId?.ToByteArray() ?? (object)DBNull.Value;
                insertParam15.Value = item.uniqueId?.ToByteArray() ?? (object)DBNull.Value;
                insertParam16.Value = item.byteCount;
                insertParam17.Value = item.hdrEncryptedKeyHeader;
                insertParam18.Value = item.hdrVersionTag.ToByteArray();
                insertParam19.Value = item.hdrAppData;
                insertParam20.Value = item.hdrLocalVersionTag?.ToByteArray() ?? (object)DBNull.Value;
                insertParam21.Value = item.hdrLocalAppData ?? (object)DBNull.Value;
                insertParam22.Value = item.hdrReactionSummary ?? (object)DBNull.Value;
                insertParam23.Value = item.hdrServerData;
                insertParam24.Value = item.hdrTransferHistory ?? (object)DBNull.Value;
                insertParam25.Value = item.hdrFileMetaData;
                insertParam26.Value = item.hdrTmpDriveAlias.ToByteArray();
                insertParam27.Value = item.hdrTmpDriveType.ToByteArray();
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    long modified = (long) rdr[1];
                    item.created = new UnixTimeUtc(created);
                    item.modified = new UnixTimeUtc((long)modified);
                    item.rowId = (long) rdr[2];
                    return true;
                }
                return false;
            }
        }

        protected virtual async Task<int> UpsertAsync(DriveMainIndexRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.driveId.AssertGuidNotEmpty("Guid parameter driveId cannot be set to Empty GUID.");
            item.fileId.AssertGuidNotEmpty("Guid parameter fileId cannot be set to Empty GUID.");
            item.globalTransitId.AssertGuidNotEmpty("Guid parameter globalTransitId cannot be set to Empty GUID.");
            item.groupId.AssertGuidNotEmpty("Guid parameter groupId cannot be set to Empty GUID.");
            item.uniqueId.AssertGuidNotEmpty("Guid parameter uniqueId cannot be set to Empty GUID.");
            item.hdrVersionTag.AssertGuidNotEmpty("Guid parameter hdrVersionTag cannot be set to Empty GUID.");
            item.hdrLocalVersionTag.AssertGuidNotEmpty("Guid parameter hdrLocalVersionTag cannot be set to Empty GUID.");
            item.hdrTmpDriveAlias.AssertGuidNotEmpty("Guid parameter hdrTmpDriveAlias cannot be set to Empty GUID.");
            item.hdrTmpDriveType.AssertGuidNotEmpty("Guid parameter hdrTmpDriveType cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                string sqlNowStr = upsertCommand.SqlNow();
                upsertCommand.CommandText = "INSERT INTO DriveMainIndex (identityId,driveId,fileId,globalTransitId,fileState,requiredSecurityGroup,fileSystemType,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,uniqueId,byteCount,hdrEncryptedKeyHeader,hdrVersionTag,hdrAppData,hdrLocalVersionTag,hdrLocalAppData,hdrReactionSummary,hdrServerData,hdrTransferHistory,hdrFileMetaData,hdrTmpDriveAlias,hdrTmpDriveType,created,modified) " +
                                            $"VALUES (@identityId,@driveId,@fileId,@globalTransitId,@fileState,@requiredSecurityGroup,@fileSystemType,@userDate,@fileType,@dataType,@archivalStatus,@historyStatus,@senderId,@groupId,@uniqueId,@byteCount,@hdrEncryptedKeyHeader,@hdrVersionTag,@hdrAppData,@hdrLocalVersionTag,@hdrLocalAppData,@hdrReactionSummary,@hdrServerData,@hdrTransferHistory,@hdrFileMetaData,@hdrTmpDriveAlias,@hdrTmpDriveType,{sqlNowStr},{sqlNowStr})"+
                                            "ON CONFLICT (identityId,driveId,fileId) DO UPDATE "+
                                            $"SET globalTransitId = @globalTransitId,fileState = @fileState,requiredSecurityGroup = @requiredSecurityGroup,fileSystemType = @fileSystemType,userDate = @userDate,fileType = @fileType,dataType = @dataType,archivalStatus = @archivalStatus,historyStatus = @historyStatus,senderId = @senderId,groupId = @groupId,uniqueId = @uniqueId,byteCount = @byteCount,hdrEncryptedKeyHeader = @hdrEncryptedKeyHeader,hdrVersionTag = @hdrVersionTag,hdrAppData = @hdrAppData,hdrLocalVersionTag = @hdrLocalVersionTag,hdrLocalAppData = @hdrLocalAppData,hdrReactionSummary = @hdrReactionSummary,hdrServerData = @hdrServerData,hdrTransferHistory = @hdrTransferHistory,hdrFileMetaData = @hdrFileMetaData,hdrTmpDriveAlias = @hdrTmpDriveAlias,hdrTmpDriveType = @hdrTmpDriveType,modified = {upsertCommand.SqlMax()}(DriveMainIndex.modified+1,{sqlNowStr}) "+
                                            "RETURNING created,modified,rowId;";
                var upsertParam1 = upsertCommand.CreateParameter();
                upsertParam1.DbType = DbType.Binary;
                upsertParam1.ParameterName = "@identityId";
                upsertCommand.Parameters.Add(upsertParam1);
                var upsertParam2 = upsertCommand.CreateParameter();
                upsertParam2.DbType = DbType.Binary;
                upsertParam2.ParameterName = "@driveId";
                upsertCommand.Parameters.Add(upsertParam2);
                var upsertParam3 = upsertCommand.CreateParameter();
                upsertParam3.DbType = DbType.Binary;
                upsertParam3.ParameterName = "@fileId";
                upsertCommand.Parameters.Add(upsertParam3);
                var upsertParam4 = upsertCommand.CreateParameter();
                upsertParam4.DbType = DbType.Binary;
                upsertParam4.ParameterName = "@globalTransitId";
                upsertCommand.Parameters.Add(upsertParam4);
                var upsertParam5 = upsertCommand.CreateParameter();
                upsertParam5.DbType = DbType.Int32;
                upsertParam5.ParameterName = "@fileState";
                upsertCommand.Parameters.Add(upsertParam5);
                var upsertParam6 = upsertCommand.CreateParameter();
                upsertParam6.DbType = DbType.Int32;
                upsertParam6.ParameterName = "@requiredSecurityGroup";
                upsertCommand.Parameters.Add(upsertParam6);
                var upsertParam7 = upsertCommand.CreateParameter();
                upsertParam7.DbType = DbType.Int32;
                upsertParam7.ParameterName = "@fileSystemType";
                upsertCommand.Parameters.Add(upsertParam7);
                var upsertParam8 = upsertCommand.CreateParameter();
                upsertParam8.DbType = DbType.Int64;
                upsertParam8.ParameterName = "@userDate";
                upsertCommand.Parameters.Add(upsertParam8);
                var upsertParam9 = upsertCommand.CreateParameter();
                upsertParam9.DbType = DbType.Int32;
                upsertParam9.ParameterName = "@fileType";
                upsertCommand.Parameters.Add(upsertParam9);
                var upsertParam10 = upsertCommand.CreateParameter();
                upsertParam10.DbType = DbType.Int32;
                upsertParam10.ParameterName = "@dataType";
                upsertCommand.Parameters.Add(upsertParam10);
                var upsertParam11 = upsertCommand.CreateParameter();
                upsertParam11.DbType = DbType.Int32;
                upsertParam11.ParameterName = "@archivalStatus";
                upsertCommand.Parameters.Add(upsertParam11);
                var upsertParam12 = upsertCommand.CreateParameter();
                upsertParam12.DbType = DbType.Int32;
                upsertParam12.ParameterName = "@historyStatus";
                upsertCommand.Parameters.Add(upsertParam12);
                var upsertParam13 = upsertCommand.CreateParameter();
                upsertParam13.DbType = DbType.String;
                upsertParam13.ParameterName = "@senderId";
                upsertCommand.Parameters.Add(upsertParam13);
                var upsertParam14 = upsertCommand.CreateParameter();
                upsertParam14.DbType = DbType.Binary;
                upsertParam14.ParameterName = "@groupId";
                upsertCommand.Parameters.Add(upsertParam14);
                var upsertParam15 = upsertCommand.CreateParameter();
                upsertParam15.DbType = DbType.Binary;
                upsertParam15.ParameterName = "@uniqueId";
                upsertCommand.Parameters.Add(upsertParam15);
                var upsertParam16 = upsertCommand.CreateParameter();
                upsertParam16.DbType = DbType.Int64;
                upsertParam16.ParameterName = "@byteCount";
                upsertCommand.Parameters.Add(upsertParam16);
                var upsertParam17 = upsertCommand.CreateParameter();
                upsertParam17.DbType = DbType.String;
                upsertParam17.ParameterName = "@hdrEncryptedKeyHeader";
                upsertCommand.Parameters.Add(upsertParam17);
                var upsertParam18 = upsertCommand.CreateParameter();
                upsertParam18.DbType = DbType.Binary;
                upsertParam18.ParameterName = "@hdrVersionTag";
                upsertCommand.Parameters.Add(upsertParam18);
                var upsertParam19 = upsertCommand.CreateParameter();
                upsertParam19.DbType = DbType.String;
                upsertParam19.ParameterName = "@hdrAppData";
                upsertCommand.Parameters.Add(upsertParam19);
                var upsertParam20 = upsertCommand.CreateParameter();
                upsertParam20.DbType = DbType.Binary;
                upsertParam20.ParameterName = "@hdrLocalVersionTag";
                upsertCommand.Parameters.Add(upsertParam20);
                var upsertParam21 = upsertCommand.CreateParameter();
                upsertParam21.DbType = DbType.String;
                upsertParam21.ParameterName = "@hdrLocalAppData";
                upsertCommand.Parameters.Add(upsertParam21);
                var upsertParam22 = upsertCommand.CreateParameter();
                upsertParam22.DbType = DbType.String;
                upsertParam22.ParameterName = "@hdrReactionSummary";
                upsertCommand.Parameters.Add(upsertParam22);
                var upsertParam23 = upsertCommand.CreateParameter();
                upsertParam23.DbType = DbType.String;
                upsertParam23.ParameterName = "@hdrServerData";
                upsertCommand.Parameters.Add(upsertParam23);
                var upsertParam24 = upsertCommand.CreateParameter();
                upsertParam24.DbType = DbType.String;
                upsertParam24.ParameterName = "@hdrTransferHistory";
                upsertCommand.Parameters.Add(upsertParam24);
                var upsertParam25 = upsertCommand.CreateParameter();
                upsertParam25.DbType = DbType.String;
                upsertParam25.ParameterName = "@hdrFileMetaData";
                upsertCommand.Parameters.Add(upsertParam25);
                var upsertParam26 = upsertCommand.CreateParameter();
                upsertParam26.DbType = DbType.Binary;
                upsertParam26.ParameterName = "@hdrTmpDriveAlias";
                upsertCommand.Parameters.Add(upsertParam26);
                var upsertParam27 = upsertCommand.CreateParameter();
                upsertParam27.DbType = DbType.Binary;
                upsertParam27.ParameterName = "@hdrTmpDriveType";
                upsertCommand.Parameters.Add(upsertParam27);
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
                upsertParam20.Value = item.hdrLocalVersionTag?.ToByteArray() ?? (object)DBNull.Value;
                upsertParam21.Value = item.hdrLocalAppData ?? (object)DBNull.Value;
                upsertParam22.Value = item.hdrReactionSummary ?? (object)DBNull.Value;
                upsertParam23.Value = item.hdrServerData;
                upsertParam24.Value = item.hdrTransferHistory ?? (object)DBNull.Value;
                upsertParam25.Value = item.hdrFileMetaData;
                upsertParam26.Value = item.hdrTmpDriveAlias.ToByteArray();
                upsertParam27.Value = item.hdrTmpDriveType.ToByteArray();
                await using var rdr = await upsertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    long modified = (long) rdr[1];
                    item.created = new UnixTimeUtc(created);
                    item.modified = new UnixTimeUtc((long)modified);
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<int> UpdateAsync(DriveMainIndexRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.driveId.AssertGuidNotEmpty("Guid parameter driveId cannot be set to Empty GUID.");
            item.fileId.AssertGuidNotEmpty("Guid parameter fileId cannot be set to Empty GUID.");
            item.globalTransitId.AssertGuidNotEmpty("Guid parameter globalTransitId cannot be set to Empty GUID.");
            item.groupId.AssertGuidNotEmpty("Guid parameter groupId cannot be set to Empty GUID.");
            item.uniqueId.AssertGuidNotEmpty("Guid parameter uniqueId cannot be set to Empty GUID.");
            item.hdrVersionTag.AssertGuidNotEmpty("Guid parameter hdrVersionTag cannot be set to Empty GUID.");
            item.hdrLocalVersionTag.AssertGuidNotEmpty("Guid parameter hdrLocalVersionTag cannot be set to Empty GUID.");
            item.hdrTmpDriveAlias.AssertGuidNotEmpty("Guid parameter hdrTmpDriveAlias cannot be set to Empty GUID.");
            item.hdrTmpDriveType.AssertGuidNotEmpty("Guid parameter hdrTmpDriveType cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                string sqlNowStr = updateCommand.SqlNow();
                updateCommand.CommandText = "UPDATE DriveMainIndex " +
                                            $"SET globalTransitId = @globalTransitId,fileState = @fileState,requiredSecurityGroup = @requiredSecurityGroup,fileSystemType = @fileSystemType,userDate = @userDate,fileType = @fileType,dataType = @dataType,archivalStatus = @archivalStatus,historyStatus = @historyStatus,senderId = @senderId,groupId = @groupId,uniqueId = @uniqueId,byteCount = @byteCount,hdrEncryptedKeyHeader = @hdrEncryptedKeyHeader,hdrVersionTag = @hdrVersionTag,hdrAppData = @hdrAppData,hdrLocalVersionTag = @hdrLocalVersionTag,hdrLocalAppData = @hdrLocalAppData,hdrReactionSummary = @hdrReactionSummary,hdrServerData = @hdrServerData,hdrTransferHistory = @hdrTransferHistory,hdrFileMetaData = @hdrFileMetaData,hdrTmpDriveAlias = @hdrTmpDriveAlias,hdrTmpDriveType = @hdrTmpDriveType,modified = {updateCommand.SqlMax()}(DriveMainIndex.modified+1,{sqlNowStr}) "+
                                            "WHERE (identityId = @identityId AND driveId = @driveId AND fileId = @fileId) "+
                                            "RETURNING created,modified,rowId;";
                var updateParam1 = updateCommand.CreateParameter();
                updateParam1.DbType = DbType.Binary;
                updateParam1.ParameterName = "@identityId";
                updateCommand.Parameters.Add(updateParam1);
                var updateParam2 = updateCommand.CreateParameter();
                updateParam2.DbType = DbType.Binary;
                updateParam2.ParameterName = "@driveId";
                updateCommand.Parameters.Add(updateParam2);
                var updateParam3 = updateCommand.CreateParameter();
                updateParam3.DbType = DbType.Binary;
                updateParam3.ParameterName = "@fileId";
                updateCommand.Parameters.Add(updateParam3);
                var updateParam4 = updateCommand.CreateParameter();
                updateParam4.DbType = DbType.Binary;
                updateParam4.ParameterName = "@globalTransitId";
                updateCommand.Parameters.Add(updateParam4);
                var updateParam5 = updateCommand.CreateParameter();
                updateParam5.DbType = DbType.Int32;
                updateParam5.ParameterName = "@fileState";
                updateCommand.Parameters.Add(updateParam5);
                var updateParam6 = updateCommand.CreateParameter();
                updateParam6.DbType = DbType.Int32;
                updateParam6.ParameterName = "@requiredSecurityGroup";
                updateCommand.Parameters.Add(updateParam6);
                var updateParam7 = updateCommand.CreateParameter();
                updateParam7.DbType = DbType.Int32;
                updateParam7.ParameterName = "@fileSystemType";
                updateCommand.Parameters.Add(updateParam7);
                var updateParam8 = updateCommand.CreateParameter();
                updateParam8.DbType = DbType.Int64;
                updateParam8.ParameterName = "@userDate";
                updateCommand.Parameters.Add(updateParam8);
                var updateParam9 = updateCommand.CreateParameter();
                updateParam9.DbType = DbType.Int32;
                updateParam9.ParameterName = "@fileType";
                updateCommand.Parameters.Add(updateParam9);
                var updateParam10 = updateCommand.CreateParameter();
                updateParam10.DbType = DbType.Int32;
                updateParam10.ParameterName = "@dataType";
                updateCommand.Parameters.Add(updateParam10);
                var updateParam11 = updateCommand.CreateParameter();
                updateParam11.DbType = DbType.Int32;
                updateParam11.ParameterName = "@archivalStatus";
                updateCommand.Parameters.Add(updateParam11);
                var updateParam12 = updateCommand.CreateParameter();
                updateParam12.DbType = DbType.Int32;
                updateParam12.ParameterName = "@historyStatus";
                updateCommand.Parameters.Add(updateParam12);
                var updateParam13 = updateCommand.CreateParameter();
                updateParam13.DbType = DbType.String;
                updateParam13.ParameterName = "@senderId";
                updateCommand.Parameters.Add(updateParam13);
                var updateParam14 = updateCommand.CreateParameter();
                updateParam14.DbType = DbType.Binary;
                updateParam14.ParameterName = "@groupId";
                updateCommand.Parameters.Add(updateParam14);
                var updateParam15 = updateCommand.CreateParameter();
                updateParam15.DbType = DbType.Binary;
                updateParam15.ParameterName = "@uniqueId";
                updateCommand.Parameters.Add(updateParam15);
                var updateParam16 = updateCommand.CreateParameter();
                updateParam16.DbType = DbType.Int64;
                updateParam16.ParameterName = "@byteCount";
                updateCommand.Parameters.Add(updateParam16);
                var updateParam17 = updateCommand.CreateParameter();
                updateParam17.DbType = DbType.String;
                updateParam17.ParameterName = "@hdrEncryptedKeyHeader";
                updateCommand.Parameters.Add(updateParam17);
                var updateParam18 = updateCommand.CreateParameter();
                updateParam18.DbType = DbType.Binary;
                updateParam18.ParameterName = "@hdrVersionTag";
                updateCommand.Parameters.Add(updateParam18);
                var updateParam19 = updateCommand.CreateParameter();
                updateParam19.DbType = DbType.String;
                updateParam19.ParameterName = "@hdrAppData";
                updateCommand.Parameters.Add(updateParam19);
                var updateParam20 = updateCommand.CreateParameter();
                updateParam20.DbType = DbType.Binary;
                updateParam20.ParameterName = "@hdrLocalVersionTag";
                updateCommand.Parameters.Add(updateParam20);
                var updateParam21 = updateCommand.CreateParameter();
                updateParam21.DbType = DbType.String;
                updateParam21.ParameterName = "@hdrLocalAppData";
                updateCommand.Parameters.Add(updateParam21);
                var updateParam22 = updateCommand.CreateParameter();
                updateParam22.DbType = DbType.String;
                updateParam22.ParameterName = "@hdrReactionSummary";
                updateCommand.Parameters.Add(updateParam22);
                var updateParam23 = updateCommand.CreateParameter();
                updateParam23.DbType = DbType.String;
                updateParam23.ParameterName = "@hdrServerData";
                updateCommand.Parameters.Add(updateParam23);
                var updateParam24 = updateCommand.CreateParameter();
                updateParam24.DbType = DbType.String;
                updateParam24.ParameterName = "@hdrTransferHistory";
                updateCommand.Parameters.Add(updateParam24);
                var updateParam25 = updateCommand.CreateParameter();
                updateParam25.DbType = DbType.String;
                updateParam25.ParameterName = "@hdrFileMetaData";
                updateCommand.Parameters.Add(updateParam25);
                var updateParam26 = updateCommand.CreateParameter();
                updateParam26.DbType = DbType.Binary;
                updateParam26.ParameterName = "@hdrTmpDriveAlias";
                updateCommand.Parameters.Add(updateParam26);
                var updateParam27 = updateCommand.CreateParameter();
                updateParam27.DbType = DbType.Binary;
                updateParam27.ParameterName = "@hdrTmpDriveType";
                updateCommand.Parameters.Add(updateParam27);
                updateParam1.Value = item.identityId.ToByteArray();
                updateParam2.Value = item.driveId.ToByteArray();
                updateParam3.Value = item.fileId.ToByteArray();
                updateParam4.Value = item.globalTransitId?.ToByteArray() ?? (object)DBNull.Value;
                updateParam5.Value = item.fileState;
                updateParam6.Value = item.requiredSecurityGroup;
                updateParam7.Value = item.fileSystemType;
                updateParam8.Value = item.userDate.milliseconds;
                updateParam9.Value = item.fileType;
                updateParam10.Value = item.dataType;
                updateParam11.Value = item.archivalStatus;
                updateParam12.Value = item.historyStatus;
                updateParam13.Value = item.senderId ?? (object)DBNull.Value;
                updateParam14.Value = item.groupId?.ToByteArray() ?? (object)DBNull.Value;
                updateParam15.Value = item.uniqueId?.ToByteArray() ?? (object)DBNull.Value;
                updateParam16.Value = item.byteCount;
                updateParam17.Value = item.hdrEncryptedKeyHeader;
                updateParam18.Value = item.hdrVersionTag.ToByteArray();
                updateParam19.Value = item.hdrAppData;
                updateParam20.Value = item.hdrLocalVersionTag?.ToByteArray() ?? (object)DBNull.Value;
                updateParam21.Value = item.hdrLocalAppData ?? (object)DBNull.Value;
                updateParam22.Value = item.hdrReactionSummary ?? (object)DBNull.Value;
                updateParam23.Value = item.hdrServerData;
                updateParam24.Value = item.hdrTransferHistory ?? (object)DBNull.Value;
                updateParam25.Value = item.hdrFileMetaData;
                updateParam26.Value = item.hdrTmpDriveAlias.ToByteArray();
                updateParam27.Value = item.hdrTmpDriveType.ToByteArray();
                await using var rdr = await updateCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    long modified = (long) rdr[1];
                    item.created = new UnixTimeUtc(created);
                    item.modified = new UnixTimeUtc((long)modified);
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<int> GetCountAsync()
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

        public static List<string> GetColumnNames()
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
                var getCountDriveParam1 = getCountDriveCommand.CreateParameter();
                getCountDriveParam1.DbType = DbType.Binary;
                getCountDriveParam1.ParameterName = "$driveId";
                getCountDriveCommand.Parameters.Add(getCountDriveParam1);
                getCountDriveParam1.Value = driveId.ToByteArray();
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
            item.senderIdNoLengthCheck = (rdr[13] == DBNull.Value) ? null : (string)rdr[13];
            item.groupId = (rdr[14] == DBNull.Value) ? null : new Guid((byte[])rdr[14]);
            item.uniqueId = (rdr[15] == DBNull.Value) ? null : new Guid((byte[])rdr[15]);
            item.byteCount = (rdr[16] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[16];
            item.hdrEncryptedKeyHeaderNoLengthCheck = (rdr[17] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[17];
            item.hdrVersionTag = (rdr[18] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[18]);
            item.hdrAppDataNoLengthCheck = (rdr[19] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[19];
            item.hdrLocalVersionTag = (rdr[20] == DBNull.Value) ? null : new Guid((byte[])rdr[20]);
            item.hdrLocalAppDataNoLengthCheck = (rdr[21] == DBNull.Value) ? null : (string)rdr[21];
            item.hdrReactionSummaryNoLengthCheck = (rdr[22] == DBNull.Value) ? null : (string)rdr[22];
            item.hdrServerDataNoLengthCheck = (rdr[23] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[23];
            item.hdrTransferHistoryNoLengthCheck = (rdr[24] == DBNull.Value) ? null : (string)rdr[24];
            item.hdrFileMetaDataNoLengthCheck = (rdr[25] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[25];
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
                var delete0Param1 = delete0Command.CreateParameter();
                delete0Param1.DbType = DbType.Binary;
                delete0Param1.ParameterName = "@identityId";
                delete0Command.Parameters.Add(delete0Param1);
                var delete0Param2 = delete0Command.CreateParameter();
                delete0Param2.DbType = DbType.Binary;
                delete0Param2.ParameterName = "@driveId";
                delete0Command.Parameters.Add(delete0Param2);
                var delete0Param3 = delete0Command.CreateParameter();
                delete0Param3.DbType = DbType.Binary;
                delete0Param3.ParameterName = "@fileId";
                delete0Command.Parameters.Add(delete0Param3);

                delete0Param1.Value = identityId.ToByteArray();
                delete0Param2.Value = driveId.ToByteArray();
                delete0Param3.Value = fileId.ToByteArray();
                var count = await delete0Command.ExecuteNonQueryAsync();
                return count;
            }
        }

        protected DriveMainIndexRecord ReadRecordFromReader0(DbDataReader rdr,Guid identityId,Guid driveId,Guid? uniqueId)
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
            item.senderIdNoLengthCheck = (rdr[11] == DBNull.Value) ? null : (string)rdr[11];
            item.groupId = (rdr[12] == DBNull.Value) ? null : new Guid((byte[])rdr[12]);
            item.byteCount = (rdr[13] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[13];
            item.hdrEncryptedKeyHeaderNoLengthCheck = (rdr[14] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[14];
            item.hdrVersionTag = (rdr[15] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[15]);
            item.hdrAppDataNoLengthCheck = (rdr[16] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[16];
            item.hdrLocalVersionTag = (rdr[17] == DBNull.Value) ? null : new Guid((byte[])rdr[17]);
            item.hdrLocalAppDataNoLengthCheck = (rdr[18] == DBNull.Value) ? null : (string)rdr[18];
            item.hdrReactionSummaryNoLengthCheck = (rdr[19] == DBNull.Value) ? null : (string)rdr[19];
            item.hdrServerDataNoLengthCheck = (rdr[20] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[20];
            item.hdrTransferHistoryNoLengthCheck = (rdr[21] == DBNull.Value) ? null : (string)rdr[21];
            item.hdrFileMetaDataNoLengthCheck = (rdr[22] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[22];
            item.hdrTmpDriveAlias = (rdr[23] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[23]);
            item.hdrTmpDriveType = (rdr[24] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[24]);
            item.created = (rdr[25] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[25]);
            item.modified = (rdr[26] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[26]);
            return item;
       }

        protected virtual async Task<DriveMainIndexRecord> GetByUniqueIdAsync(Guid identityId,Guid driveId,Guid? uniqueId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT rowId,fileId,globalTransitId,fileState,requiredSecurityGroup,fileSystemType,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,byteCount,hdrEncryptedKeyHeader,hdrVersionTag,hdrAppData,hdrLocalVersionTag,hdrLocalAppData,hdrReactionSummary,hdrServerData,hdrTransferHistory,hdrFileMetaData,hdrTmpDriveAlias,hdrTmpDriveType,created,modified FROM DriveMainIndex " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND uniqueId = @uniqueId LIMIT 1;"+
                                             ";";
                var get0Param1 = get0Command.CreateParameter();
                get0Param1.DbType = DbType.Binary;
                get0Param1.ParameterName = "@identityId";
                get0Command.Parameters.Add(get0Param1);
                var get0Param2 = get0Command.CreateParameter();
                get0Param2.DbType = DbType.Binary;
                get0Param2.ParameterName = "@driveId";
                get0Command.Parameters.Add(get0Param2);
                var get0Param3 = get0Command.CreateParameter();
                get0Param3.DbType = DbType.Binary;
                get0Param3.ParameterName = "@uniqueId";
                get0Command.Parameters.Add(get0Param3);

                get0Param1.Value = identityId.ToByteArray();
                get0Param2.Value = driveId.ToByteArray();
                get0Param3.Value = uniqueId?.ToByteArray() ?? (object)DBNull.Value;
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr,identityId,driveId,uniqueId);
                        return r;
                    } // using
                } //
            } // using
        }

        protected DriveMainIndexRecord ReadRecordFromReader1(DbDataReader rdr,Guid identityId,Guid driveId,Guid? globalTransitId)
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
            item.senderIdNoLengthCheck = (rdr[10] == DBNull.Value) ? null : (string)rdr[10];
            item.groupId = (rdr[11] == DBNull.Value) ? null : new Guid((byte[])rdr[11]);
            item.uniqueId = (rdr[12] == DBNull.Value) ? null : new Guid((byte[])rdr[12]);
            item.byteCount = (rdr[13] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[13];
            item.hdrEncryptedKeyHeaderNoLengthCheck = (rdr[14] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[14];
            item.hdrVersionTag = (rdr[15] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[15]);
            item.hdrAppDataNoLengthCheck = (rdr[16] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[16];
            item.hdrLocalVersionTag = (rdr[17] == DBNull.Value) ? null : new Guid((byte[])rdr[17]);
            item.hdrLocalAppDataNoLengthCheck = (rdr[18] == DBNull.Value) ? null : (string)rdr[18];
            item.hdrReactionSummaryNoLengthCheck = (rdr[19] == DBNull.Value) ? null : (string)rdr[19];
            item.hdrServerDataNoLengthCheck = (rdr[20] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[20];
            item.hdrTransferHistoryNoLengthCheck = (rdr[21] == DBNull.Value) ? null : (string)rdr[21];
            item.hdrFileMetaDataNoLengthCheck = (rdr[22] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[22];
            item.hdrTmpDriveAlias = (rdr[23] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[23]);
            item.hdrTmpDriveType = (rdr[24] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[24]);
            item.created = (rdr[25] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[25]);
            item.modified = (rdr[26] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[26]);
            return item;
       }

        protected virtual async Task<DriveMainIndexRecord> GetByGlobalTransitIdAsync(Guid identityId,Guid driveId,Guid? globalTransitId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get1Command = cn.CreateCommand();
            {
                get1Command.CommandText = "SELECT rowId,fileId,fileState,requiredSecurityGroup,fileSystemType,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,uniqueId,byteCount,hdrEncryptedKeyHeader,hdrVersionTag,hdrAppData,hdrLocalVersionTag,hdrLocalAppData,hdrReactionSummary,hdrServerData,hdrTransferHistory,hdrFileMetaData,hdrTmpDriveAlias,hdrTmpDriveType,created,modified FROM DriveMainIndex " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND globalTransitId = @globalTransitId LIMIT 1;"+
                                             ";";
                var get1Param1 = get1Command.CreateParameter();
                get1Param1.DbType = DbType.Binary;
                get1Param1.ParameterName = "@identityId";
                get1Command.Parameters.Add(get1Param1);
                var get1Param2 = get1Command.CreateParameter();
                get1Param2.DbType = DbType.Binary;
                get1Param2.ParameterName = "@driveId";
                get1Command.Parameters.Add(get1Param2);
                var get1Param3 = get1Command.CreateParameter();
                get1Param3.DbType = DbType.Binary;
                get1Param3.ParameterName = "@globalTransitId";
                get1Command.Parameters.Add(get1Param3);

                get1Param1.Value = identityId.ToByteArray();
                get1Param2.Value = driveId.ToByteArray();
                get1Param3.Value = globalTransitId?.ToByteArray() ?? (object)DBNull.Value;
                {
                    using (var rdr = await get1Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader1(rdr,identityId,driveId,globalTransitId);
                        return r;
                    } // using
                } //
            } // using
        }

        protected DriveMainIndexRecord ReadRecordFromReader2(DbDataReader rdr,Guid identityId,Guid driveId)
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
            item.senderIdNoLengthCheck = (rdr[11] == DBNull.Value) ? null : (string)rdr[11];
            item.groupId = (rdr[12] == DBNull.Value) ? null : new Guid((byte[])rdr[12]);
            item.uniqueId = (rdr[13] == DBNull.Value) ? null : new Guid((byte[])rdr[13]);
            item.byteCount = (rdr[14] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[14];
            item.hdrEncryptedKeyHeaderNoLengthCheck = (rdr[15] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[15];
            item.hdrVersionTag = (rdr[16] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[16]);
            item.hdrAppDataNoLengthCheck = (rdr[17] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[17];
            item.hdrLocalVersionTag = (rdr[18] == DBNull.Value) ? null : new Guid((byte[])rdr[18]);
            item.hdrLocalAppDataNoLengthCheck = (rdr[19] == DBNull.Value) ? null : (string)rdr[19];
            item.hdrReactionSummaryNoLengthCheck = (rdr[20] == DBNull.Value) ? null : (string)rdr[20];
            item.hdrServerDataNoLengthCheck = (rdr[21] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[21];
            item.hdrTransferHistoryNoLengthCheck = (rdr[22] == DBNull.Value) ? null : (string)rdr[22];
            item.hdrFileMetaDataNoLengthCheck = (rdr[23] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[23];
            item.hdrTmpDriveAlias = (rdr[24] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[24]);
            item.hdrTmpDriveType = (rdr[25] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[25]);
            item.created = (rdr[26] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[26]);
            item.modified = (rdr[27] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[27]);
            return item;
       }

        protected virtual async Task<DriveMainIndexRecord> GetFullRecordAsync(Guid identityId,Guid driveId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get2Command = cn.CreateCommand();
            {
                get2Command.CommandText = "SELECT rowId,fileId,globalTransitId,fileState,requiredSecurityGroup,fileSystemType,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,uniqueId,byteCount,hdrEncryptedKeyHeader,hdrVersionTag,hdrAppData,hdrLocalVersionTag,hdrLocalAppData,hdrReactionSummary,hdrServerData,hdrTransferHistory,hdrFileMetaData,hdrTmpDriveAlias,hdrTmpDriveType,created,modified FROM DriveMainIndex " +
                                             "WHERE identityId = @identityId AND driveId = @driveId LIMIT 1;"+
                                             ";";
                var get2Param1 = get2Command.CreateParameter();
                get2Param1.DbType = DbType.Binary;
                get2Param1.ParameterName = "@identityId";
                get2Command.Parameters.Add(get2Param1);
                var get2Param2 = get2Command.CreateParameter();
                get2Param2.DbType = DbType.Binary;
                get2Param2.ParameterName = "@driveId";
                get2Command.Parameters.Add(get2Param2);

                get2Param1.Value = identityId.ToByteArray();
                get2Param2.Value = driveId.ToByteArray();
                {
                    using (var rdr = await get2Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader2(rdr,identityId,driveId);
                        return r;
                    } // using
                } //
            } // using
        }

        protected DriveMainIndexRecord ReadRecordFromReader3(DbDataReader rdr,Guid identityId,Guid driveId,Guid fileId)
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
            item.senderIdNoLengthCheck = (rdr[10] == DBNull.Value) ? null : (string)rdr[10];
            item.groupId = (rdr[11] == DBNull.Value) ? null : new Guid((byte[])rdr[11]);
            item.uniqueId = (rdr[12] == DBNull.Value) ? null : new Guid((byte[])rdr[12]);
            item.byteCount = (rdr[13] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[13];
            item.hdrEncryptedKeyHeaderNoLengthCheck = (rdr[14] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[14];
            item.hdrVersionTag = (rdr[15] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[15]);
            item.hdrAppDataNoLengthCheck = (rdr[16] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[16];
            item.hdrLocalVersionTag = (rdr[17] == DBNull.Value) ? null : new Guid((byte[])rdr[17]);
            item.hdrLocalAppDataNoLengthCheck = (rdr[18] == DBNull.Value) ? null : (string)rdr[18];
            item.hdrReactionSummaryNoLengthCheck = (rdr[19] == DBNull.Value) ? null : (string)rdr[19];
            item.hdrServerDataNoLengthCheck = (rdr[20] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[20];
            item.hdrTransferHistoryNoLengthCheck = (rdr[21] == DBNull.Value) ? null : (string)rdr[21];
            item.hdrFileMetaDataNoLengthCheck = (rdr[22] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[22];
            item.hdrTmpDriveAlias = (rdr[23] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[23]);
            item.hdrTmpDriveType = (rdr[24] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[24]);
            item.created = (rdr[25] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[25]);
            item.modified = (rdr[26] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[26]);
            return item;
       }

        protected virtual async Task<DriveMainIndexRecord> GetAsync(Guid identityId,Guid driveId,Guid fileId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get3Command = cn.CreateCommand();
            {
                get3Command.CommandText = "SELECT rowId,globalTransitId,fileState,requiredSecurityGroup,fileSystemType,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,uniqueId,byteCount,hdrEncryptedKeyHeader,hdrVersionTag,hdrAppData,hdrLocalVersionTag,hdrLocalAppData,hdrReactionSummary,hdrServerData,hdrTransferHistory,hdrFileMetaData,hdrTmpDriveAlias,hdrTmpDriveType,created,modified FROM DriveMainIndex " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId LIMIT 1;"+
                                             ";";
                var get3Param1 = get3Command.CreateParameter();
                get3Param1.DbType = DbType.Binary;
                get3Param1.ParameterName = "@identityId";
                get3Command.Parameters.Add(get3Param1);
                var get3Param2 = get3Command.CreateParameter();
                get3Param2.DbType = DbType.Binary;
                get3Param2.ParameterName = "@driveId";
                get3Command.Parameters.Add(get3Param2);
                var get3Param3 = get3Command.CreateParameter();
                get3Param3.DbType = DbType.Binary;
                get3Param3.ParameterName = "@fileId";
                get3Command.Parameters.Add(get3Param3);

                get3Param1.Value = identityId.ToByteArray();
                get3Param2.Value = driveId.ToByteArray();
                get3Param3.Value = fileId.ToByteArray();
                {
                    using (var rdr = await get3Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader3(rdr,identityId,driveId,fileId);
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
                var getPaging0Param1 = getPaging0Command.CreateParameter();
                getPaging0Param1.DbType = DbType.Int64;
                getPaging0Param1.ParameterName = "@rowId";
                getPaging0Command.Parameters.Add(getPaging0Param1);
                var getPaging0Param2 = getPaging0Command.CreateParameter();
                getPaging0Param2.DbType = DbType.Int64;
                getPaging0Param2.ParameterName = "@count";
                getPaging0Command.Parameters.Add(getPaging0Param2);

                getPaging0Param1.Value = inCursor;
                getPaging0Param2.Value = count+1;

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
