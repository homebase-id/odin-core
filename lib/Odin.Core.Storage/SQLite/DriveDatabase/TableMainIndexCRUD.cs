using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;

namespace Odin.Core.Storage.SQLite.DriveDatabase
{
    public class MainIndexRecord
    {
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
    } // End of class MainIndexRecord

    public class TableMainIndexCRUD : TableBase
    {
        private bool _disposed = false;
        private SqliteCommand _insertCommand = null;
        private static Object _insertLock = new Object();
        private SqliteParameter _insertParam1 = null;
        private SqliteParameter _insertParam2 = null;
        private SqliteParameter _insertParam3 = null;
        private SqliteParameter _insertParam4 = null;
        private SqliteParameter _insertParam5 = null;
        private SqliteParameter _insertParam6 = null;
        private SqliteParameter _insertParam7 = null;
        private SqliteParameter _insertParam8 = null;
        private SqliteParameter _insertParam9 = null;
        private SqliteParameter _insertParam10 = null;
        private SqliteParameter _insertParam11 = null;
        private SqliteParameter _insertParam12 = null;
        private SqliteParameter _insertParam13 = null;
        private SqliteParameter _insertParam14 = null;
        private SqliteCommand _updateCommand = null;
        private static Object _updateLock = new Object();
        private SqliteParameter _updateParam1 = null;
        private SqliteParameter _updateParam2 = null;
        private SqliteParameter _updateParam3 = null;
        private SqliteParameter _updateParam4 = null;
        private SqliteParameter _updateParam5 = null;
        private SqliteParameter _updateParam6 = null;
        private SqliteParameter _updateParam7 = null;
        private SqliteParameter _updateParam8 = null;
        private SqliteParameter _updateParam9 = null;
        private SqliteParameter _updateParam10 = null;
        private SqliteParameter _updateParam11 = null;
        private SqliteParameter _updateParam12 = null;
        private SqliteParameter _updateParam13 = null;
        private SqliteParameter _updateParam14 = null;
        private SqliteCommand _upsertCommand = null;
        private static Object _upsertLock = new Object();
        private SqliteParameter _upsertParam1 = null;
        private SqliteParameter _upsertParam2 = null;
        private SqliteParameter _upsertParam3 = null;
        private SqliteParameter _upsertParam4 = null;
        private SqliteParameter _upsertParam5 = null;
        private SqliteParameter _upsertParam6 = null;
        private SqliteParameter _upsertParam7 = null;
        private SqliteParameter _upsertParam8 = null;
        private SqliteParameter _upsertParam9 = null;
        private SqliteParameter _upsertParam10 = null;
        private SqliteParameter _upsertParam11 = null;
        private SqliteParameter _upsertParam12 = null;
        private SqliteParameter _upsertParam13 = null;
        private SqliteParameter _upsertParam14 = null;
        private SqliteCommand _delete0Command = null;
        private static Object _delete0Lock = new Object();
        private SqliteParameter _delete0Param1 = null;
        private SqliteCommand _get0Command = null;
        private static Object _get0Lock = new Object();
        private SqliteParameter _get0Param1 = null;

        public TableMainIndexCRUD(DriveDatabase db, CacheHelper cache) : base(db)
        {
        }

        ~TableMainIndexCRUD()
        {
            if (_disposed == false) throw new Exception("TableMainIndexCRUD Not disposed properly");
        }

        public override void Dispose()
        {
            _insertCommand?.Dispose();
            _insertCommand = null;
            _updateCommand?.Dispose();
            _updateCommand = null;
            _upsertCommand?.Dispose();
            _upsertCommand = null;
            _delete0Command?.Dispose();
            _delete0Command = null;
            _get0Command?.Dispose();
            _get0Command = null;
            _disposed = true;
        }

