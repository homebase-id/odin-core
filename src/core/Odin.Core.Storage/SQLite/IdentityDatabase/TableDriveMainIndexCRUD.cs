using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;
using Odin.Core.Identity;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class DriveMainIndexRecord
    {
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
                    if (value?.Length < 0) throw new Exception("Too short");
                    if (value?.Length > 65535) throw new Exception("Too long");
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
        private UnixTimeUtcUnique _created;
        public UnixTimeUtcUnique created
        {
           get {
                   return _created;
               }
           set {
                  _created = value;
               }
        }
        private UnixTimeUtcUnique? _modified;
        public UnixTimeUtcUnique? modified
        {
           get {
                   return _modified;
               }
           set {
                  _modified = value;
               }
        }
    } // End of class DriveMainIndexRecord

    public class TableDriveMainIndexCRUD : TableBase
    {
        private bool _disposed = false;

        public TableDriveMainIndexCRUD(IdentityDatabase db, CacheHelper cache) : base(db, "driveMainIndex")
        {
        }

        ~TableDriveMainIndexCRUD()
        {
            if (_disposed == false) throw new Exception("TableDriveMainIndexCRUD Not disposed properly");
        }

        public override void Dispose()
        {
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        public sealed override void EnsureTableExists(DatabaseConnection conn, bool dropExisting = false)
        {
                using (var cmd = _database.CreateCommand())
                {
                    if (dropExisting)
                    {
                       cmd.CommandText = "DROP TABLE IF EXISTS driveMainIndex;";
                       conn.ExecuteNonQuery(cmd);
                    }
                    cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS driveMainIndex("
                     +"identityId BLOB NOT NULL, "
                     +"driveId BLOB NOT NULL, "
                     +"fileId BLOB NOT NULL, "
                     +"globalTransitId BLOB , "
                     +"fileState INT NOT NULL, "
                     +"requiredSecurityGroup INT NOT NULL, "
                     +"fileSystemType INT NOT NULL, "
                     +"userDate INT NOT NULL, "
                     +"fileType INT NOT NULL, "
                     +"dataType INT NOT NULL, "
                     +"archivalStatus INT NOT NULL, "
                     +"historyStatus INT NOT NULL, "
                     +"senderId STRING , "
                     +"groupId BLOB , "
                     +"uniqueId BLOB , "
                     +"byteCount INT NOT NULL, "
                     +"created INT NOT NULL, "
                     +"modified INT  "
                     +", PRIMARY KEY (identityId,driveId,fileId)"
                     +", UNIQUE(identityId,driveId,uniqueId)"
                     +", UNIQUE(identityId,driveId,globalTransitId)"
                     +");"
                     +"CREATE INDEX IF NOT EXISTS Idx0TableDriveMainIndexCRUD ON driveMainIndex(identityId,driveId,modified);"
                     ;
                    conn.ExecuteNonQuery(cmd);
            }
        }

        protected virtual int Insert(DatabaseConnection conn, DriveMainIndexRecord item)
        {
            DatabaseBase.AssertGuidNotEmpty(item.identityId, "Guid parameter identityId cannot be set to Empty GUID.");
            DatabaseBase.AssertGuidNotEmpty(item.driveId, "Guid parameter driveId cannot be set to Empty GUID.");
            DatabaseBase.AssertGuidNotEmpty(item.fileId, "Guid parameter fileId cannot be set to Empty GUID.");
            DatabaseBase.AssertGuidNotEmpty(item.globalTransitId, "Guid parameter globalTransitId cannot be set to Empty GUID.");
            DatabaseBase.AssertGuidNotEmpty(item.groupId, "Guid parameter groupId cannot be set to Empty GUID.");
            DatabaseBase.AssertGuidNotEmpty(item.uniqueId, "Guid parameter uniqueId cannot be set to Empty GUID.");
            using (var _insertCommand = _database.CreateCommand())
            {
                _insertCommand.CommandText = "INSERT INTO driveMainIndex (identityId,driveId,fileId,globalTransitId,fileState,requiredSecurityGroup,fileSystemType,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,uniqueId,byteCount,created,modified) " +
                                             "VALUES (@identityId,@driveId,@fileId,@globalTransitId,@fileState,@requiredSecurityGroup,@fileSystemType,@userDate,@fileType,@dataType,@archivalStatus,@historyStatus,@senderId,@groupId,@uniqueId,@byteCount,@created,@modified)";
                var _insertParam1 = _insertCommand.CreateParameter();
                _insertParam1.ParameterName = "@identityId";
                _insertCommand.Parameters.Add(_insertParam1);
                var _insertParam2 = _insertCommand.CreateParameter();
                _insertParam2.ParameterName = "@driveId";
                _insertCommand.Parameters.Add(_insertParam2);
                var _insertParam3 = _insertCommand.CreateParameter();
                _insertParam3.ParameterName = "@fileId";
                _insertCommand.Parameters.Add(_insertParam3);
                var _insertParam4 = _insertCommand.CreateParameter();
                _insertParam4.ParameterName = "@globalTransitId";
                _insertCommand.Parameters.Add(_insertParam4);
                var _insertParam5 = _insertCommand.CreateParameter();
                _insertParam5.ParameterName = "@fileState";
                _insertCommand.Parameters.Add(_insertParam5);
                var _insertParam6 = _insertCommand.CreateParameter();
                _insertParam6.ParameterName = "@requiredSecurityGroup";
                _insertCommand.Parameters.Add(_insertParam6);
                var _insertParam7 = _insertCommand.CreateParameter();
                _insertParam7.ParameterName = "@fileSystemType";
                _insertCommand.Parameters.Add(_insertParam7);
                var _insertParam8 = _insertCommand.CreateParameter();
                _insertParam8.ParameterName = "@userDate";
                _insertCommand.Parameters.Add(_insertParam8);
                var _insertParam9 = _insertCommand.CreateParameter();
                _insertParam9.ParameterName = "@fileType";
                _insertCommand.Parameters.Add(_insertParam9);
                var _insertParam10 = _insertCommand.CreateParameter();
                _insertParam10.ParameterName = "@dataType";
                _insertCommand.Parameters.Add(_insertParam10);
                var _insertParam11 = _insertCommand.CreateParameter();
                _insertParam11.ParameterName = "@archivalStatus";
                _insertCommand.Parameters.Add(_insertParam11);
                var _insertParam12 = _insertCommand.CreateParameter();
                _insertParam12.ParameterName = "@historyStatus";
                _insertCommand.Parameters.Add(_insertParam12);
                var _insertParam13 = _insertCommand.CreateParameter();
                _insertParam13.ParameterName = "@senderId";
                _insertCommand.Parameters.Add(_insertParam13);
                var _insertParam14 = _insertCommand.CreateParameter();
                _insertParam14.ParameterName = "@groupId";
                _insertCommand.Parameters.Add(_insertParam14);
                var _insertParam15 = _insertCommand.CreateParameter();
                _insertParam15.ParameterName = "@uniqueId";
                _insertCommand.Parameters.Add(_insertParam15);
                var _insertParam16 = _insertCommand.CreateParameter();
                _insertParam16.ParameterName = "@byteCount";
                _insertCommand.Parameters.Add(_insertParam16);
                var _insertParam17 = _insertCommand.CreateParameter();
                _insertParam17.ParameterName = "@created";
                _insertCommand.Parameters.Add(_insertParam17);
                var _insertParam18 = _insertCommand.CreateParameter();
                _insertParam18.ParameterName = "@modified";
                _insertCommand.Parameters.Add(_insertParam18);
                _insertParam1.Value = item.identityId.ToByteArray();
                _insertParam2.Value = item.driveId.ToByteArray();
                _insertParam3.Value = item.fileId.ToByteArray();
                _insertParam4.Value = item.globalTransitId?.ToByteArray() ?? (object)DBNull.Value;
                _insertParam5.Value = item.fileState;
                _insertParam6.Value = item.requiredSecurityGroup;
                _insertParam7.Value = item.fileSystemType;
                _insertParam8.Value = item.userDate.milliseconds;
                _insertParam9.Value = item.fileType;
                _insertParam10.Value = item.dataType;
                _insertParam11.Value = item.archivalStatus;
                _insertParam12.Value = item.historyStatus;
                _insertParam13.Value = item.senderId ?? (object)DBNull.Value;
                _insertParam14.Value = item.groupId?.ToByteArray() ?? (object)DBNull.Value;
                _insertParam15.Value = item.uniqueId?.ToByteArray() ?? (object)DBNull.Value;
                _insertParam16.Value = item.byteCount;
                var now = UnixTimeUtcUnique.Now();
                _insertParam17.Value = now.uniqueTime;
                item.modified = null;
                _insertParam18.Value = DBNull.Value;
                var count = conn.ExecuteNonQuery(_insertCommand);
                if (count > 0)
                {
                     item.created = now;
                }
                return count;
            } // Using
        }

        public virtual int TryInsert(DatabaseConnection conn, DriveMainIndexRecord item)
        {
            DatabaseBase.AssertGuidNotEmpty(item.identityId, "Guid parameter identityId cannot be set to Empty GUID.");
            DatabaseBase.AssertGuidNotEmpty(item.driveId, "Guid parameter driveId cannot be set to Empty GUID.");
            DatabaseBase.AssertGuidNotEmpty(item.fileId, "Guid parameter fileId cannot be set to Empty GUID.");
            DatabaseBase.AssertGuidNotEmpty(item.globalTransitId, "Guid parameter globalTransitId cannot be set to Empty GUID.");
            DatabaseBase.AssertGuidNotEmpty(item.groupId, "Guid parameter groupId cannot be set to Empty GUID.");
            DatabaseBase.AssertGuidNotEmpty(item.uniqueId, "Guid parameter uniqueId cannot be set to Empty GUID.");
            using (var _insertCommand = _database.CreateCommand())
            {
                _insertCommand.CommandText = "INSERT OR IGNORE INTO driveMainIndex (identityId,driveId,fileId,globalTransitId,fileState,requiredSecurityGroup,fileSystemType,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,uniqueId,byteCount,created,modified) " +
                                             "VALUES (@identityId,@driveId,@fileId,@globalTransitId,@fileState,@requiredSecurityGroup,@fileSystemType,@userDate,@fileType,@dataType,@archivalStatus,@historyStatus,@senderId,@groupId,@uniqueId,@byteCount,@created,@modified)";
                var _insertParam1 = _insertCommand.CreateParameter();
                _insertParam1.ParameterName = "@identityId";
                _insertCommand.Parameters.Add(_insertParam1);
                var _insertParam2 = _insertCommand.CreateParameter();
                _insertParam2.ParameterName = "@driveId";
                _insertCommand.Parameters.Add(_insertParam2);
                var _insertParam3 = _insertCommand.CreateParameter();
                _insertParam3.ParameterName = "@fileId";
                _insertCommand.Parameters.Add(_insertParam3);
                var _insertParam4 = _insertCommand.CreateParameter();
                _insertParam4.ParameterName = "@globalTransitId";
                _insertCommand.Parameters.Add(_insertParam4);
                var _insertParam5 = _insertCommand.CreateParameter();
                _insertParam5.ParameterName = "@fileState";
                _insertCommand.Parameters.Add(_insertParam5);
                var _insertParam6 = _insertCommand.CreateParameter();
                _insertParam6.ParameterName = "@requiredSecurityGroup";
                _insertCommand.Parameters.Add(_insertParam6);
                var _insertParam7 = _insertCommand.CreateParameter();
                _insertParam7.ParameterName = "@fileSystemType";
                _insertCommand.Parameters.Add(_insertParam7);
                var _insertParam8 = _insertCommand.CreateParameter();
                _insertParam8.ParameterName = "@userDate";
                _insertCommand.Parameters.Add(_insertParam8);
                var _insertParam9 = _insertCommand.CreateParameter();
                _insertParam9.ParameterName = "@fileType";
                _insertCommand.Parameters.Add(_insertParam9);
                var _insertParam10 = _insertCommand.CreateParameter();
                _insertParam10.ParameterName = "@dataType";
                _insertCommand.Parameters.Add(_insertParam10);
                var _insertParam11 = _insertCommand.CreateParameter();
                _insertParam11.ParameterName = "@archivalStatus";
                _insertCommand.Parameters.Add(_insertParam11);
                var _insertParam12 = _insertCommand.CreateParameter();
                _insertParam12.ParameterName = "@historyStatus";
                _insertCommand.Parameters.Add(_insertParam12);
                var _insertParam13 = _insertCommand.CreateParameter();
                _insertParam13.ParameterName = "@senderId";
                _insertCommand.Parameters.Add(_insertParam13);
                var _insertParam14 = _insertCommand.CreateParameter();
                _insertParam14.ParameterName = "@groupId";
                _insertCommand.Parameters.Add(_insertParam14);
                var _insertParam15 = _insertCommand.CreateParameter();
                _insertParam15.ParameterName = "@uniqueId";
                _insertCommand.Parameters.Add(_insertParam15);
                var _insertParam16 = _insertCommand.CreateParameter();
                _insertParam16.ParameterName = "@byteCount";
                _insertCommand.Parameters.Add(_insertParam16);
                var _insertParam17 = _insertCommand.CreateParameter();
                _insertParam17.ParameterName = "@created";
                _insertCommand.Parameters.Add(_insertParam17);
                var _insertParam18 = _insertCommand.CreateParameter();
                _insertParam18.ParameterName = "@modified";
                _insertCommand.Parameters.Add(_insertParam18);
                _insertParam1.Value = item.identityId.ToByteArray();
                _insertParam2.Value = item.driveId.ToByteArray();
                _insertParam3.Value = item.fileId.ToByteArray();
                _insertParam4.Value = item.globalTransitId?.ToByteArray() ?? (object)DBNull.Value;
                _insertParam5.Value = item.fileState;
                _insertParam6.Value = item.requiredSecurityGroup;
                _insertParam7.Value = item.fileSystemType;
                _insertParam8.Value = item.userDate.milliseconds;
                _insertParam9.Value = item.fileType;
                _insertParam10.Value = item.dataType;
                _insertParam11.Value = item.archivalStatus;
                _insertParam12.Value = item.historyStatus;
                _insertParam13.Value = item.senderId ?? (object)DBNull.Value;
                _insertParam14.Value = item.groupId?.ToByteArray() ?? (object)DBNull.Value;
                _insertParam15.Value = item.uniqueId?.ToByteArray() ?? (object)DBNull.Value;
                _insertParam16.Value = item.byteCount;
                var now = UnixTimeUtcUnique.Now();
                _insertParam17.Value = now.uniqueTime;
                item.modified = null;
                _insertParam18.Value = DBNull.Value;
                var count = conn.ExecuteNonQuery(_insertCommand);
                if (count > 0)
                {
                    item.created = now;
                }
                return count;
            } // Using
        }

        protected virtual int Upsert(DatabaseConnection conn, DriveMainIndexRecord item)
        {
            DatabaseBase.AssertGuidNotEmpty(item.identityId, "Guid parameter identityId cannot be set to Empty GUID.");
            DatabaseBase.AssertGuidNotEmpty(item.driveId, "Guid parameter driveId cannot be set to Empty GUID.");
            DatabaseBase.AssertGuidNotEmpty(item.fileId, "Guid parameter fileId cannot be set to Empty GUID.");
            DatabaseBase.AssertGuidNotEmpty(item.globalTransitId, "Guid parameter globalTransitId cannot be set to Empty GUID.");
            DatabaseBase.AssertGuidNotEmpty(item.groupId, "Guid parameter groupId cannot be set to Empty GUID.");
            DatabaseBase.AssertGuidNotEmpty(item.uniqueId, "Guid parameter uniqueId cannot be set to Empty GUID.");
            using (var _upsertCommand = _database.CreateCommand())
            {
                _upsertCommand.CommandText = "INSERT INTO driveMainIndex (identityId,driveId,fileId,globalTransitId,fileState,requiredSecurityGroup,fileSystemType,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,uniqueId,byteCount,created) " +
                                             "VALUES (@identityId,@driveId,@fileId,@globalTransitId,@fileState,@requiredSecurityGroup,@fileSystemType,@userDate,@fileType,@dataType,@archivalStatus,@historyStatus,@senderId,@groupId,@uniqueId,@byteCount,@created)"+
                                             "ON CONFLICT (identityId,driveId,fileId) DO UPDATE "+
                                             "SET globalTransitId = @globalTransitId,fileState = @fileState,requiredSecurityGroup = @requiredSecurityGroup,fileSystemType = @fileSystemType,userDate = @userDate,fileType = @fileType,dataType = @dataType,archivalStatus = @archivalStatus,historyStatus = @historyStatus,senderId = @senderId,groupId = @groupId,uniqueId = @uniqueId,byteCount = @byteCount,modified = @modified "+
                                             "RETURNING created, modified;";
                var _upsertParam1 = _upsertCommand.CreateParameter();
                _upsertParam1.ParameterName = "@identityId";
                _upsertCommand.Parameters.Add(_upsertParam1);
                var _upsertParam2 = _upsertCommand.CreateParameter();
                _upsertParam2.ParameterName = "@driveId";
                _upsertCommand.Parameters.Add(_upsertParam2);
                var _upsertParam3 = _upsertCommand.CreateParameter();
                _upsertParam3.ParameterName = "@fileId";
                _upsertCommand.Parameters.Add(_upsertParam3);
                var _upsertParam4 = _upsertCommand.CreateParameter();
                _upsertParam4.ParameterName = "@globalTransitId";
                _upsertCommand.Parameters.Add(_upsertParam4);
                var _upsertParam5 = _upsertCommand.CreateParameter();
                _upsertParam5.ParameterName = "@fileState";
                _upsertCommand.Parameters.Add(_upsertParam5);
                var _upsertParam6 = _upsertCommand.CreateParameter();
                _upsertParam6.ParameterName = "@requiredSecurityGroup";
                _upsertCommand.Parameters.Add(_upsertParam6);
                var _upsertParam7 = _upsertCommand.CreateParameter();
                _upsertParam7.ParameterName = "@fileSystemType";
                _upsertCommand.Parameters.Add(_upsertParam7);
                var _upsertParam8 = _upsertCommand.CreateParameter();
                _upsertParam8.ParameterName = "@userDate";
                _upsertCommand.Parameters.Add(_upsertParam8);
                var _upsertParam9 = _upsertCommand.CreateParameter();
                _upsertParam9.ParameterName = "@fileType";
                _upsertCommand.Parameters.Add(_upsertParam9);
                var _upsertParam10 = _upsertCommand.CreateParameter();
                _upsertParam10.ParameterName = "@dataType";
                _upsertCommand.Parameters.Add(_upsertParam10);
                var _upsertParam11 = _upsertCommand.CreateParameter();
                _upsertParam11.ParameterName = "@archivalStatus";
                _upsertCommand.Parameters.Add(_upsertParam11);
                var _upsertParam12 = _upsertCommand.CreateParameter();
                _upsertParam12.ParameterName = "@historyStatus";
                _upsertCommand.Parameters.Add(_upsertParam12);
                var _upsertParam13 = _upsertCommand.CreateParameter();
                _upsertParam13.ParameterName = "@senderId";
                _upsertCommand.Parameters.Add(_upsertParam13);
                var _upsertParam14 = _upsertCommand.CreateParameter();
                _upsertParam14.ParameterName = "@groupId";
                _upsertCommand.Parameters.Add(_upsertParam14);
                var _upsertParam15 = _upsertCommand.CreateParameter();
                _upsertParam15.ParameterName = "@uniqueId";
                _upsertCommand.Parameters.Add(_upsertParam15);
                var _upsertParam16 = _upsertCommand.CreateParameter();
                _upsertParam16.ParameterName = "@byteCount";
                _upsertCommand.Parameters.Add(_upsertParam16);
                var _upsertParam17 = _upsertCommand.CreateParameter();
                _upsertParam17.ParameterName = "@created";
                _upsertCommand.Parameters.Add(_upsertParam17);
                var _upsertParam18 = _upsertCommand.CreateParameter();
                _upsertParam18.ParameterName = "@modified";
                _upsertCommand.Parameters.Add(_upsertParam18);
                var now = UnixTimeUtcUnique.Now();
                _upsertParam1.Value = item.identityId.ToByteArray();
                _upsertParam2.Value = item.driveId.ToByteArray();
                _upsertParam3.Value = item.fileId.ToByteArray();
                _upsertParam4.Value = item.globalTransitId?.ToByteArray() ?? (object)DBNull.Value;
                _upsertParam5.Value = item.fileState;
                _upsertParam6.Value = item.requiredSecurityGroup;
                _upsertParam7.Value = item.fileSystemType;
                _upsertParam8.Value = item.userDate.milliseconds;
                _upsertParam9.Value = item.fileType;
                _upsertParam10.Value = item.dataType;
                _upsertParam11.Value = item.archivalStatus;
                _upsertParam12.Value = item.historyStatus;
                _upsertParam13.Value = item.senderId ?? (object)DBNull.Value;
                _upsertParam14.Value = item.groupId?.ToByteArray() ?? (object)DBNull.Value;
                _upsertParam15.Value = item.uniqueId?.ToByteArray() ?? (object)DBNull.Value;
                _upsertParam16.Value = item.byteCount;
                _upsertParam17.Value = now.uniqueTime;
                _upsertParam18.Value = now.uniqueTime;
                using (SqliteDataReader rdr = conn.ExecuteReader(_upsertCommand, System.Data.CommandBehavior.SingleRow))
                {
                   if (rdr.Read())
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
            } // Using
        }

        protected virtual int Update(DatabaseConnection conn, DriveMainIndexRecord item)
        {
            DatabaseBase.AssertGuidNotEmpty(item.identityId, "Guid parameter identityId cannot be set to Empty GUID.");
            DatabaseBase.AssertGuidNotEmpty(item.driveId, "Guid parameter driveId cannot be set to Empty GUID.");
            DatabaseBase.AssertGuidNotEmpty(item.fileId, "Guid parameter fileId cannot be set to Empty GUID.");
            DatabaseBase.AssertGuidNotEmpty(item.globalTransitId, "Guid parameter globalTransitId cannot be set to Empty GUID.");
            DatabaseBase.AssertGuidNotEmpty(item.groupId, "Guid parameter groupId cannot be set to Empty GUID.");
            DatabaseBase.AssertGuidNotEmpty(item.uniqueId, "Guid parameter uniqueId cannot be set to Empty GUID.");
            using (var _updateCommand = _database.CreateCommand())
            {
                _updateCommand.CommandText = "UPDATE driveMainIndex " +
                                             "SET globalTransitId = @globalTransitId,fileState = @fileState,requiredSecurityGroup = @requiredSecurityGroup,fileSystemType = @fileSystemType,userDate = @userDate,fileType = @fileType,dataType = @dataType,archivalStatus = @archivalStatus,historyStatus = @historyStatus,senderId = @senderId,groupId = @groupId,uniqueId = @uniqueId,byteCount = @byteCount,modified = @modified "+
                                             "WHERE (identityId = @identityId AND driveId = @driveId AND fileId = @fileId)";
                var _updateParam1 = _updateCommand.CreateParameter();
                _updateParam1.ParameterName = "@identityId";
                _updateCommand.Parameters.Add(_updateParam1);
                var _updateParam2 = _updateCommand.CreateParameter();
                _updateParam2.ParameterName = "@driveId";
                _updateCommand.Parameters.Add(_updateParam2);
                var _updateParam3 = _updateCommand.CreateParameter();
                _updateParam3.ParameterName = "@fileId";
                _updateCommand.Parameters.Add(_updateParam3);
                var _updateParam4 = _updateCommand.CreateParameter();
                _updateParam4.ParameterName = "@globalTransitId";
                _updateCommand.Parameters.Add(_updateParam4);
                var _updateParam5 = _updateCommand.CreateParameter();
                _updateParam5.ParameterName = "@fileState";
                _updateCommand.Parameters.Add(_updateParam5);
                var _updateParam6 = _updateCommand.CreateParameter();
                _updateParam6.ParameterName = "@requiredSecurityGroup";
                _updateCommand.Parameters.Add(_updateParam6);
                var _updateParam7 = _updateCommand.CreateParameter();
                _updateParam7.ParameterName = "@fileSystemType";
                _updateCommand.Parameters.Add(_updateParam7);
                var _updateParam8 = _updateCommand.CreateParameter();
                _updateParam8.ParameterName = "@userDate";
                _updateCommand.Parameters.Add(_updateParam8);
                var _updateParam9 = _updateCommand.CreateParameter();
                _updateParam9.ParameterName = "@fileType";
                _updateCommand.Parameters.Add(_updateParam9);
                var _updateParam10 = _updateCommand.CreateParameter();
                _updateParam10.ParameterName = "@dataType";
                _updateCommand.Parameters.Add(_updateParam10);
                var _updateParam11 = _updateCommand.CreateParameter();
                _updateParam11.ParameterName = "@archivalStatus";
                _updateCommand.Parameters.Add(_updateParam11);
                var _updateParam12 = _updateCommand.CreateParameter();
                _updateParam12.ParameterName = "@historyStatus";
                _updateCommand.Parameters.Add(_updateParam12);
                var _updateParam13 = _updateCommand.CreateParameter();
                _updateParam13.ParameterName = "@senderId";
                _updateCommand.Parameters.Add(_updateParam13);
                var _updateParam14 = _updateCommand.CreateParameter();
                _updateParam14.ParameterName = "@groupId";
                _updateCommand.Parameters.Add(_updateParam14);
                var _updateParam15 = _updateCommand.CreateParameter();
                _updateParam15.ParameterName = "@uniqueId";
                _updateCommand.Parameters.Add(_updateParam15);
                var _updateParam16 = _updateCommand.CreateParameter();
                _updateParam16.ParameterName = "@byteCount";
                _updateCommand.Parameters.Add(_updateParam16);
                var _updateParam17 = _updateCommand.CreateParameter();
                _updateParam17.ParameterName = "@created";
                _updateCommand.Parameters.Add(_updateParam17);
                var _updateParam18 = _updateCommand.CreateParameter();
                _updateParam18.ParameterName = "@modified";
                _updateCommand.Parameters.Add(_updateParam18);
             var now = UnixTimeUtcUnique.Now();
                _updateParam1.Value = item.identityId.ToByteArray();
                _updateParam2.Value = item.driveId.ToByteArray();
                _updateParam3.Value = item.fileId.ToByteArray();
                _updateParam4.Value = item.globalTransitId?.ToByteArray() ?? (object)DBNull.Value;
                _updateParam5.Value = item.fileState;
                _updateParam6.Value = item.requiredSecurityGroup;
                _updateParam7.Value = item.fileSystemType;
                _updateParam8.Value = item.userDate.milliseconds;
                _updateParam9.Value = item.fileType;
                _updateParam10.Value = item.dataType;
                _updateParam11.Value = item.archivalStatus;
                _updateParam12.Value = item.historyStatus;
                _updateParam13.Value = item.senderId ?? (object)DBNull.Value;
                _updateParam14.Value = item.groupId?.ToByteArray() ?? (object)DBNull.Value;
                _updateParam15.Value = item.uniqueId?.ToByteArray() ?? (object)DBNull.Value;
                _updateParam16.Value = item.byteCount;
                _updateParam17.Value = now.uniqueTime;
                _updateParam18.Value = now.uniqueTime;
                var count = conn.ExecuteNonQuery(_updateCommand);
                if (count > 0)
                {
                     item.modified = now;
                }
                return count;
            } // Using
        }

        protected virtual int GetCountDirty(DatabaseConnection conn)
        {
            using (var _getCountCommand = _database.CreateCommand())
            {
                _getCountCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM driveMainIndex; PRAGMA read_uncommitted = 0;";
                var count = conn.ExecuteScalar(_getCountCommand);
                if (count == null || count == DBNull.Value || !(count is int || count is long))
                    return -1;
                else
                    return Convert.ToInt32(count);
            }
        }

        public override List<string> GetColumnNames()
        {
            var sl = new List<string>();
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
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        protected virtual int GetDriveCountDirty(DatabaseConnection conn, Guid driveId)
        {
            using (var _getCountDriveCommand = _database.CreateCommand())
            {
                _getCountDriveCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM driveMainIndex WHERE driveId = $driveId;PRAGMA read_uncommitted = 0;";
                var _getCountDriveParam1 = _getCountDriveCommand.CreateParameter();
                _getCountDriveParam1.ParameterName = "$driveId";
                _getCountDriveCommand.Parameters.Add(_getCountDriveParam1);
                _getCountDriveParam1.Value = driveId.ToByteArray();
                var count = conn.ExecuteScalar(_getCountDriveCommand);
                if (count == null || count == DBNull.Value || !(count is int || count is long))
                    return -1;
                else
                    return Convert.ToInt32(count);
            } // using
        }

        // SELECT identityId,driveId,fileId,globalTransitId,fileState,requiredSecurityGroup,fileSystemType,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,uniqueId,byteCount,created,modified
        protected DriveMainIndexRecord ReadRecordFromReaderAll(SqliteDataReader rdr)
        {
            var result = new List<DriveMainIndexRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new DriveMainIndexRecord();

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(0, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in identityId...");
                item.identityId = new Guid(_guid);
            }

            if (rdr.IsDBNull(1))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(1, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in driveId...");
                item.driveId = new Guid(_guid);
            }

            if (rdr.IsDBNull(2))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(2, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in fileId...");
                item.fileId = new Guid(_guid);
            }

            if (rdr.IsDBNull(3))
                item.globalTransitId = null;
            else
            {
                bytesRead = rdr.GetBytes(3, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in globalTransitId...");
                item.globalTransitId = new Guid(_guid);
            }

            if (rdr.IsDBNull(4))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.fileState = rdr.GetInt32(4);
            }

            if (rdr.IsDBNull(5))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.requiredSecurityGroup = rdr.GetInt32(5);
            }

            if (rdr.IsDBNull(6))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.fileSystemType = rdr.GetInt32(6);
            }

            if (rdr.IsDBNull(7))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.userDate = new UnixTimeUtc(rdr.GetInt64(7));
            }

            if (rdr.IsDBNull(8))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.fileType = rdr.GetInt32(8);
            }

            if (rdr.IsDBNull(9))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.dataType = rdr.GetInt32(9);
            }

            if (rdr.IsDBNull(10))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.archivalStatus = rdr.GetInt32(10);
            }

            if (rdr.IsDBNull(11))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.historyStatus = rdr.GetInt32(11);
            }

            if (rdr.IsDBNull(12))
                item.senderId = null;
            else
            {
                item.senderId = rdr.GetString(12);
            }

            if (rdr.IsDBNull(13))
                item.groupId = null;
            else
            {
                bytesRead = rdr.GetBytes(13, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in groupId...");
                item.groupId = new Guid(_guid);
            }

            if (rdr.IsDBNull(14))
                item.uniqueId = null;
            else
            {
                bytesRead = rdr.GetBytes(14, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in uniqueId...");
                item.uniqueId = new Guid(_guid);
            }

            if (rdr.IsDBNull(15))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                        item.byteCount = rdr.GetInt64(15);
            }

            if (rdr.IsDBNull(16))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.created = new UnixTimeUtcUnique(rdr.GetInt64(16));
            }

            if (rdr.IsDBNull(17))
                item.modified = null;
            else
            {
                item.modified = new UnixTimeUtcUnique(rdr.GetInt64(17));
            }
            return item;
       }

        protected int Delete(DatabaseConnection conn, Guid identityId,Guid driveId,Guid fileId)
        {
            using (var _delete0Command = _database.CreateCommand())
            {
                _delete0Command.CommandText = "DELETE FROM driveMainIndex " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId";
                var _delete0Param1 = _delete0Command.CreateParameter();
                _delete0Param1.ParameterName = "@identityId";
                _delete0Command.Parameters.Add(_delete0Param1);
                var _delete0Param2 = _delete0Command.CreateParameter();
                _delete0Param2.ParameterName = "@driveId";
                _delete0Command.Parameters.Add(_delete0Param2);
                var _delete0Param3 = _delete0Command.CreateParameter();
                _delete0Param3.ParameterName = "@fileId";
                _delete0Command.Parameters.Add(_delete0Param3);

                _delete0Param1.Value = identityId.ToByteArray();
                _delete0Param2.Value = driveId.ToByteArray();
                _delete0Param3.Value = fileId.ToByteArray();
                var count = conn.ExecuteNonQuery(_delete0Command);
                return count;
            } // Using
        }

        protected DriveMainIndexRecord ReadRecordFromReader0(SqliteDataReader rdr, Guid identityId,Guid driveId,Guid? uniqueId)
        {
            var result = new List<DriveMainIndexRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new DriveMainIndexRecord();
            item.identityId = identityId;
            item.driveId = driveId;
            item.uniqueId = uniqueId;

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(0, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in fileId...");
                item.fileId = new Guid(_guid);
            }

            if (rdr.IsDBNull(1))
                item.globalTransitId = null;
            else
            {
                bytesRead = rdr.GetBytes(1, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in globalTransitId...");
                item.globalTransitId = new Guid(_guid);
            }

            if (rdr.IsDBNull(2))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.fileState = rdr.GetInt32(2);
            }

            if (rdr.IsDBNull(3))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.requiredSecurityGroup = rdr.GetInt32(3);
            }

            if (rdr.IsDBNull(4))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.fileSystemType = rdr.GetInt32(4);
            }

            if (rdr.IsDBNull(5))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.userDate = new UnixTimeUtc(rdr.GetInt64(5));
            }

            if (rdr.IsDBNull(6))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.fileType = rdr.GetInt32(6);
            }

            if (rdr.IsDBNull(7))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.dataType = rdr.GetInt32(7);
            }

            if (rdr.IsDBNull(8))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.archivalStatus = rdr.GetInt32(8);
            }

            if (rdr.IsDBNull(9))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.historyStatus = rdr.GetInt32(9);
            }

            if (rdr.IsDBNull(10))
                item.senderId = null;
            else
            {
                item.senderId = rdr.GetString(10);
            }

            if (rdr.IsDBNull(11))
                item.groupId = null;
            else
            {
                bytesRead = rdr.GetBytes(11, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in groupId...");
                item.groupId = new Guid(_guid);
            }

            if (rdr.IsDBNull(12))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                        item.byteCount = rdr.GetInt64(12);
            }

            if (rdr.IsDBNull(13))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.created = new UnixTimeUtcUnique(rdr.GetInt64(13));
            }

            if (rdr.IsDBNull(14))
                item.modified = null;
            else
            {
                item.modified = new UnixTimeUtcUnique(rdr.GetInt64(14));
            }
            return item;
       }

        protected DriveMainIndexRecord GetByUniqueId(DatabaseConnection conn, Guid identityId,Guid driveId,Guid? uniqueId)
        {
            using (var _get0Command = _database.CreateCommand())
            {
                _get0Command.CommandText = "SELECT fileId,globalTransitId,fileState,requiredSecurityGroup,fileSystemType,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,byteCount,created,modified FROM driveMainIndex " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND uniqueId = @uniqueId LIMIT 1;";
                var _get0Param1 = _get0Command.CreateParameter();
                _get0Param1.ParameterName = "@identityId";
                _get0Command.Parameters.Add(_get0Param1);
                var _get0Param2 = _get0Command.CreateParameter();
                _get0Param2.ParameterName = "@driveId";
                _get0Command.Parameters.Add(_get0Param2);
                var _get0Param3 = _get0Command.CreateParameter();
                _get0Param3.ParameterName = "@uniqueId";
                _get0Command.Parameters.Add(_get0Param3);

                _get0Param1.Value = identityId.ToByteArray();
                _get0Param2.Value = driveId.ToByteArray();
                _get0Param3.Value = uniqueId?.ToByteArray() ?? (object)DBNull.Value;
                lock (conn._lock)
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_get0Command, System.Data.CommandBehavior.SingleRow))
                    {
                        if (!rdr.Read())
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr, identityId,driveId,uniqueId);
                        return r;
                    } // using
                } // lock
            } // using
        }

        protected DriveMainIndexRecord ReadRecordFromReader1(SqliteDataReader rdr, Guid identityId,Guid driveId,Guid? globalTransitId)
        {
            var result = new List<DriveMainIndexRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new DriveMainIndexRecord();
            item.identityId = identityId;
            item.driveId = driveId;
            item.globalTransitId = globalTransitId;

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(0, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in fileId...");
                item.fileId = new Guid(_guid);
            }

            if (rdr.IsDBNull(1))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.fileState = rdr.GetInt32(1);
            }

            if (rdr.IsDBNull(2))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.requiredSecurityGroup = rdr.GetInt32(2);
            }

            if (rdr.IsDBNull(3))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.fileSystemType = rdr.GetInt32(3);
            }

            if (rdr.IsDBNull(4))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.userDate = new UnixTimeUtc(rdr.GetInt64(4));
            }

            if (rdr.IsDBNull(5))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.fileType = rdr.GetInt32(5);
            }

            if (rdr.IsDBNull(6))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.dataType = rdr.GetInt32(6);
            }

            if (rdr.IsDBNull(7))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.archivalStatus = rdr.GetInt32(7);
            }

            if (rdr.IsDBNull(8))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.historyStatus = rdr.GetInt32(8);
            }

            if (rdr.IsDBNull(9))
                item.senderId = null;
            else
            {
                item.senderId = rdr.GetString(9);
            }

            if (rdr.IsDBNull(10))
                item.groupId = null;
            else
            {
                bytesRead = rdr.GetBytes(10, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in groupId...");
                item.groupId = new Guid(_guid);
            }

            if (rdr.IsDBNull(11))
                item.uniqueId = null;
            else
            {
                bytesRead = rdr.GetBytes(11, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in uniqueId...");
                item.uniqueId = new Guid(_guid);
            }

            if (rdr.IsDBNull(12))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                        item.byteCount = rdr.GetInt64(12);
            }

            if (rdr.IsDBNull(13))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.created = new UnixTimeUtcUnique(rdr.GetInt64(13));
            }

            if (rdr.IsDBNull(14))
                item.modified = null;
            else
            {
                item.modified = new UnixTimeUtcUnique(rdr.GetInt64(14));
            }
            return item;
       }

        protected DriveMainIndexRecord GetByGlobalTransitId(DatabaseConnection conn, Guid identityId,Guid driveId,Guid? globalTransitId)
        {
            using (var _get1Command = _database.CreateCommand())
            {
                _get1Command.CommandText = "SELECT fileId,fileState,requiredSecurityGroup,fileSystemType,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,uniqueId,byteCount,created,modified FROM driveMainIndex " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND globalTransitId = @globalTransitId LIMIT 1;";
                var _get1Param1 = _get1Command.CreateParameter();
                _get1Param1.ParameterName = "@identityId";
                _get1Command.Parameters.Add(_get1Param1);
                var _get1Param2 = _get1Command.CreateParameter();
                _get1Param2.ParameterName = "@driveId";
                _get1Command.Parameters.Add(_get1Param2);
                var _get1Param3 = _get1Command.CreateParameter();
                _get1Param3.ParameterName = "@globalTransitId";
                _get1Command.Parameters.Add(_get1Param3);

                _get1Param1.Value = identityId.ToByteArray();
                _get1Param2.Value = driveId.ToByteArray();
                _get1Param3.Value = globalTransitId?.ToByteArray() ?? (object)DBNull.Value;
                lock (conn._lock)
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_get1Command, System.Data.CommandBehavior.SingleRow))
                    {
                        if (!rdr.Read())
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader1(rdr, identityId,driveId,globalTransitId);
                        return r;
                    } // using
                } // lock
            } // using
        }

        protected DriveMainIndexRecord ReadRecordFromReader2(SqliteDataReader rdr, Guid identityId,Guid driveId,Guid fileId)
        {
            var result = new List<DriveMainIndexRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new DriveMainIndexRecord();
            item.identityId = identityId;
            item.driveId = driveId;
            item.fileId = fileId;

            if (rdr.IsDBNull(0))
                item.globalTransitId = null;
            else
            {
                bytesRead = rdr.GetBytes(0, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in globalTransitId...");
                item.globalTransitId = new Guid(_guid);
            }

            if (rdr.IsDBNull(1))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.fileState = rdr.GetInt32(1);
            }

            if (rdr.IsDBNull(2))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.requiredSecurityGroup = rdr.GetInt32(2);
            }

            if (rdr.IsDBNull(3))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.fileSystemType = rdr.GetInt32(3);
            }

            if (rdr.IsDBNull(4))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.userDate = new UnixTimeUtc(rdr.GetInt64(4));
            }

            if (rdr.IsDBNull(5))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.fileType = rdr.GetInt32(5);
            }

            if (rdr.IsDBNull(6))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.dataType = rdr.GetInt32(6);
            }

            if (rdr.IsDBNull(7))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.archivalStatus = rdr.GetInt32(7);
            }

            if (rdr.IsDBNull(8))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.historyStatus = rdr.GetInt32(8);
            }

            if (rdr.IsDBNull(9))
                item.senderId = null;
            else
            {
                item.senderId = rdr.GetString(9);
            }

            if (rdr.IsDBNull(10))
                item.groupId = null;
            else
            {
                bytesRead = rdr.GetBytes(10, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in groupId...");
                item.groupId = new Guid(_guid);
            }

            if (rdr.IsDBNull(11))
                item.uniqueId = null;
            else
            {
                bytesRead = rdr.GetBytes(11, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in uniqueId...");
                item.uniqueId = new Guid(_guid);
            }

            if (rdr.IsDBNull(12))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                        item.byteCount = rdr.GetInt64(12);
            }

            if (rdr.IsDBNull(13))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.created = new UnixTimeUtcUnique(rdr.GetInt64(13));
            }

            if (rdr.IsDBNull(14))
                item.modified = null;
            else
            {
                item.modified = new UnixTimeUtcUnique(rdr.GetInt64(14));
            }
            return item;
       }

        protected DriveMainIndexRecord Get(DatabaseConnection conn, Guid identityId,Guid driveId,Guid fileId)
        {
            using (var _get2Command = _database.CreateCommand())
            {
                _get2Command.CommandText = "SELECT globalTransitId,fileState,requiredSecurityGroup,fileSystemType,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,uniqueId,byteCount,created,modified FROM driveMainIndex " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId LIMIT 1;";
                var _get2Param1 = _get2Command.CreateParameter();
                _get2Param1.ParameterName = "@identityId";
                _get2Command.Parameters.Add(_get2Param1);
                var _get2Param2 = _get2Command.CreateParameter();
                _get2Param2.ParameterName = "@driveId";
                _get2Command.Parameters.Add(_get2Param2);
                var _get2Param3 = _get2Command.CreateParameter();
                _get2Param3.ParameterName = "@fileId";
                _get2Command.Parameters.Add(_get2Param3);

                _get2Param1.Value = identityId.ToByteArray();
                _get2Param2.Value = driveId.ToByteArray();
                _get2Param3.Value = fileId.ToByteArray();
                lock (conn._lock)
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_get2Command, System.Data.CommandBehavior.SingleRow))
                    {
                        if (!rdr.Read())
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader2(rdr, identityId,driveId,fileId);
                        return r;
                    } // using
                } // lock
            } // using
        }

    }
}
