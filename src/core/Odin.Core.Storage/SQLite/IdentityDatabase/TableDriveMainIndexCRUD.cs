// using System;
// using System.Data;
// using System.Data.Common;
// using System.Collections.Generic;
// using System.Runtime.CompilerServices;
// using System.Threading.Tasks;
// using Odin.Core.Time;
// using Odin.Core.Identity;
// using Odin.Core.Storage.Factory;
// using Odin.Core.Util;
//
// // THIS FILE IS AUTO GENERATED - DO NOT EDIT
//
// namespace Odin.Core.Storage.SQLite.IdentityDatabase
// {
//     public class DriveMainIndexRecord
//     {
//         private Guid _identityId;
//         public Guid identityId
//         {
//            get {
//                    return _identityId;
//                }
//            set {
//                   _identityId = value;
//                }
//         }
//         private Guid _driveId;
//         public Guid driveId
//         {
//            get {
//                    return _driveId;
//                }
//            set {
//                   _driveId = value;
//                }
//         }
//         private Guid _fileId;
//         public Guid fileId
//         {
//            get {
//                    return _fileId;
//                }
//            set {
//                   _fileId = value;
//                }
//         }
//         private Guid? _globalTransitId;
//         public Guid? globalTransitId
//         {
//            get {
//                    return _globalTransitId;
//                }
//            set {
//                   _globalTransitId = value;
//                }
//         }
//         private Int32 _fileState;
//         public Int32 fileState
//         {
//            get {
//                    return _fileState;
//                }
//            set {
//                   _fileState = value;
//                }
//         }
//         private Int32 _requiredSecurityGroup;
//         public Int32 requiredSecurityGroup
//         {
//            get {
//                    return _requiredSecurityGroup;
//                }
//            set {
//                   _requiredSecurityGroup = value;
//                }
//         }
//         private Int32 _fileSystemType;
//         public Int32 fileSystemType
//         {
//            get {
//                    return _fileSystemType;
//                }
//            set {
//                   _fileSystemType = value;
//                }
//         }
//         private UnixTimeUtc _userDate;
//         public UnixTimeUtc userDate
//         {
//            get {
//                    return _userDate;
//                }
//            set {
//                   _userDate = value;
//                }
//         }
//         private Int32 _fileType;
//         public Int32 fileType
//         {
//            get {
//                    return _fileType;
//                }
//            set {
//                   _fileType = value;
//                }
//         }
//         private Int32 _dataType;
//         public Int32 dataType
//         {
//            get {
//                    return _dataType;
//                }
//            set {
//                   _dataType = value;
//                }
//         }
//         private Int32 _archivalStatus;
//         public Int32 archivalStatus
//         {
//            get {
//                    return _archivalStatus;
//                }
//            set {
//                   _archivalStatus = value;
//                }
//         }
//         private Int32 _historyStatus;
//         public Int32 historyStatus
//         {
//            get {
//                    return _historyStatus;
//                }
//            set {
//                   _historyStatus = value;
//                }
//         }
//         private string _senderId;
//         public string senderId
//         {
//            get {
//                    return _senderId;
//                }
//            set {
//                     if (value?.Length < 0) throw new Exception("Too short");
//                     if (value?.Length > 65535) throw new Exception("Too long");
//                   _senderId = value;
//                }
//         }
//         private Guid? _groupId;
//         public Guid? groupId
//         {
//            get {
//                    return _groupId;
//                }
//            set {
//                   _groupId = value;
//                }
//         }
//         private Guid? _uniqueId;
//         public Guid? uniqueId
//         {
//            get {
//                    return _uniqueId;
//                }
//            set {
//                   _uniqueId = value;
//                }
//         }
//         private Int64 _byteCount;
//         public Int64 byteCount
//         {
//            get {
//                    return _byteCount;
//                }
//            set {
//                   _byteCount = value;
//                }
//         }
//         private string _hdrEncryptedKeyHeader;
//         public string hdrEncryptedKeyHeader
//         {
//            get {
//                    return _hdrEncryptedKeyHeader;
//                }
//            set {
//                     if (value == null) throw new Exception("Cannot be null");
//                     if (value?.Length < 16) throw new Exception("Too short");
//                     if (value?.Length > 512) throw new Exception("Too long");
//                   _hdrEncryptedKeyHeader = value;
//                }
//         }
//         private Guid _hdrVersionTag;
//         public Guid hdrVersionTag
//         {
//            get {
//                    return _hdrVersionTag;
//                }
//            set {
//                   _hdrVersionTag = value;
//                }
//         }
//         private string _hdrAppData;
//         public string hdrAppData
//         {
//            get {
//                    return _hdrAppData;
//                }
//            set {
//                     if (value == null) throw new Exception("Cannot be null");
//                     if (value?.Length < 0) throw new Exception("Too short");
//                     if (value?.Length > 65536) throw new Exception("Too long");
//                   _hdrAppData = value;
//                }
//         }
//         private string _hdrReactionSummary;
//         public string hdrReactionSummary
//         {
//            get {
//                    return _hdrReactionSummary;
//                }
//            set {
//                     if (value?.Length < 0) throw new Exception("Too short");
//                     if (value?.Length > 16384) throw new Exception("Too long");
//                   _hdrReactionSummary = value;
//                }
//         }
//         private string _hdrServerData;
//         public string hdrServerData
//         {
//            get {
//                    return _hdrServerData;
//                }
//            set {
//                     if (value == null) throw new Exception("Cannot be null");
//                     if (value?.Length < 0) throw new Exception("Too short");
//                     if (value?.Length > 16384) throw new Exception("Too long");
//                   _hdrServerData = value;
//                }
//         }
//         private string _hdrTransferHistory;
//         public string hdrTransferHistory
//         {
//            get {
//                    return _hdrTransferHistory;
//                }
//            set {
//                     if (value?.Length < 0) throw new Exception("Too short");
//                     if (value?.Length > 16384) throw new Exception("Too long");
//                   _hdrTransferHistory = value;
//                }
//         }
//         private string _hdrFileMetaData;
//         public string hdrFileMetaData
//         {
//            get {
//                    return _hdrFileMetaData;
//                }
//            set {
//                     if (value == null) throw new Exception("Cannot be null");
//                     if (value?.Length < 0) throw new Exception("Too short");
//                     if (value?.Length > 16384) throw new Exception("Too long");
//                   _hdrFileMetaData = value;
//                }
//         }
//         private Guid _hdrTmpDriveAlias;
//         public Guid hdrTmpDriveAlias
//         {
//            get {
//                    return _hdrTmpDriveAlias;
//                }
//            set {
//                   _hdrTmpDriveAlias = value;
//                }
//         }
//         private Guid _hdrTmpDriveType;
//         public Guid hdrTmpDriveType
//         {
//            get {
//                    return _hdrTmpDriveType;
//                }
//            set {
//                   _hdrTmpDriveType = value;
//                }
//         }
//         private UnixTimeUtcUnique _created;
//         public UnixTimeUtcUnique created
//         {
//            get {
//                    return _created;
//                }
//            set {
//                   _created = value;
//                }
//         }
//         private UnixTimeUtcUnique? _modified;
//         public UnixTimeUtcUnique? modified
//         {
//            get {
//                    return _modified;
//                }
//            set {
//                   _modified = value;
//                }
//         }
//     } // End of class DriveMainIndexRecord
//
//     public class TableDriveMainIndexCRUD
//     {
//
//         public TableDriveMainIndexCRUD(CacheHelper cache)
//         {
//         }
//
//
//         public async Task EnsureTableExistsAsync(DatabaseConnection conn, bool dropExisting = false)
//         {
//             using (var cmd = conn.db.CreateCommand())
//             {
//                 if (dropExisting)
//                 {
//                    cmd.CommandText = "DROP TABLE IF EXISTS driveMainIndex;";
//                    await conn.ExecuteNonQueryAsync(cmd);
//                 }
//                 cmd.CommandText =
//                 "CREATE TABLE IF NOT EXISTS driveMainIndex("
//                  +"identityId BLOB NOT NULL, "
//                  +"driveId BLOB NOT NULL, "
//                  +"fileId BLOB NOT NULL, "
//                  +"globalTransitId BLOB , "
//                  +"fileState INT NOT NULL, "
//                  +"requiredSecurityGroup INT NOT NULL, "
//                  +"fileSystemType INT NOT NULL, "
//                  +"userDate INT NOT NULL, "
//                  +"fileType INT NOT NULL, "
//                  +"dataType INT NOT NULL, "
//                  +"archivalStatus INT NOT NULL, "
//                  +"historyStatus INT NOT NULL, "
//                  +"senderId STRING , "
//                  +"groupId BLOB , "
//                  +"uniqueId BLOB , "
//                  +"byteCount INT NOT NULL, "
//                  +"hdrEncryptedKeyHeader STRING NOT NULL, "
//                  +"hdrVersionTag BLOB NOT NULL UNIQUE, "
//                  +"hdrAppData STRING NOT NULL, "
//                  +"hdrReactionSummary STRING , "
//                  +"hdrServerData STRING NOT NULL, "
//                  +"hdrTransferHistory STRING , "
//                  +"hdrFileMetaData STRING NOT NULL, "
//                  +"hdrTmpDriveAlias BLOB NOT NULL, "
//                  +"hdrTmpDriveType BLOB NOT NULL, "
//                  +"created INT NOT NULL, "
//                  +"modified INT  "
//                  +", PRIMARY KEY (identityId,driveId,fileId)"
//                  +", UNIQUE(identityId,driveId,uniqueId)"
//                  +", UNIQUE(identityId,driveId,globalTransitId)"
//                  +");"
//                  +"CREATE INDEX IF NOT EXISTS Idx0TableDriveMainIndexCRUD ON driveMainIndex(identityId,driveId,modified);"
//                  ;
//                  await conn.ExecuteNonQueryAsync(cmd);
//             }
//         }
//
//         internal virtual async Task<int> InsertAsync(DatabaseConnection conn, DriveMainIndexRecord item)
//         {
//             item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
//             item.driveId.AssertGuidNotEmpty("Guid parameter driveId cannot be set to Empty GUID.");
//             item.fileId.AssertGuidNotEmpty("Guid parameter fileId cannot be set to Empty GUID.");
//             item.globalTransitId.AssertGuidNotEmpty("Guid parameter globalTransitId cannot be set to Empty GUID.");
//             item.groupId.AssertGuidNotEmpty("Guid parameter groupId cannot be set to Empty GUID.");
//             item.uniqueId.AssertGuidNotEmpty("Guid parameter uniqueId cannot be set to Empty GUID.");
//             item.hdrVersionTag.AssertGuidNotEmpty("Guid parameter hdrVersionTag cannot be set to Empty GUID.");
//             item.hdrTmpDriveAlias.AssertGuidNotEmpty("Guid parameter hdrTmpDriveAlias cannot be set to Empty GUID.");
//             item.hdrTmpDriveType.AssertGuidNotEmpty("Guid parameter hdrTmpDriveType cannot be set to Empty GUID.");
//             using (var insertCommand = conn.db.CreateCommand())
//             {
//                 insertCommand.CommandText = "INSERT INTO driveMainIndex (identityId,driveId,fileId,globalTransitId,fileState,requiredSecurityGroup,fileSystemType,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,uniqueId,byteCount,hdrEncryptedKeyHeader,hdrVersionTag,hdrAppData,hdrReactionSummary,hdrServerData,hdrTransferHistory,hdrFileMetaData,hdrTmpDriveAlias,hdrTmpDriveType,created,modified) " +
//                                              "VALUES (@identityId,@driveId,@fileId,@globalTransitId,@fileState,@requiredSecurityGroup,@fileSystemType,@userDate,@fileType,@dataType,@archivalStatus,@historyStatus,@senderId,@groupId,@uniqueId,@byteCount,@hdrEncryptedKeyHeader,@hdrVersionTag,@hdrAppData,@hdrReactionSummary,@hdrServerData,@hdrTransferHistory,@hdrFileMetaData,@hdrTmpDriveAlias,@hdrTmpDriveType,@created,@modified)";
//                 var insertParam1 = insertCommand.CreateParameter();
//                 insertParam1.ParameterName = "@identityId";
//                 insertCommand.Parameters.Add(insertParam1);
//                 var insertParam2 = insertCommand.CreateParameter();
//                 insertParam2.ParameterName = "@driveId";
//                 insertCommand.Parameters.Add(insertParam2);
//                 var insertParam3 = insertCommand.CreateParameter();
//                 insertParam3.ParameterName = "@fileId";
//                 insertCommand.Parameters.Add(insertParam3);
//                 var insertParam4 = insertCommand.CreateParameter();
//                 insertParam4.ParameterName = "@globalTransitId";
//                 insertCommand.Parameters.Add(insertParam4);
//                 var insertParam5 = insertCommand.CreateParameter();
//                 insertParam5.ParameterName = "@fileState";
//                 insertCommand.Parameters.Add(insertParam5);
//                 var insertParam6 = insertCommand.CreateParameter();
//                 insertParam6.ParameterName = "@requiredSecurityGroup";
//                 insertCommand.Parameters.Add(insertParam6);
//                 var insertParam7 = insertCommand.CreateParameter();
//                 insertParam7.ParameterName = "@fileSystemType";
//                 insertCommand.Parameters.Add(insertParam7);
//                 var insertParam8 = insertCommand.CreateParameter();
//                 insertParam8.ParameterName = "@userDate";
//                 insertCommand.Parameters.Add(insertParam8);
//                 var insertParam9 = insertCommand.CreateParameter();
//                 insertParam9.ParameterName = "@fileType";
//                 insertCommand.Parameters.Add(insertParam9);
//                 var insertParam10 = insertCommand.CreateParameter();
//                 insertParam10.ParameterName = "@dataType";
//                 insertCommand.Parameters.Add(insertParam10);
//                 var insertParam11 = insertCommand.CreateParameter();
//                 insertParam11.ParameterName = "@archivalStatus";
//                 insertCommand.Parameters.Add(insertParam11);
//                 var insertParam12 = insertCommand.CreateParameter();
//                 insertParam12.ParameterName = "@historyStatus";
//                 insertCommand.Parameters.Add(insertParam12);
//                 var insertParam13 = insertCommand.CreateParameter();
//                 insertParam13.ParameterName = "@senderId";
//                 insertCommand.Parameters.Add(insertParam13);
//                 var insertParam14 = insertCommand.CreateParameter();
//                 insertParam14.ParameterName = "@groupId";
//                 insertCommand.Parameters.Add(insertParam14);
//                 var insertParam15 = insertCommand.CreateParameter();
//                 insertParam15.ParameterName = "@uniqueId";
//                 insertCommand.Parameters.Add(insertParam15);
//                 var insertParam16 = insertCommand.CreateParameter();
//                 insertParam16.ParameterName = "@byteCount";
//                 insertCommand.Parameters.Add(insertParam16);
//                 var insertParam17 = insertCommand.CreateParameter();
//                 insertParam17.ParameterName = "@hdrEncryptedKeyHeader";
//                 insertCommand.Parameters.Add(insertParam17);
//                 var insertParam18 = insertCommand.CreateParameter();
//                 insertParam18.ParameterName = "@hdrVersionTag";
//                 insertCommand.Parameters.Add(insertParam18);
//                 var insertParam19 = insertCommand.CreateParameter();
//                 insertParam19.ParameterName = "@hdrAppData";
//                 insertCommand.Parameters.Add(insertParam19);
//                 var insertParam20 = insertCommand.CreateParameter();
//                 insertParam20.ParameterName = "@hdrReactionSummary";
//                 insertCommand.Parameters.Add(insertParam20);
//                 var insertParam21 = insertCommand.CreateParameter();
//                 insertParam21.ParameterName = "@hdrServerData";
//                 insertCommand.Parameters.Add(insertParam21);
//                 var insertParam22 = insertCommand.CreateParameter();
//                 insertParam22.ParameterName = "@hdrTransferHistory";
//                 insertCommand.Parameters.Add(insertParam22);
//                 var insertParam23 = insertCommand.CreateParameter();
//                 insertParam23.ParameterName = "@hdrFileMetaData";
//                 insertCommand.Parameters.Add(insertParam23);
//                 var insertParam24 = insertCommand.CreateParameter();
//                 insertParam24.ParameterName = "@hdrTmpDriveAlias";
//                 insertCommand.Parameters.Add(insertParam24);
//                 var insertParam25 = insertCommand.CreateParameter();
//                 insertParam25.ParameterName = "@hdrTmpDriveType";
//                 insertCommand.Parameters.Add(insertParam25);
//                 var insertParam26 = insertCommand.CreateParameter();
//                 insertParam26.ParameterName = "@created";
//                 insertCommand.Parameters.Add(insertParam26);
//                 var insertParam27 = insertCommand.CreateParameter();
//                 insertParam27.ParameterName = "@modified";
//                 insertCommand.Parameters.Add(insertParam27);
//                 insertParam1.Value = item.identityId.ToByteArray();
//                 insertParam2.Value = item.driveId.ToByteArray();
//                 insertParam3.Value = item.fileId.ToByteArray();
//                 insertParam4.Value = item.globalTransitId?.ToByteArray() ?? (object)DBNull.Value;
//                 insertParam5.Value = item.fileState;
//                 insertParam6.Value = item.requiredSecurityGroup;
//                 insertParam7.Value = item.fileSystemType;
//                 insertParam8.Value = item.userDate.milliseconds;
//                 insertParam9.Value = item.fileType;
//                 insertParam10.Value = item.dataType;
//                 insertParam11.Value = item.archivalStatus;
//                 insertParam12.Value = item.historyStatus;
//                 insertParam13.Value = item.senderId ?? (object)DBNull.Value;
//                 insertParam14.Value = item.groupId?.ToByteArray() ?? (object)DBNull.Value;
//                 insertParam15.Value = item.uniqueId?.ToByteArray() ?? (object)DBNull.Value;
//                 insertParam16.Value = item.byteCount;
//                 insertParam17.Value = item.hdrEncryptedKeyHeader;
//                 insertParam18.Value = item.hdrVersionTag.ToByteArray();
//                 insertParam19.Value = item.hdrAppData;
//                 insertParam20.Value = item.hdrReactionSummary ?? (object)DBNull.Value;
//                 insertParam21.Value = item.hdrServerData;
//                 insertParam22.Value = item.hdrTransferHistory ?? (object)DBNull.Value;
//                 insertParam23.Value = item.hdrFileMetaData;
//                 insertParam24.Value = item.hdrTmpDriveAlias.ToByteArray();
//                 insertParam25.Value = item.hdrTmpDriveType.ToByteArray();
//                 var now = UnixTimeUtcUnique.Now();
//                 insertParam26.Value = now.uniqueTime;
//                 item.modified = null;
//                 insertParam27.Value = DBNull.Value;
//                 var count = await conn.ExecuteNonQueryAsync(insertCommand);
//                 if (count > 0)
//                 {
//                      item.created = now;
//                 }
//                 return count;
//             }
//         }
//
//         internal virtual async Task<int> TryInsertAsync(DatabaseConnection conn, DriveMainIndexRecord item)
//         {
//             item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
//             item.driveId.AssertGuidNotEmpty("Guid parameter driveId cannot be set to Empty GUID.");
//             item.fileId.AssertGuidNotEmpty("Guid parameter fileId cannot be set to Empty GUID.");
//             item.globalTransitId.AssertGuidNotEmpty("Guid parameter globalTransitId cannot be set to Empty GUID.");
//             item.groupId.AssertGuidNotEmpty("Guid parameter groupId cannot be set to Empty GUID.");
//             item.uniqueId.AssertGuidNotEmpty("Guid parameter uniqueId cannot be set to Empty GUID.");
//             item.hdrVersionTag.AssertGuidNotEmpty("Guid parameter hdrVersionTag cannot be set to Empty GUID.");
//             item.hdrTmpDriveAlias.AssertGuidNotEmpty("Guid parameter hdrTmpDriveAlias cannot be set to Empty GUID.");
//             item.hdrTmpDriveType.AssertGuidNotEmpty("Guid parameter hdrTmpDriveType cannot be set to Empty GUID.");
//             using (var insertCommand = conn.db.CreateCommand())
//             {
//                 insertCommand.CommandText = "INSERT OR IGNORE INTO driveMainIndex (identityId,driveId,fileId,globalTransitId,fileState,requiredSecurityGroup,fileSystemType,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,uniqueId,byteCount,hdrEncryptedKeyHeader,hdrVersionTag,hdrAppData,hdrReactionSummary,hdrServerData,hdrTransferHistory,hdrFileMetaData,hdrTmpDriveAlias,hdrTmpDriveType,created,modified) " +
//                                              "VALUES (@identityId,@driveId,@fileId,@globalTransitId,@fileState,@requiredSecurityGroup,@fileSystemType,@userDate,@fileType,@dataType,@archivalStatus,@historyStatus,@senderId,@groupId,@uniqueId,@byteCount,@hdrEncryptedKeyHeader,@hdrVersionTag,@hdrAppData,@hdrReactionSummary,@hdrServerData,@hdrTransferHistory,@hdrFileMetaData,@hdrTmpDriveAlias,@hdrTmpDriveType,@created,@modified)";
//                 var insertParam1 = insertCommand.CreateParameter();
//                 insertParam1.ParameterName = "@identityId";
//                 insertCommand.Parameters.Add(insertParam1);
//                 var insertParam2 = insertCommand.CreateParameter();
//                 insertParam2.ParameterName = "@driveId";
//                 insertCommand.Parameters.Add(insertParam2);
//                 var insertParam3 = insertCommand.CreateParameter();
//                 insertParam3.ParameterName = "@fileId";
//                 insertCommand.Parameters.Add(insertParam3);
//                 var insertParam4 = insertCommand.CreateParameter();
//                 insertParam4.ParameterName = "@globalTransitId";
//                 insertCommand.Parameters.Add(insertParam4);
//                 var insertParam5 = insertCommand.CreateParameter();
//                 insertParam5.ParameterName = "@fileState";
//                 insertCommand.Parameters.Add(insertParam5);
//                 var insertParam6 = insertCommand.CreateParameter();
//                 insertParam6.ParameterName = "@requiredSecurityGroup";
//                 insertCommand.Parameters.Add(insertParam6);
//                 var insertParam7 = insertCommand.CreateParameter();
//                 insertParam7.ParameterName = "@fileSystemType";
//                 insertCommand.Parameters.Add(insertParam7);
//                 var insertParam8 = insertCommand.CreateParameter();
//                 insertParam8.ParameterName = "@userDate";
//                 insertCommand.Parameters.Add(insertParam8);
//                 var insertParam9 = insertCommand.CreateParameter();
//                 insertParam9.ParameterName = "@fileType";
//                 insertCommand.Parameters.Add(insertParam9);
//                 var insertParam10 = insertCommand.CreateParameter();
//                 insertParam10.ParameterName = "@dataType";
//                 insertCommand.Parameters.Add(insertParam10);
//                 var insertParam11 = insertCommand.CreateParameter();
//                 insertParam11.ParameterName = "@archivalStatus";
//                 insertCommand.Parameters.Add(insertParam11);
//                 var insertParam12 = insertCommand.CreateParameter();
//                 insertParam12.ParameterName = "@historyStatus";
//                 insertCommand.Parameters.Add(insertParam12);
//                 var insertParam13 = insertCommand.CreateParameter();
//                 insertParam13.ParameterName = "@senderId";
//                 insertCommand.Parameters.Add(insertParam13);
//                 var insertParam14 = insertCommand.CreateParameter();
//                 insertParam14.ParameterName = "@groupId";
//                 insertCommand.Parameters.Add(insertParam14);
//                 var insertParam15 = insertCommand.CreateParameter();
//                 insertParam15.ParameterName = "@uniqueId";
//                 insertCommand.Parameters.Add(insertParam15);
//                 var insertParam16 = insertCommand.CreateParameter();
//                 insertParam16.ParameterName = "@byteCount";
//                 insertCommand.Parameters.Add(insertParam16);
//                 var insertParam17 = insertCommand.CreateParameter();
//                 insertParam17.ParameterName = "@hdrEncryptedKeyHeader";
//                 insertCommand.Parameters.Add(insertParam17);
//                 var insertParam18 = insertCommand.CreateParameter();
//                 insertParam18.ParameterName = "@hdrVersionTag";
//                 insertCommand.Parameters.Add(insertParam18);
//                 var insertParam19 = insertCommand.CreateParameter();
//                 insertParam19.ParameterName = "@hdrAppData";
//                 insertCommand.Parameters.Add(insertParam19);
//                 var insertParam20 = insertCommand.CreateParameter();
//                 insertParam20.ParameterName = "@hdrReactionSummary";
//                 insertCommand.Parameters.Add(insertParam20);
//                 var insertParam21 = insertCommand.CreateParameter();
//                 insertParam21.ParameterName = "@hdrServerData";
//                 insertCommand.Parameters.Add(insertParam21);
//                 var insertParam22 = insertCommand.CreateParameter();
//                 insertParam22.ParameterName = "@hdrTransferHistory";
//                 insertCommand.Parameters.Add(insertParam22);
//                 var insertParam23 = insertCommand.CreateParameter();
//                 insertParam23.ParameterName = "@hdrFileMetaData";
//                 insertCommand.Parameters.Add(insertParam23);
//                 var insertParam24 = insertCommand.CreateParameter();
//                 insertParam24.ParameterName = "@hdrTmpDriveAlias";
//                 insertCommand.Parameters.Add(insertParam24);
//                 var insertParam25 = insertCommand.CreateParameter();
//                 insertParam25.ParameterName = "@hdrTmpDriveType";
//                 insertCommand.Parameters.Add(insertParam25);
//                 var insertParam26 = insertCommand.CreateParameter();
//                 insertParam26.ParameterName = "@created";
//                 insertCommand.Parameters.Add(insertParam26);
//                 var insertParam27 = insertCommand.CreateParameter();
//                 insertParam27.ParameterName = "@modified";
//                 insertCommand.Parameters.Add(insertParam27);
//                 insertParam1.Value = item.identityId.ToByteArray();
//                 insertParam2.Value = item.driveId.ToByteArray();
//                 insertParam3.Value = item.fileId.ToByteArray();
//                 insertParam4.Value = item.globalTransitId?.ToByteArray() ?? (object)DBNull.Value;
//                 insertParam5.Value = item.fileState;
//                 insertParam6.Value = item.requiredSecurityGroup;
//                 insertParam7.Value = item.fileSystemType;
//                 insertParam8.Value = item.userDate.milliseconds;
//                 insertParam9.Value = item.fileType;
//                 insertParam10.Value = item.dataType;
//                 insertParam11.Value = item.archivalStatus;
//                 insertParam12.Value = item.historyStatus;
//                 insertParam13.Value = item.senderId ?? (object)DBNull.Value;
//                 insertParam14.Value = item.groupId?.ToByteArray() ?? (object)DBNull.Value;
//                 insertParam15.Value = item.uniqueId?.ToByteArray() ?? (object)DBNull.Value;
//                 insertParam16.Value = item.byteCount;
//                 insertParam17.Value = item.hdrEncryptedKeyHeader;
//                 insertParam18.Value = item.hdrVersionTag.ToByteArray();
//                 insertParam19.Value = item.hdrAppData;
//                 insertParam20.Value = item.hdrReactionSummary ?? (object)DBNull.Value;
//                 insertParam21.Value = item.hdrServerData;
//                 insertParam22.Value = item.hdrTransferHistory ?? (object)DBNull.Value;
//                 insertParam23.Value = item.hdrFileMetaData;
//                 insertParam24.Value = item.hdrTmpDriveAlias.ToByteArray();
//                 insertParam25.Value = item.hdrTmpDriveType.ToByteArray();
//                 var now = UnixTimeUtcUnique.Now();
//                 insertParam26.Value = now.uniqueTime;
//                 item.modified = null;
//                 insertParam27.Value = DBNull.Value;
//                 var count = await conn.ExecuteNonQueryAsync(insertCommand);
//                 if (count > 0)
//                 {
//                     item.created = now;
//                 }
//                 return count;
//             }
//         }
//
//         internal virtual async Task<int> UpsertAsync(DatabaseConnection conn, DriveMainIndexRecord item)
//         {
//             item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
//             item.driveId.AssertGuidNotEmpty("Guid parameter driveId cannot be set to Empty GUID.");
//             item.fileId.AssertGuidNotEmpty("Guid parameter fileId cannot be set to Empty GUID.");
//             item.globalTransitId.AssertGuidNotEmpty("Guid parameter globalTransitId cannot be set to Empty GUID.");
//             item.groupId.AssertGuidNotEmpty("Guid parameter groupId cannot be set to Empty GUID.");
//             item.uniqueId.AssertGuidNotEmpty("Guid parameter uniqueId cannot be set to Empty GUID.");
//             item.hdrVersionTag.AssertGuidNotEmpty("Guid parameter hdrVersionTag cannot be set to Empty GUID.");
//             item.hdrTmpDriveAlias.AssertGuidNotEmpty("Guid parameter hdrTmpDriveAlias cannot be set to Empty GUID.");
//             item.hdrTmpDriveType.AssertGuidNotEmpty("Guid parameter hdrTmpDriveType cannot be set to Empty GUID.");
//             using (var upsertCommand = conn.db.CreateCommand())
//             {
//                 upsertCommand.CommandText = "INSERT INTO driveMainIndex (identityId,driveId,fileId,globalTransitId,fileState,requiredSecurityGroup,fileSystemType,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,uniqueId,byteCount,hdrEncryptedKeyHeader,hdrVersionTag,hdrAppData,hdrReactionSummary,hdrServerData,hdrTransferHistory,hdrFileMetaData,hdrTmpDriveAlias,hdrTmpDriveType,created) " +
//                                              "VALUES (@identityId,@driveId,@fileId,@globalTransitId,@fileState,@requiredSecurityGroup,@fileSystemType,@userDate,@fileType,@dataType,@archivalStatus,@historyStatus,@senderId,@groupId,@uniqueId,@byteCount,@hdrEncryptedKeyHeader,@hdrVersionTag,@hdrAppData,@hdrReactionSummary,@hdrServerData,@hdrTransferHistory,@hdrFileMetaData,@hdrTmpDriveAlias,@hdrTmpDriveType,@created)"+
//                                              "ON CONFLICT (identityId,driveId,fileId) DO UPDATE "+
//                                              "SET globalTransitId = @globalTransitId,fileState = @fileState,requiredSecurityGroup = @requiredSecurityGroup,fileSystemType = @fileSystemType,userDate = @userDate,fileType = @fileType,dataType = @dataType,archivalStatus = @archivalStatus,historyStatus = @historyStatus,senderId = @senderId,groupId = @groupId,uniqueId = @uniqueId,byteCount = @byteCount,hdrEncryptedKeyHeader = @hdrEncryptedKeyHeader,hdrVersionTag = @hdrVersionTag,hdrAppData = @hdrAppData,hdrReactionSummary = @hdrReactionSummary,hdrServerData = @hdrServerData,hdrTransferHistory = @hdrTransferHistory,hdrFileMetaData = @hdrFileMetaData,hdrTmpDriveAlias = @hdrTmpDriveAlias,hdrTmpDriveType = @hdrTmpDriveType,modified = @modified "+
//                                              "RETURNING created, modified;";
//                 var upsertParam1 = upsertCommand.CreateParameter();
//                 upsertParam1.ParameterName = "@identityId";
//                 upsertCommand.Parameters.Add(upsertParam1);
//                 var upsertParam2 = upsertCommand.CreateParameter();
//                 upsertParam2.ParameterName = "@driveId";
//                 upsertCommand.Parameters.Add(upsertParam2);
//                 var upsertParam3 = upsertCommand.CreateParameter();
//                 upsertParam3.ParameterName = "@fileId";
//                 upsertCommand.Parameters.Add(upsertParam3);
//                 var upsertParam4 = upsertCommand.CreateParameter();
//                 upsertParam4.ParameterName = "@globalTransitId";
//                 upsertCommand.Parameters.Add(upsertParam4);
//                 var upsertParam5 = upsertCommand.CreateParameter();
//                 upsertParam5.ParameterName = "@fileState";
//                 upsertCommand.Parameters.Add(upsertParam5);
//                 var upsertParam6 = upsertCommand.CreateParameter();
//                 upsertParam6.ParameterName = "@requiredSecurityGroup";
//                 upsertCommand.Parameters.Add(upsertParam6);
//                 var upsertParam7 = upsertCommand.CreateParameter();
//                 upsertParam7.ParameterName = "@fileSystemType";
//                 upsertCommand.Parameters.Add(upsertParam7);
//                 var upsertParam8 = upsertCommand.CreateParameter();
//                 upsertParam8.ParameterName = "@userDate";
//                 upsertCommand.Parameters.Add(upsertParam8);
//                 var upsertParam9 = upsertCommand.CreateParameter();
//                 upsertParam9.ParameterName = "@fileType";
//                 upsertCommand.Parameters.Add(upsertParam9);
//                 var upsertParam10 = upsertCommand.CreateParameter();
//                 upsertParam10.ParameterName = "@dataType";
//                 upsertCommand.Parameters.Add(upsertParam10);
//                 var upsertParam11 = upsertCommand.CreateParameter();
//                 upsertParam11.ParameterName = "@archivalStatus";
//                 upsertCommand.Parameters.Add(upsertParam11);
//                 var upsertParam12 = upsertCommand.CreateParameter();
//                 upsertParam12.ParameterName = "@historyStatus";
//                 upsertCommand.Parameters.Add(upsertParam12);
//                 var upsertParam13 = upsertCommand.CreateParameter();
//                 upsertParam13.ParameterName = "@senderId";
//                 upsertCommand.Parameters.Add(upsertParam13);
//                 var upsertParam14 = upsertCommand.CreateParameter();
//                 upsertParam14.ParameterName = "@groupId";
//                 upsertCommand.Parameters.Add(upsertParam14);
//                 var upsertParam15 = upsertCommand.CreateParameter();
//                 upsertParam15.ParameterName = "@uniqueId";
//                 upsertCommand.Parameters.Add(upsertParam15);
//                 var upsertParam16 = upsertCommand.CreateParameter();
//                 upsertParam16.ParameterName = "@byteCount";
//                 upsertCommand.Parameters.Add(upsertParam16);
//                 var upsertParam17 = upsertCommand.CreateParameter();
//                 upsertParam17.ParameterName = "@hdrEncryptedKeyHeader";
//                 upsertCommand.Parameters.Add(upsertParam17);
//                 var upsertParam18 = upsertCommand.CreateParameter();
//                 upsertParam18.ParameterName = "@hdrVersionTag";
//                 upsertCommand.Parameters.Add(upsertParam18);
//                 var upsertParam19 = upsertCommand.CreateParameter();
//                 upsertParam19.ParameterName = "@hdrAppData";
//                 upsertCommand.Parameters.Add(upsertParam19);
//                 var upsertParam20 = upsertCommand.CreateParameter();
//                 upsertParam20.ParameterName = "@hdrReactionSummary";
//                 upsertCommand.Parameters.Add(upsertParam20);
//                 var upsertParam21 = upsertCommand.CreateParameter();
//                 upsertParam21.ParameterName = "@hdrServerData";
//                 upsertCommand.Parameters.Add(upsertParam21);
//                 var upsertParam22 = upsertCommand.CreateParameter();
//                 upsertParam22.ParameterName = "@hdrTransferHistory";
//                 upsertCommand.Parameters.Add(upsertParam22);
//                 var upsertParam23 = upsertCommand.CreateParameter();
//                 upsertParam23.ParameterName = "@hdrFileMetaData";
//                 upsertCommand.Parameters.Add(upsertParam23);
//                 var upsertParam24 = upsertCommand.CreateParameter();
//                 upsertParam24.ParameterName = "@hdrTmpDriveAlias";
//                 upsertCommand.Parameters.Add(upsertParam24);
//                 var upsertParam25 = upsertCommand.CreateParameter();
//                 upsertParam25.ParameterName = "@hdrTmpDriveType";
//                 upsertCommand.Parameters.Add(upsertParam25);
//                 var upsertParam26 = upsertCommand.CreateParameter();
//                 upsertParam26.ParameterName = "@created";
//                 upsertCommand.Parameters.Add(upsertParam26);
//                 var upsertParam27 = upsertCommand.CreateParameter();
//                 upsertParam27.ParameterName = "@modified";
//                 upsertCommand.Parameters.Add(upsertParam27);
//                 var now = UnixTimeUtcUnique.Now();
//                 upsertParam1.Value = item.identityId.ToByteArray();
//                 upsertParam2.Value = item.driveId.ToByteArray();
//                 upsertParam3.Value = item.fileId.ToByteArray();
//                 upsertParam4.Value = item.globalTransitId?.ToByteArray() ?? (object)DBNull.Value;
//                 upsertParam5.Value = item.fileState;
//                 upsertParam6.Value = item.requiredSecurityGroup;
//                 upsertParam7.Value = item.fileSystemType;
//                 upsertParam8.Value = item.userDate.milliseconds;
//                 upsertParam9.Value = item.fileType;
//                 upsertParam10.Value = item.dataType;
//                 upsertParam11.Value = item.archivalStatus;
//                 upsertParam12.Value = item.historyStatus;
//                 upsertParam13.Value = item.senderId ?? (object)DBNull.Value;
//                 upsertParam14.Value = item.groupId?.ToByteArray() ?? (object)DBNull.Value;
//                 upsertParam15.Value = item.uniqueId?.ToByteArray() ?? (object)DBNull.Value;
//                 upsertParam16.Value = item.byteCount;
//                 upsertParam17.Value = item.hdrEncryptedKeyHeader;
//                 upsertParam18.Value = item.hdrVersionTag.ToByteArray();
//                 upsertParam19.Value = item.hdrAppData;
//                 upsertParam20.Value = item.hdrReactionSummary ?? (object)DBNull.Value;
//                 upsertParam21.Value = item.hdrServerData;
//                 upsertParam22.Value = item.hdrTransferHistory ?? (object)DBNull.Value;
//                 upsertParam23.Value = item.hdrFileMetaData;
//                 upsertParam24.Value = item.hdrTmpDriveAlias.ToByteArray();
//                 upsertParam25.Value = item.hdrTmpDriveType.ToByteArray();
//                 upsertParam26.Value = now.uniqueTime;
//                 upsertParam27.Value = now.uniqueTime;
//                 await using var rdr = await conn.ExecuteReaderAsync(upsertCommand, System.Data.CommandBehavior.SingleRow);
//                 if (await rdr.ReadAsync())
//                 {
//                    long created = rdr.GetInt64(0);
//                    long? modified = rdr.IsDBNull(1) ? null : rdr.GetInt64(1);
//                    item.created = new UnixTimeUtcUnique(created);
//                    if (modified != null)
//                       item.modified = new UnixTimeUtcUnique((long)modified);
//                    else
//                       item.modified = null;
//                    return 1;
//                 }
//                 return 0;
//             }
//         }
//
//         internal virtual async Task<int> UpdateAsync(DatabaseConnection conn, DriveMainIndexRecord item)
//         {
//             item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
//             item.driveId.AssertGuidNotEmpty("Guid parameter driveId cannot be set to Empty GUID.");
//             item.fileId.AssertGuidNotEmpty("Guid parameter fileId cannot be set to Empty GUID.");
//             item.globalTransitId.AssertGuidNotEmpty("Guid parameter globalTransitId cannot be set to Empty GUID.");
//             item.groupId.AssertGuidNotEmpty("Guid parameter groupId cannot be set to Empty GUID.");
//             item.uniqueId.AssertGuidNotEmpty("Guid parameter uniqueId cannot be set to Empty GUID.");
//             item.hdrVersionTag.AssertGuidNotEmpty("Guid parameter hdrVersionTag cannot be set to Empty GUID.");
//             item.hdrTmpDriveAlias.AssertGuidNotEmpty("Guid parameter hdrTmpDriveAlias cannot be set to Empty GUID.");
//             item.hdrTmpDriveType.AssertGuidNotEmpty("Guid parameter hdrTmpDriveType cannot be set to Empty GUID.");
//             using (var updateCommand = conn.db.CreateCommand())
//             {
//                 updateCommand.CommandText = "UPDATE driveMainIndex " +
//                                              "SET globalTransitId = @globalTransitId,fileState = @fileState,requiredSecurityGroup = @requiredSecurityGroup,fileSystemType = @fileSystemType,userDate = @userDate,fileType = @fileType,dataType = @dataType,archivalStatus = @archivalStatus,historyStatus = @historyStatus,senderId = @senderId,groupId = @groupId,uniqueId = @uniqueId,byteCount = @byteCount,hdrEncryptedKeyHeader = @hdrEncryptedKeyHeader,hdrVersionTag = @hdrVersionTag,hdrAppData = @hdrAppData,hdrReactionSummary = @hdrReactionSummary,hdrServerData = @hdrServerData,hdrTransferHistory = @hdrTransferHistory,hdrFileMetaData = @hdrFileMetaData,hdrTmpDriveAlias = @hdrTmpDriveAlias,hdrTmpDriveType = @hdrTmpDriveType,modified = @modified "+
//                                              "WHERE (identityId = @identityId AND driveId = @driveId AND fileId = @fileId)";
//                 var updateParam1 = updateCommand.CreateParameter();
//                 updateParam1.ParameterName = "@identityId";
//                 updateCommand.Parameters.Add(updateParam1);
//                 var updateParam2 = updateCommand.CreateParameter();
//                 updateParam2.ParameterName = "@driveId";
//                 updateCommand.Parameters.Add(updateParam2);
//                 var updateParam3 = updateCommand.CreateParameter();
//                 updateParam3.ParameterName = "@fileId";
//                 updateCommand.Parameters.Add(updateParam3);
//                 var updateParam4 = updateCommand.CreateParameter();
//                 updateParam4.ParameterName = "@globalTransitId";
//                 updateCommand.Parameters.Add(updateParam4);
//                 var updateParam5 = updateCommand.CreateParameter();
//                 updateParam5.ParameterName = "@fileState";
//                 updateCommand.Parameters.Add(updateParam5);
//                 var updateParam6 = updateCommand.CreateParameter();
//                 updateParam6.ParameterName = "@requiredSecurityGroup";
//                 updateCommand.Parameters.Add(updateParam6);
//                 var updateParam7 = updateCommand.CreateParameter();
//                 updateParam7.ParameterName = "@fileSystemType";
//                 updateCommand.Parameters.Add(updateParam7);
//                 var updateParam8 = updateCommand.CreateParameter();
//                 updateParam8.ParameterName = "@userDate";
//                 updateCommand.Parameters.Add(updateParam8);
//                 var updateParam9 = updateCommand.CreateParameter();
//                 updateParam9.ParameterName = "@fileType";
//                 updateCommand.Parameters.Add(updateParam9);
//                 var updateParam10 = updateCommand.CreateParameter();
//                 updateParam10.ParameterName = "@dataType";
//                 updateCommand.Parameters.Add(updateParam10);
//                 var updateParam11 = updateCommand.CreateParameter();
//                 updateParam11.ParameterName = "@archivalStatus";
//                 updateCommand.Parameters.Add(updateParam11);
//                 var updateParam12 = updateCommand.CreateParameter();
//                 updateParam12.ParameterName = "@historyStatus";
//                 updateCommand.Parameters.Add(updateParam12);
//                 var updateParam13 = updateCommand.CreateParameter();
//                 updateParam13.ParameterName = "@senderId";
//                 updateCommand.Parameters.Add(updateParam13);
//                 var updateParam14 = updateCommand.CreateParameter();
//                 updateParam14.ParameterName = "@groupId";
//                 updateCommand.Parameters.Add(updateParam14);
//                 var updateParam15 = updateCommand.CreateParameter();
//                 updateParam15.ParameterName = "@uniqueId";
//                 updateCommand.Parameters.Add(updateParam15);
//                 var updateParam16 = updateCommand.CreateParameter();
//                 updateParam16.ParameterName = "@byteCount";
//                 updateCommand.Parameters.Add(updateParam16);
//                 var updateParam17 = updateCommand.CreateParameter();
//                 updateParam17.ParameterName = "@hdrEncryptedKeyHeader";
//                 updateCommand.Parameters.Add(updateParam17);
//                 var updateParam18 = updateCommand.CreateParameter();
//                 updateParam18.ParameterName = "@hdrVersionTag";
//                 updateCommand.Parameters.Add(updateParam18);
//                 var updateParam19 = updateCommand.CreateParameter();
//                 updateParam19.ParameterName = "@hdrAppData";
//                 updateCommand.Parameters.Add(updateParam19);
//                 var updateParam20 = updateCommand.CreateParameter();
//                 updateParam20.ParameterName = "@hdrReactionSummary";
//                 updateCommand.Parameters.Add(updateParam20);
//                 var updateParam21 = updateCommand.CreateParameter();
//                 updateParam21.ParameterName = "@hdrServerData";
//                 updateCommand.Parameters.Add(updateParam21);
//                 var updateParam22 = updateCommand.CreateParameter();
//                 updateParam22.ParameterName = "@hdrTransferHistory";
//                 updateCommand.Parameters.Add(updateParam22);
//                 var updateParam23 = updateCommand.CreateParameter();
//                 updateParam23.ParameterName = "@hdrFileMetaData";
//                 updateCommand.Parameters.Add(updateParam23);
//                 var updateParam24 = updateCommand.CreateParameter();
//                 updateParam24.ParameterName = "@hdrTmpDriveAlias";
//                 updateCommand.Parameters.Add(updateParam24);
//                 var updateParam25 = updateCommand.CreateParameter();
//                 updateParam25.ParameterName = "@hdrTmpDriveType";
//                 updateCommand.Parameters.Add(updateParam25);
//                 var updateParam26 = updateCommand.CreateParameter();
//                 updateParam26.ParameterName = "@created";
//                 updateCommand.Parameters.Add(updateParam26);
//                 var updateParam27 = updateCommand.CreateParameter();
//                 updateParam27.ParameterName = "@modified";
//                 updateCommand.Parameters.Add(updateParam27);
//                 var now = UnixTimeUtcUnique.Now();
//                 updateParam1.Value = item.identityId.ToByteArray();
//                 updateParam2.Value = item.driveId.ToByteArray();
//                 updateParam3.Value = item.fileId.ToByteArray();
//                 updateParam4.Value = item.globalTransitId?.ToByteArray() ?? (object)DBNull.Value;
//                 updateParam5.Value = item.fileState;
//                 updateParam6.Value = item.requiredSecurityGroup;
//                 updateParam7.Value = item.fileSystemType;
//                 updateParam8.Value = item.userDate.milliseconds;
//                 updateParam9.Value = item.fileType;
//                 updateParam10.Value = item.dataType;
//                 updateParam11.Value = item.archivalStatus;
//                 updateParam12.Value = item.historyStatus;
//                 updateParam13.Value = item.senderId ?? (object)DBNull.Value;
//                 updateParam14.Value = item.groupId?.ToByteArray() ?? (object)DBNull.Value;
//                 updateParam15.Value = item.uniqueId?.ToByteArray() ?? (object)DBNull.Value;
//                 updateParam16.Value = item.byteCount;
//                 updateParam17.Value = item.hdrEncryptedKeyHeader;
//                 updateParam18.Value = item.hdrVersionTag.ToByteArray();
//                 updateParam19.Value = item.hdrAppData;
//                 updateParam20.Value = item.hdrReactionSummary ?? (object)DBNull.Value;
//                 updateParam21.Value = item.hdrServerData;
//                 updateParam22.Value = item.hdrTransferHistory ?? (object)DBNull.Value;
//                 updateParam23.Value = item.hdrFileMetaData;
//                 updateParam24.Value = item.hdrTmpDriveAlias.ToByteArray();
//                 updateParam25.Value = item.hdrTmpDriveType.ToByteArray();
//                 updateParam26.Value = now.uniqueTime;
//                 updateParam27.Value = now.uniqueTime;
//                 var count = await conn.ExecuteNonQueryAsync(updateCommand);
//                 if (count > 0)
//                 {
//                      item.modified = now;
//                 }
//                 return count;
//             }
//         }
//
//         internal virtual async Task<int> GetCountDirtyAsync(DatabaseConnection conn)
//         {
//             using (var getCountCommand = conn.db.CreateCommand())
//             {
//                  // TODO: this is SQLite specific
//                 getCountCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM driveMainIndex; PRAGMA read_uncommitted = 0;";
//                 var count = await conn.ExecuteScalarAsync(getCountCommand);
//                 if (count == null || count == DBNull.Value || !(count is int || count is long))
//                     return -1;
//                 else
//                     return Convert.ToInt32(count);
//             }
//         }
//
//         public List<string> GetColumnNames()
//         {
//             var sl = new List<string>();
//             sl.Add("identityId");
//             sl.Add("driveId");
//             sl.Add("fileId");
//             sl.Add("globalTransitId");
//             sl.Add("fileState");
//             sl.Add("requiredSecurityGroup");
//             sl.Add("fileSystemType");
//             sl.Add("userDate");
//             sl.Add("fileType");
//             sl.Add("dataType");
//             sl.Add("archivalStatus");
//             sl.Add("historyStatus");
//             sl.Add("senderId");
//             sl.Add("groupId");
//             sl.Add("uniqueId");
//             sl.Add("byteCount");
//             sl.Add("hdrEncryptedKeyHeader");
//             sl.Add("hdrVersionTag");
//             sl.Add("hdrAppData");
//             sl.Add("hdrReactionSummary");
//             sl.Add("hdrServerData");
//             sl.Add("hdrTransferHistory");
//             sl.Add("hdrFileMetaData");
//             sl.Add("hdrTmpDriveAlias");
//             sl.Add("hdrTmpDriveType");
//             sl.Add("created");
//             sl.Add("modified");
//             return sl;
//         }
//
//         internal virtual async Task<int> GetDriveCountDirtyAsync(DatabaseConnection conn, Guid driveId)
//         {
//             using (var getCountDriveCommand = conn.db.CreateCommand())
//             {
//                 getCountDriveCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM driveMainIndex WHERE driveId = $driveId;PRAGMA read_uncommitted = 0;";
//                 var getCountDriveParam1 = getCountDriveCommand.CreateParameter();
//                 getCountDriveParam1.ParameterName = "$driveId";
//                 getCountDriveCommand.Parameters.Add(getCountDriveParam1);
//                 getCountDriveParam1.Value = driveId.ToByteArray();
//                 var count = await conn.ExecuteScalarAsync(getCountDriveCommand);
//                 if (count == null || count == DBNull.Value || !(count is int || count is long))
//                     return -1;
//                 else
//                     return Convert.ToInt32(count);
//             } // using
//         }
//
//         // SELECT identityId,driveId,fileId,globalTransitId,fileState,requiredSecurityGroup,fileSystemType,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,uniqueId,byteCount,hdrEncryptedKeyHeader,hdrVersionTag,hdrAppData,hdrReactionSummary,hdrServerData,hdrTransferHistory,hdrFileMetaData,hdrTmpDriveAlias,hdrTmpDriveType,created,modified
//         internal DriveMainIndexRecord ReadRecordFromReaderAll(DbDataReader rdr)
//         {
//             var result = new List<DriveMainIndexRecord>();
//             byte[] tmpbuf = new byte[65536+1];
// #pragma warning disable CS0168
//             long bytesRead;
// #pragma warning restore CS0168
//             var guid = new byte[16];
//             var item = new DriveMainIndexRecord();
//
//             if (rdr.IsDBNull(0))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 bytesRead = rdr.GetBytes(0, 0, guid, 0, 16);
//                 if (bytesRead != 16)
//                     throw new Exception("Not a GUID in identityId...");
//                 item.identityId = new Guid(guid);
//             }
//
//             if (rdr.IsDBNull(1))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 bytesRead = rdr.GetBytes(1, 0, guid, 0, 16);
//                 if (bytesRead != 16)
//                     throw new Exception("Not a GUID in driveId...");
//                 item.driveId = new Guid(guid);
//             }
//
//             if (rdr.IsDBNull(2))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 bytesRead = rdr.GetBytes(2, 0, guid, 0, 16);
//                 if (bytesRead != 16)
//                     throw new Exception("Not a GUID in fileId...");
//                 item.fileId = new Guid(guid);
//             }
//
//             if (rdr.IsDBNull(3))
//                 item.globalTransitId = null;
//             else
//             {
//                 bytesRead = rdr.GetBytes(3, 0, guid, 0, 16);
//                 if (bytesRead != 16)
//                     throw new Exception("Not a GUID in globalTransitId...");
//                 item.globalTransitId = new Guid(guid);
//             }
//
//             if (rdr.IsDBNull(4))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.fileState = rdr.GetInt32(4);
//             }
//
//             if (rdr.IsDBNull(5))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.requiredSecurityGroup = rdr.GetInt32(5);
//             }
//
//             if (rdr.IsDBNull(6))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.fileSystemType = rdr.GetInt32(6);
//             }
//
//             if (rdr.IsDBNull(7))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.userDate = new UnixTimeUtc(rdr.GetInt64(7));
//             }
//
//             if (rdr.IsDBNull(8))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.fileType = rdr.GetInt32(8);
//             }
//
//             if (rdr.IsDBNull(9))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.dataType = rdr.GetInt32(9);
//             }
//
//             if (rdr.IsDBNull(10))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.archivalStatus = rdr.GetInt32(10);
//             }
//
//             if (rdr.IsDBNull(11))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.historyStatus = rdr.GetInt32(11);
//             }
//
//             if (rdr.IsDBNull(12))
//                 item.senderId = null;
//             else
//             {
//                 item.senderId = rdr.GetString(12);
//             }
//
//             if (rdr.IsDBNull(13))
//                 item.groupId = null;
//             else
//             {
//                 bytesRead = rdr.GetBytes(13, 0, guid, 0, 16);
//                 if (bytesRead != 16)
//                     throw new Exception("Not a GUID in groupId...");
//                 item.groupId = new Guid(guid);
//             }
//
//             if (rdr.IsDBNull(14))
//                 item.uniqueId = null;
//             else
//             {
//                 bytesRead = rdr.GetBytes(14, 0, guid, 0, 16);
//                 if (bytesRead != 16)
//                     throw new Exception("Not a GUID in uniqueId...");
//                 item.uniqueId = new Guid(guid);
//             }
//
//             if (rdr.IsDBNull(15))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                         item.byteCount = rdr.GetInt64(15);
//             }
//
//             if (rdr.IsDBNull(16))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.hdrEncryptedKeyHeader = rdr.GetString(16);
//             }
//
//             if (rdr.IsDBNull(17))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 bytesRead = rdr.GetBytes(17, 0, guid, 0, 16);
//                 if (bytesRead != 16)
//                     throw new Exception("Not a GUID in hdrVersionTag...");
//                 item.hdrVersionTag = new Guid(guid);
//             }
//
//             if (rdr.IsDBNull(18))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.hdrAppData = rdr.GetString(18);
//             }
//
//             if (rdr.IsDBNull(19))
//                 item.hdrReactionSummary = null;
//             else
//             {
//                 item.hdrReactionSummary = rdr.GetString(19);
//             }
//
//             if (rdr.IsDBNull(20))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.hdrServerData = rdr.GetString(20);
//             }
//
//             if (rdr.IsDBNull(21))
//                 item.hdrTransferHistory = null;
//             else
//             {
//                 item.hdrTransferHistory = rdr.GetString(21);
//             }
//
//             if (rdr.IsDBNull(22))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.hdrFileMetaData = rdr.GetString(22);
//             }
//
//             if (rdr.IsDBNull(23))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 bytesRead = rdr.GetBytes(23, 0, guid, 0, 16);
//                 if (bytesRead != 16)
//                     throw new Exception("Not a GUID in hdrTmpDriveAlias...");
//                 item.hdrTmpDriveAlias = new Guid(guid);
//             }
//
//             if (rdr.IsDBNull(24))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 bytesRead = rdr.GetBytes(24, 0, guid, 0, 16);
//                 if (bytesRead != 16)
//                     throw new Exception("Not a GUID in hdrTmpDriveType...");
//                 item.hdrTmpDriveType = new Guid(guid);
//             }
//
//             if (rdr.IsDBNull(25))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.created = new UnixTimeUtcUnique(rdr.GetInt64(25));
//             }
//
//             if (rdr.IsDBNull(26))
//                 item.modified = null;
//             else
//             {
//                 item.modified = new UnixTimeUtcUnique(rdr.GetInt64(26));
//             }
//             return item;
//        }
//
//         internal async Task<int> DeleteAsync(DatabaseConnection conn, Guid identityId,Guid driveId,Guid fileId)
//         {
//             using (var delete0Command = conn.db.CreateCommand())
//             {
//                 delete0Command.CommandText = "DELETE FROM driveMainIndex " +
//                                              "WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId";
//                 var delete0Param1 = delete0Command.CreateParameter();
//                 delete0Param1.ParameterName = "@identityId";
//                 delete0Command.Parameters.Add(delete0Param1);
//                 var delete0Param2 = delete0Command.CreateParameter();
//                 delete0Param2.ParameterName = "@driveId";
//                 delete0Command.Parameters.Add(delete0Param2);
//                 var delete0Param3 = delete0Command.CreateParameter();
//                 delete0Param3.ParameterName = "@fileId";
//                 delete0Command.Parameters.Add(delete0Param3);
//
//                 delete0Param1.Value = identityId.ToByteArray();
//                 delete0Param2.Value = driveId.ToByteArray();
//                 delete0Param3.Value = fileId.ToByteArray();
//                 var count = await conn.ExecuteNonQueryAsync(delete0Command);
//                 return count;
//             }
//         }
//
//         internal DriveMainIndexRecord ReadRecordFromReader0(DbDataReader rdr, Guid identityId,Guid driveId,Guid? uniqueId)
//         {
//             var result = new List<DriveMainIndexRecord>();
//             byte[] tmpbuf = new byte[65536+1];
// #pragma warning disable CS0168
//             long bytesRead;
// #pragma warning restore CS0168
//             var guid = new byte[16];
//             var item = new DriveMainIndexRecord();
//             item.identityId = identityId;
//             item.driveId = driveId;
//             item.uniqueId = uniqueId;
//
//             if (rdr.IsDBNull(0))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 bytesRead = rdr.GetBytes(0, 0, guid, 0, 16);
//                 if (bytesRead != 16)
//                     throw new Exception("Not a GUID in fileId...");
//                 item.fileId = new Guid(guid);
//             }
//
//             if (rdr.IsDBNull(1))
//                 item.globalTransitId = null;
//             else
//             {
//                 bytesRead = rdr.GetBytes(1, 0, guid, 0, 16);
//                 if (bytesRead != 16)
//                     throw new Exception("Not a GUID in globalTransitId...");
//                 item.globalTransitId = new Guid(guid);
//             }
//
//             if (rdr.IsDBNull(2))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.fileState = rdr.GetInt32(2);
//             }
//
//             if (rdr.IsDBNull(3))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.requiredSecurityGroup = rdr.GetInt32(3);
//             }
//
//             if (rdr.IsDBNull(4))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.fileSystemType = rdr.GetInt32(4);
//             }
//
//             if (rdr.IsDBNull(5))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.userDate = new UnixTimeUtc(rdr.GetInt64(5));
//             }
//
//             if (rdr.IsDBNull(6))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.fileType = rdr.GetInt32(6);
//             }
//
//             if (rdr.IsDBNull(7))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.dataType = rdr.GetInt32(7);
//             }
//
//             if (rdr.IsDBNull(8))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.archivalStatus = rdr.GetInt32(8);
//             }
//
//             if (rdr.IsDBNull(9))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.historyStatus = rdr.GetInt32(9);
//             }
//
//             if (rdr.IsDBNull(10))
//                 item.senderId = null;
//             else
//             {
//                 item.senderId = rdr.GetString(10);
//             }
//
//             if (rdr.IsDBNull(11))
//                 item.groupId = null;
//             else
//             {
//                 bytesRead = rdr.GetBytes(11, 0, guid, 0, 16);
//                 if (bytesRead != 16)
//                     throw new Exception("Not a GUID in groupId...");
//                 item.groupId = new Guid(guid);
//             }
//
//             if (rdr.IsDBNull(12))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                         item.byteCount = rdr.GetInt64(12);
//             }
//
//             if (rdr.IsDBNull(13))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.hdrEncryptedKeyHeader = rdr.GetString(13);
//             }
//
//             if (rdr.IsDBNull(14))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 bytesRead = rdr.GetBytes(14, 0, guid, 0, 16);
//                 if (bytesRead != 16)
//                     throw new Exception("Not a GUID in hdrVersionTag...");
//                 item.hdrVersionTag = new Guid(guid);
//             }
//
//             if (rdr.IsDBNull(15))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.hdrAppData = rdr.GetString(15);
//             }
//
//             if (rdr.IsDBNull(16))
//                 item.hdrReactionSummary = null;
//             else
//             {
//                 item.hdrReactionSummary = rdr.GetString(16);
//             }
//
//             if (rdr.IsDBNull(17))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.hdrServerData = rdr.GetString(17);
//             }
//
//             if (rdr.IsDBNull(18))
//                 item.hdrTransferHistory = null;
//             else
//             {
//                 item.hdrTransferHistory = rdr.GetString(18);
//             }
//
//             if (rdr.IsDBNull(19))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.hdrFileMetaData = rdr.GetString(19);
//             }
//
//             if (rdr.IsDBNull(20))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 bytesRead = rdr.GetBytes(20, 0, guid, 0, 16);
//                 if (bytesRead != 16)
//                     throw new Exception("Not a GUID in hdrTmpDriveAlias...");
//                 item.hdrTmpDriveAlias = new Guid(guid);
//             }
//
//             if (rdr.IsDBNull(21))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 bytesRead = rdr.GetBytes(21, 0, guid, 0, 16);
//                 if (bytesRead != 16)
//                     throw new Exception("Not a GUID in hdrTmpDriveType...");
//                 item.hdrTmpDriveType = new Guid(guid);
//             }
//
//             if (rdr.IsDBNull(22))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.created = new UnixTimeUtcUnique(rdr.GetInt64(22));
//             }
//
//             if (rdr.IsDBNull(23))
//                 item.modified = null;
//             else
//             {
//                 item.modified = new UnixTimeUtcUnique(rdr.GetInt64(23));
//             }
//             return item;
//        }
//
//         internal async Task<DriveMainIndexRecord> GetByUniqueIdAsync(DatabaseConnection conn, Guid identityId,Guid driveId,Guid? uniqueId)
//         {
//             using (var get0Command = conn.db.CreateCommand())
//             {
//                 get0Command.CommandText = "SELECT fileId,globalTransitId,fileState,requiredSecurityGroup,fileSystemType,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,byteCount,hdrEncryptedKeyHeader,hdrVersionTag,hdrAppData,hdrReactionSummary,hdrServerData,hdrTransferHistory,hdrFileMetaData,hdrTmpDriveAlias,hdrTmpDriveType,created,modified FROM driveMainIndex " +
//                                              "WHERE identityId = @identityId AND driveId = @driveId AND uniqueId = @uniqueId LIMIT 1;";
//                 var get0Param1 = get0Command.CreateParameter();
//                 get0Param1.ParameterName = "@identityId";
//                 get0Command.Parameters.Add(get0Param1);
//                 var get0Param2 = get0Command.CreateParameter();
//                 get0Param2.ParameterName = "@driveId";
//                 get0Command.Parameters.Add(get0Param2);
//                 var get0Param3 = get0Command.CreateParameter();
//                 get0Param3.ParameterName = "@uniqueId";
//                 get0Command.Parameters.Add(get0Param3);
//
//                 get0Param1.Value = identityId.ToByteArray();
//                 get0Param2.Value = driveId.ToByteArray();
//                 get0Param3.Value = uniqueId?.ToByteArray() ?? (object)DBNull.Value;
//                 {
//                     using (var rdr = await conn.ExecuteReaderAsync(get0Command, System.Data.CommandBehavior.SingleRow))
//                     {
//                         if (await rdr.ReadAsync() == false)
//                         {
//                             return null;
//                         }
//                         var r = ReadRecordFromReader0(rdr, identityId,driveId,uniqueId);
//                         return r;
//                     } // using
//                 } //
//             } // using
//         }
//
//         internal DriveMainIndexRecord ReadRecordFromReader1(DbDataReader rdr, Guid identityId,Guid driveId,Guid? globalTransitId)
//         {
//             var result = new List<DriveMainIndexRecord>();
//             byte[] tmpbuf = new byte[65536+1];
// #pragma warning disable CS0168
//             long bytesRead;
// #pragma warning restore CS0168
//             var guid = new byte[16];
//             var item = new DriveMainIndexRecord();
//             item.identityId = identityId;
//             item.driveId = driveId;
//             item.globalTransitId = globalTransitId;
//
//             if (rdr.IsDBNull(0))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 bytesRead = rdr.GetBytes(0, 0, guid, 0, 16);
//                 if (bytesRead != 16)
//                     throw new Exception("Not a GUID in fileId...");
//                 item.fileId = new Guid(guid);
//             }
//
//             if (rdr.IsDBNull(1))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.fileState = rdr.GetInt32(1);
//             }
//
//             if (rdr.IsDBNull(2))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.requiredSecurityGroup = rdr.GetInt32(2);
//             }
//
//             if (rdr.IsDBNull(3))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.fileSystemType = rdr.GetInt32(3);
//             }
//
//             if (rdr.IsDBNull(4))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.userDate = new UnixTimeUtc(rdr.GetInt64(4));
//             }
//
//             if (rdr.IsDBNull(5))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.fileType = rdr.GetInt32(5);
//             }
//
//             if (rdr.IsDBNull(6))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.dataType = rdr.GetInt32(6);
//             }
//
//             if (rdr.IsDBNull(7))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.archivalStatus = rdr.GetInt32(7);
//             }
//
//             if (rdr.IsDBNull(8))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.historyStatus = rdr.GetInt32(8);
//             }
//
//             if (rdr.IsDBNull(9))
//                 item.senderId = null;
//             else
//             {
//                 item.senderId = rdr.GetString(9);
//             }
//
//             if (rdr.IsDBNull(10))
//                 item.groupId = null;
//             else
//             {
//                 bytesRead = rdr.GetBytes(10, 0, guid, 0, 16);
//                 if (bytesRead != 16)
//                     throw new Exception("Not a GUID in groupId...");
//                 item.groupId = new Guid(guid);
//             }
//
//             if (rdr.IsDBNull(11))
//                 item.uniqueId = null;
//             else
//             {
//                 bytesRead = rdr.GetBytes(11, 0, guid, 0, 16);
//                 if (bytesRead != 16)
//                     throw new Exception("Not a GUID in uniqueId...");
//                 item.uniqueId = new Guid(guid);
//             }
//
//             if (rdr.IsDBNull(12))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                         item.byteCount = rdr.GetInt64(12);
//             }
//
//             if (rdr.IsDBNull(13))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.hdrEncryptedKeyHeader = rdr.GetString(13);
//             }
//
//             if (rdr.IsDBNull(14))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 bytesRead = rdr.GetBytes(14, 0, guid, 0, 16);
//                 if (bytesRead != 16)
//                     throw new Exception("Not a GUID in hdrVersionTag...");
//                 item.hdrVersionTag = new Guid(guid);
//             }
//
//             if (rdr.IsDBNull(15))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.hdrAppData = rdr.GetString(15);
//             }
//
//             if (rdr.IsDBNull(16))
//                 item.hdrReactionSummary = null;
//             else
//             {
//                 item.hdrReactionSummary = rdr.GetString(16);
//             }
//
//             if (rdr.IsDBNull(17))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.hdrServerData = rdr.GetString(17);
//             }
//
//             if (rdr.IsDBNull(18))
//                 item.hdrTransferHistory = null;
//             else
//             {
//                 item.hdrTransferHistory = rdr.GetString(18);
//             }
//
//             if (rdr.IsDBNull(19))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.hdrFileMetaData = rdr.GetString(19);
//             }
//
//             if (rdr.IsDBNull(20))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 bytesRead = rdr.GetBytes(20, 0, guid, 0, 16);
//                 if (bytesRead != 16)
//                     throw new Exception("Not a GUID in hdrTmpDriveAlias...");
//                 item.hdrTmpDriveAlias = new Guid(guid);
//             }
//
//             if (rdr.IsDBNull(21))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 bytesRead = rdr.GetBytes(21, 0, guid, 0, 16);
//                 if (bytesRead != 16)
//                     throw new Exception("Not a GUID in hdrTmpDriveType...");
//                 item.hdrTmpDriveType = new Guid(guid);
//             }
//
//             if (rdr.IsDBNull(22))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.created = new UnixTimeUtcUnique(rdr.GetInt64(22));
//             }
//
//             if (rdr.IsDBNull(23))
//                 item.modified = null;
//             else
//             {
//                 item.modified = new UnixTimeUtcUnique(rdr.GetInt64(23));
//             }
//             return item;
//        }
//
//         internal async Task<DriveMainIndexRecord> GetByGlobalTransitIdAsync(DatabaseConnection conn, Guid identityId,Guid driveId,Guid? globalTransitId)
//         {
//             using (var get1Command = conn.db.CreateCommand())
//             {
//                 get1Command.CommandText = "SELECT fileId,fileState,requiredSecurityGroup,fileSystemType,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,uniqueId,byteCount,hdrEncryptedKeyHeader,hdrVersionTag,hdrAppData,hdrReactionSummary,hdrServerData,hdrTransferHistory,hdrFileMetaData,hdrTmpDriveAlias,hdrTmpDriveType,created,modified FROM driveMainIndex " +
//                                              "WHERE identityId = @identityId AND driveId = @driveId AND globalTransitId = @globalTransitId LIMIT 1;";
//                 var get1Param1 = get1Command.CreateParameter();
//                 get1Param1.ParameterName = "@identityId";
//                 get1Command.Parameters.Add(get1Param1);
//                 var get1Param2 = get1Command.CreateParameter();
//                 get1Param2.ParameterName = "@driveId";
//                 get1Command.Parameters.Add(get1Param2);
//                 var get1Param3 = get1Command.CreateParameter();
//                 get1Param3.ParameterName = "@globalTransitId";
//                 get1Command.Parameters.Add(get1Param3);
//
//                 get1Param1.Value = identityId.ToByteArray();
//                 get1Param2.Value = driveId.ToByteArray();
//                 get1Param3.Value = globalTransitId?.ToByteArray() ?? (object)DBNull.Value;
//                 {
//                     using (var rdr = await conn.ExecuteReaderAsync(get1Command, System.Data.CommandBehavior.SingleRow))
//                     {
//                         if (await rdr.ReadAsync() == false)
//                         {
//                             return null;
//                         }
//                         var r = ReadRecordFromReader1(rdr, identityId,driveId,globalTransitId);
//                         return r;
//                     } // using
//                 } //
//             } // using
//         }
//
//         internal DriveMainIndexRecord ReadRecordFromReader2(DbDataReader rdr, Guid identityId,Guid driveId,Guid fileId)
//         {
//             var result = new List<DriveMainIndexRecord>();
//             byte[] tmpbuf = new byte[65536+1];
// #pragma warning disable CS0168
//             long bytesRead;
// #pragma warning restore CS0168
//             var guid = new byte[16];
//             var item = new DriveMainIndexRecord();
//             item.identityId = identityId;
//             item.driveId = driveId;
//             item.fileId = fileId;
//
//             if (rdr.IsDBNull(0))
//                 item.globalTransitId = null;
//             else
//             {
//                 bytesRead = rdr.GetBytes(0, 0, guid, 0, 16);
//                 if (bytesRead != 16)
//                     throw new Exception("Not a GUID in globalTransitId...");
//                 item.globalTransitId = new Guid(guid);
//             }
//
//             if (rdr.IsDBNull(1))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.fileState = rdr.GetInt32(1);
//             }
//
//             if (rdr.IsDBNull(2))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.requiredSecurityGroup = rdr.GetInt32(2);
//             }
//
//             if (rdr.IsDBNull(3))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.fileSystemType = rdr.GetInt32(3);
//             }
//
//             if (rdr.IsDBNull(4))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.userDate = new UnixTimeUtc(rdr.GetInt64(4));
//             }
//
//             if (rdr.IsDBNull(5))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.fileType = rdr.GetInt32(5);
//             }
//
//             if (rdr.IsDBNull(6))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.dataType = rdr.GetInt32(6);
//             }
//
//             if (rdr.IsDBNull(7))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.archivalStatus = rdr.GetInt32(7);
//             }
//
//             if (rdr.IsDBNull(8))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.historyStatus = rdr.GetInt32(8);
//             }
//
//             if (rdr.IsDBNull(9))
//                 item.senderId = null;
//             else
//             {
//                 item.senderId = rdr.GetString(9);
//             }
//
//             if (rdr.IsDBNull(10))
//                 item.groupId = null;
//             else
//             {
//                 bytesRead = rdr.GetBytes(10, 0, guid, 0, 16);
//                 if (bytesRead != 16)
//                     throw new Exception("Not a GUID in groupId...");
//                 item.groupId = new Guid(guid);
//             }
//
//             if (rdr.IsDBNull(11))
//                 item.uniqueId = null;
//             else
//             {
//                 bytesRead = rdr.GetBytes(11, 0, guid, 0, 16);
//                 if (bytesRead != 16)
//                     throw new Exception("Not a GUID in uniqueId...");
//                 item.uniqueId = new Guid(guid);
//             }
//
//             if (rdr.IsDBNull(12))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                         item.byteCount = rdr.GetInt64(12);
//             }
//
//             if (rdr.IsDBNull(13))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.hdrEncryptedKeyHeader = rdr.GetString(13);
//             }
//
//             if (rdr.IsDBNull(14))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 bytesRead = rdr.GetBytes(14, 0, guid, 0, 16);
//                 if (bytesRead != 16)
//                     throw new Exception("Not a GUID in hdrVersionTag...");
//                 item.hdrVersionTag = new Guid(guid);
//             }
//
//             if (rdr.IsDBNull(15))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.hdrAppData = rdr.GetString(15);
//             }
//
//             if (rdr.IsDBNull(16))
//                 item.hdrReactionSummary = null;
//             else
//             {
//                 item.hdrReactionSummary = rdr.GetString(16);
//             }
//
//             if (rdr.IsDBNull(17))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.hdrServerData = rdr.GetString(17);
//             }
//
//             if (rdr.IsDBNull(18))
//                 item.hdrTransferHistory = null;
//             else
//             {
//                 item.hdrTransferHistory = rdr.GetString(18);
//             }
//
//             if (rdr.IsDBNull(19))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.hdrFileMetaData = rdr.GetString(19);
//             }
//
//             if (rdr.IsDBNull(20))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 bytesRead = rdr.GetBytes(20, 0, guid, 0, 16);
//                 if (bytesRead != 16)
//                     throw new Exception("Not a GUID in hdrTmpDriveAlias...");
//                 item.hdrTmpDriveAlias = new Guid(guid);
//             }
//
//             if (rdr.IsDBNull(21))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 bytesRead = rdr.GetBytes(21, 0, guid, 0, 16);
//                 if (bytesRead != 16)
//                     throw new Exception("Not a GUID in hdrTmpDriveType...");
//                 item.hdrTmpDriveType = new Guid(guid);
//             }
//
//             if (rdr.IsDBNull(22))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 item.created = new UnixTimeUtcUnique(rdr.GetInt64(22));
//             }
//
//             if (rdr.IsDBNull(23))
//                 item.modified = null;
//             else
//             {
//                 item.modified = new UnixTimeUtcUnique(rdr.GetInt64(23));
//             }
//             return item;
//        }
//
//         internal async Task<DriveMainIndexRecord> GetAsync(DatabaseConnection conn, Guid identityId,Guid driveId,Guid fileId)
//         {
//             using (var get2Command = conn.db.CreateCommand())
//             {
//                 get2Command.CommandText = "SELECT globalTransitId,fileState,requiredSecurityGroup,fileSystemType,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,uniqueId,byteCount,hdrEncryptedKeyHeader,hdrVersionTag,hdrAppData,hdrReactionSummary,hdrServerData,hdrTransferHistory,hdrFileMetaData,hdrTmpDriveAlias,hdrTmpDriveType,created,modified FROM driveMainIndex " +
//                                              "WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId LIMIT 1;";
//                 var get2Param1 = get2Command.CreateParameter();
//                 get2Param1.ParameterName = "@identityId";
//                 get2Command.Parameters.Add(get2Param1);
//                 var get2Param2 = get2Command.CreateParameter();
//                 get2Param2.ParameterName = "@driveId";
//                 get2Command.Parameters.Add(get2Param2);
//                 var get2Param3 = get2Command.CreateParameter();
//                 get2Param3.ParameterName = "@fileId";
//                 get2Command.Parameters.Add(get2Param3);
//
//                 get2Param1.Value = identityId.ToByteArray();
//                 get2Param2.Value = driveId.ToByteArray();
//                 get2Param3.Value = fileId.ToByteArray();
//                 {
//                     using (var rdr = await conn.ExecuteReaderAsync(get2Command, System.Data.CommandBehavior.SingleRow))
//                     {
//                         if (await rdr.ReadAsync() == false)
//                         {
//                             return null;
//                         }
//                         var r = ReadRecordFromReader2(rdr, identityId,driveId,fileId);
//                         return r;
//                     } // using
//                 } //
//             } // using
//         }
//
//     }
// }