        public sealed override void EnsureTableExists(bool dropExisting = false)
        {
            using (var cmd = _database.CreateCommand())
            {
                if (dropExisting)
                {
                    cmd.CommandText = "DROP TABLE IF EXISTS mainIndex;";
                    _database.ExecuteNonQuery(cmd);
                }
                cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS mainIndex("
                     +"fileId BLOB NOT NULL, "
                     +"globalTransitId BLOB  UNIQUE, "
                     +"userDate INT NOT NULL, "
                     +"fileType INT NOT NULL, "
                     +"dataType INT NOT NULL, "
                     +"archivalStatus INT NOT NULL, "
                     +"historyStatus INT NOT NULL, "
                     +"senderId STRING , "
                     +"groupId BLOB , "
                     +"uniqueId BLOB  UNIQUE, "
                     +"requiredSecurityGroup INT NOT NULL, "
                     +"fileSystemType INT NOT NULL, "
                     +"created INT NOT NULL, "
                     +"modified INT  "
                     +", PRIMARY KEY (fileId)"
                     +");"
                     +"CREATE INDEX IF NOT EXISTS Idx0TableMainIndexCRUD ON mainIndex(modified);"
                     ;
                _database.ExecuteNonQuery(cmd);
                _database.Commit();
            }
        }

        public virtual int Insert(MainIndexRecord item)
        {
            lock (_insertLock)
            {
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand();
                    _insertCommand.CommandText = "INSERT INTO mainIndex (fileId,globalTransitId,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,uniqueId,requiredSecurityGroup,fileSystemType,created,modified) " +
                                                 "VALUES ($fileId,$globalTransitId,$userDate,$fileType,$dataType,$archivalStatus,$historyStatus,$senderId,$groupId,$uniqueId,$requiredSecurityGroup,$fileSystemType,$created,$modified)";
                    _insertParam1 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam1);
                    _insertParam1.ParameterName = "$fileId";
                    _insertParam2 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam2);
                    _insertParam2.ParameterName = "$globalTransitId";
                    _insertParam3 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam3);
                    _insertParam3.ParameterName = "$userDate";
                    _insertParam4 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam4);
                    _insertParam4.ParameterName = "$fileType";
                    _insertParam5 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam5);
                    _insertParam5.ParameterName = "$dataType";
                    _insertParam6 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam6);
                    _insertParam6.ParameterName = "$archivalStatus";
                    _insertParam7 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam7);
                    _insertParam7.ParameterName = "$historyStatus";
                    _insertParam8 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam8);
                    _insertParam8.ParameterName = "$senderId";
                    _insertParam9 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam9);
                    _insertParam9.ParameterName = "$groupId";
                    _insertParam10 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam10);
                    _insertParam10.ParameterName = "$uniqueId";
                    _insertParam11 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam11);
                    _insertParam11.ParameterName = "$requiredSecurityGroup";
                    _insertParam12 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam12);
                    _insertParam12.ParameterName = "$fileSystemType";
                    _insertParam13 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam13);
                    _insertParam13.ParameterName = "$created";
                    _insertParam14 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam14);
                    _insertParam14.ParameterName = "$modified";
                    _insertCommand.Prepare();
                }
                _insertParam1.Value = item.fileId.ToByteArray();
                _insertParam2.Value = item.globalTransitId?.ToByteArray() ?? (object)DBNull.Value;
                _insertParam3.Value = item.userDate.milliseconds;
                _insertParam4.Value = item.fileType;
                _insertParam5.Value = item.dataType;
                _insertParam6.Value = item.archivalStatus;
                _insertParam7.Value = item.historyStatus;
                _insertParam8.Value = item.senderId ?? (object)DBNull.Value;
                _insertParam9.Value = item.groupId?.ToByteArray() ?? (object)DBNull.Value;
                _insertParam10.Value = item.uniqueId?.ToByteArray() ?? (object)DBNull.Value;
                _insertParam11.Value = item.requiredSecurityGroup;
                _insertParam12.Value = item.fileSystemType;
                _insertParam13.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _insertParam14.Value = DBNull.Value;
                var count = _database.ExecuteNonQuery(_insertCommand);
                return count;
            } // Lock
        }

        public virtual int Upsert(MainIndexRecord item)
        {
            lock (_upsertLock)
            {
                if (_upsertCommand == null)
                {
                    _upsertCommand = _database.CreateCommand();
                    _upsertCommand.CommandText = "INSERT INTO mainIndex (fileId,globalTransitId,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,uniqueId,requiredSecurityGroup,fileSystemType,created,modified) " +
                                                 "VALUES ($fileId,$globalTransitId,$userDate,$fileType,$dataType,$archivalStatus,$historyStatus,$senderId,$groupId,$uniqueId,$requiredSecurityGroup,$fileSystemType,$created,$modified)"+
                                                 "ON CONFLICT (fileId) DO UPDATE "+
                                                 "SET globalTransitId = $globalTransitId,userDate = $userDate,fileType = $fileType,dataType = $dataType,archivalStatus = $archivalStatus,historyStatus = $historyStatus,senderId = $senderId,groupId = $groupId,uniqueId = $uniqueId,requiredSecurityGroup = $requiredSecurityGroup,fileSystemType = $fileSystemType,modified = $modified;";
                    _upsertParam1 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam1);
                    _upsertParam1.ParameterName = "$fileId";
                    _upsertParam2 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam2);
                    _upsertParam2.ParameterName = "$globalTransitId";
                    _upsertParam3 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam3);
                    _upsertParam3.ParameterName = "$userDate";
                    _upsertParam4 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam4);
                    _upsertParam4.ParameterName = "$fileType";
                    _upsertParam5 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam5);
                    _upsertParam5.ParameterName = "$dataType";
                    _upsertParam6 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam6);
                    _upsertParam6.ParameterName = "$archivalStatus";
                    _upsertParam7 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam7);
                    _upsertParam7.ParameterName = "$historyStatus";
                    _upsertParam8 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam8);
                    _upsertParam8.ParameterName = "$senderId";
                    _upsertParam9 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam9);
                    _upsertParam9.ParameterName = "$groupId";
                    _upsertParam10 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam10);
                    _upsertParam10.ParameterName = "$uniqueId";
                    _upsertParam11 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam11);
                    _upsertParam11.ParameterName = "$requiredSecurityGroup";
                    _upsertParam12 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam12);
                    _upsertParam12.ParameterName = "$fileSystemType";
                    _upsertParam13 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam13);
                    _upsertParam13.ParameterName = "$created";
                    _upsertParam14 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam14);
                    _upsertParam14.ParameterName = "$modified";
                    _upsertCommand.Prepare();
                }
                _upsertParam1.Value = item.fileId.ToByteArray();
                _upsertParam2.Value = item.globalTransitId?.ToByteArray() ?? (object)DBNull.Value;
                _upsertParam3.Value = item.userDate.milliseconds;
                _upsertParam4.Value = item.fileType;
                _upsertParam5.Value = item.dataType;
                _upsertParam6.Value = item.archivalStatus;
                _upsertParam7.Value = item.historyStatus;
                _upsertParam8.Value = item.senderId ?? (object)DBNull.Value;
                _upsertParam9.Value = item.groupId?.ToByteArray() ?? (object)DBNull.Value;
                _upsertParam10.Value = item.uniqueId?.ToByteArray() ?? (object)DBNull.Value;
                _upsertParam11.Value = item.requiredSecurityGroup;
                _upsertParam12.Value = item.fileSystemType;
                _upsertParam13.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _upsertParam14.Value = UnixTimeUtcUnique.Now().uniqueTime;
                var count = _database.ExecuteNonQuery(_upsertCommand);
                return count;
            } // Lock
        }

        public virtual int Update(MainIndexRecord item)
        {
            lock (_updateLock)
            {
                if (_updateCommand == null)
                {
                    _updateCommand = _database.CreateCommand();
                    _updateCommand.CommandText = "UPDATE mainIndex " +
                                                 "SET globalTransitId = $globalTransitId,userDate = $userDate,fileType = $fileType,dataType = $dataType,archivalStatus = $archivalStatus,historyStatus = $historyStatus,senderId = $senderId,groupId = $groupId,uniqueId = $uniqueId,requiredSecurityGroup = $requiredSecurityGroup,fileSystemType = $fileSystemType,modified = $modified "+
                                                 "WHERE (fileId = $fileId)";
                    _updateParam1 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam1);
                    _updateParam1.ParameterName = "$fileId";
                    _updateParam2 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam2);
                    _updateParam2.ParameterName = "$globalTransitId";
                    _updateParam3 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam3);
                    _updateParam3.ParameterName = "$userDate";
                    _updateParam4 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam4);
                    _updateParam4.ParameterName = "$fileType";
                    _updateParam5 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam5);
                    _updateParam5.ParameterName = "$dataType";
                    _updateParam6 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam6);
                    _updateParam6.ParameterName = "$archivalStatus";
                    _updateParam7 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam7);
                    _updateParam7.ParameterName = "$historyStatus";
                    _updateParam8 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam8);
                    _updateParam8.ParameterName = "$senderId";
                    _updateParam9 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam9);
                    _updateParam9.ParameterName = "$groupId";
                    _updateParam10 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam10);
                    _updateParam10.ParameterName = "$uniqueId";
                    _updateParam11 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam11);
                    _updateParam11.ParameterName = "$requiredSecurityGroup";
                    _updateParam12 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam12);
                    _updateParam12.ParameterName = "$fileSystemType";
                    _updateParam13 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam13);
                    _updateParam13.ParameterName = "$created";
                    _updateParam14 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam14);
                    _updateParam14.ParameterName = "$modified";
                    _updateCommand.Prepare();
                }
                _updateParam1.Value = item.fileId.ToByteArray();
                _updateParam2.Value = item.globalTransitId?.ToByteArray() ?? (object)DBNull.Value;
                _updateParam3.Value = item.userDate.milliseconds;
                _updateParam4.Value = item.fileType;
                _updateParam5.Value = item.dataType;
                _updateParam6.Value = item.archivalStatus;
                _updateParam7.Value = item.historyStatus;
                _updateParam8.Value = item.senderId ?? (object)DBNull.Value;
                _updateParam9.Value = item.groupId?.ToByteArray() ?? (object)DBNull.Value;
                _updateParam10.Value = item.uniqueId?.ToByteArray() ?? (object)DBNull.Value;
                _updateParam11.Value = item.requiredSecurityGroup;
                _updateParam12.Value = item.fileSystemType;
                _updateParam13.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _updateParam14.Value = UnixTimeUtcUnique.Now().uniqueTime;
                var count = _database.ExecuteNonQuery(_updateCommand);
                return count;
            } // Lock
        }

        // SELECT fileId,globalTransitId,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,uniqueId,requiredSecurityGroup,fileSystemType,created,modified
        public MainIndexRecord ReadRecordFromReaderAll(SqliteDataReader rdr)
        {
            var result = new List<MainIndexRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new MainIndexRecord();

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
                item.userDate = new UnixTimeUtc(rdr.GetInt64(2));
            }

            if (rdr.IsDBNull(3))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.fileType = rdr.GetInt32(3);
            }

            if (rdr.IsDBNull(4))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.dataType = rdr.GetInt32(4);
            }

            if (rdr.IsDBNull(5))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.archivalStatus = rdr.GetInt32(5);
            }

            if (rdr.IsDBNull(6))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.historyStatus = rdr.GetInt32(6);
            }

            if (rdr.IsDBNull(7))
                item.senderId = null;
            else
            {
                item.senderId = rdr.GetString(7);
            }

            if (rdr.IsDBNull(8))
                item.groupId = null;
            else
            {
                bytesRead = rdr.GetBytes(8, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in groupId...");
                item.groupId = new Guid(_guid);
            }

            if (rdr.IsDBNull(9))
                item.uniqueId = null;
            else
            {
                bytesRead = rdr.GetBytes(9, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in uniqueId...");
                item.uniqueId = new Guid(_guid);
            }

            if (rdr.IsDBNull(10))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.requiredSecurityGroup = rdr.GetInt32(10);
            }

            if (rdr.IsDBNull(11))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.fileSystemType = rdr.GetInt32(11);
            }

            if (rdr.IsDBNull(12))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.created = new UnixTimeUtcUnique(rdr.GetInt64(12));
            }

            if (rdr.IsDBNull(13))
                item.modified = null;
            else
            {
                item.modified = new UnixTimeUtcUnique(rdr.GetInt64(13));
            }
            return item;
       }

        public int Delete(Guid fileId)
        {
            lock (_delete0Lock)
            {
                if (_delete0Command == null)
                {
                    _delete0Command = _database.CreateCommand();
                    _delete0Command.CommandText = "DELETE FROM mainIndex " +
                                                 "WHERE fileId = $fileId";
                    _delete0Param1 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param1);
                    _delete0Param1.ParameterName = "$fileId";
                    _delete0Command.Prepare();
                }
                _delete0Param1.Value = fileId.ToByteArray();
                var count = _database.ExecuteNonQuery(_delete0Command);
                return count;
            } // Lock
        }

        public MainIndexRecord ReadRecordFromReader0(SqliteDataReader rdr, Guid fileId)
        {
            var result = new List<MainIndexRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new MainIndexRecord();
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
                item.userDate = new UnixTimeUtc(rdr.GetInt64(1));
            }

            if (rdr.IsDBNull(2))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.fileType = rdr.GetInt32(2);
            }

            if (rdr.IsDBNull(3))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.dataType = rdr.GetInt32(3);
            }

            if (rdr.IsDBNull(4))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.archivalStatus = rdr.GetInt32(4);
            }

            if (rdr.IsDBNull(5))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.historyStatus = rdr.GetInt32(5);
            }

            if (rdr.IsDBNull(6))
                item.senderId = null;
            else
            {
                item.senderId = rdr.GetString(6);
            }

            if (rdr.IsDBNull(7))
                item.groupId = null;
            else
            {
                bytesRead = rdr.GetBytes(7, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in groupId...");
                item.groupId = new Guid(_guid);
            }

            if (rdr.IsDBNull(8))
                item.uniqueId = null;
            else
            {
                bytesRead = rdr.GetBytes(8, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in uniqueId...");
                item.uniqueId = new Guid(_guid);
            }

            if (rdr.IsDBNull(9))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.requiredSecurityGroup = rdr.GetInt32(9);
            }

            if (rdr.IsDBNull(10))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.fileSystemType = rdr.GetInt32(10);
            }

            if (rdr.IsDBNull(11))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.created = new UnixTimeUtcUnique(rdr.GetInt64(11));
            }

            if (rdr.IsDBNull(12))
                item.modified = null;
            else
            {
                item.modified = new UnixTimeUtcUnique(rdr.GetInt64(12));
            }
            return item;
       }

        public MainIndexRecord Get(Guid fileId)
        {
            lock (_get0Lock)
            {
                if (_get0Command == null)
                {
                    _get0Command = _database.CreateCommand();
                    _get0Command.CommandText = "SELECT globalTransitId,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,uniqueId,requiredSecurityGroup,fileSystemType,created,modified FROM mainIndex " +
                                                 "WHERE fileId = $fileId LIMIT 1;";
                    _get0Param1 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param1);
                    _get0Param1.ParameterName = "$fileId";
                    _get0Command.Prepare();
                }
                _get0Param1.Value = fileId.ToByteArray();
                using (SqliteDataReader rdr = _database.ExecuteReader(_get0Command, System.Data.CommandBehavior.SingleRow))
                {
                    if (!rdr.Read())
                    {
                        return null;
                    }
                    var r = ReadRecordFromReader0(rdr, fileId);
                    return r;
                } // using
            } // lock
        }

    }
}
