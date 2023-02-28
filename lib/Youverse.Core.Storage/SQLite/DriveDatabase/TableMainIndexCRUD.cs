using System;
using System.Collections.Generic;
using System.Data.SQLite;
using Youverse.Core.Identity;

namespace Youverse.Core.Storage.SQLite.DriveDatabase
{
    public class MainIndexItem
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
        private Int64 _userDate;
        public Int64 userDate
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
        private Int32 _isArchived;
        public Int32 isArchived
        {
           get {
                   return _isArchived;
               }
           set {
                  _isArchived = value;
               }
        }
        private Int32 _isHistory;
        public Int32 isHistory
        {
           get {
                   return _isHistory;
               }
           set {
                  _isHistory = value;
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
    } // End of class MainIndexItem

    public class TableMainIndexCRUD : TableBase
    {
        private bool _disposed = false;
        private SQLiteCommand _insertCommand = null;
        private static Object _insertLock = new Object();
        private SQLiteParameter _insertParam1 = null;
        private SQLiteParameter _insertParam2 = null;
        private SQLiteParameter _insertParam3 = null;
        private SQLiteParameter _insertParam4 = null;
        private SQLiteParameter _insertParam5 = null;
        private SQLiteParameter _insertParam6 = null;
        private SQLiteParameter _insertParam7 = null;
        private SQLiteParameter _insertParam8 = null;
        private SQLiteParameter _insertParam9 = null;
        private SQLiteParameter _insertParam10 = null;
        private SQLiteParameter _insertParam11 = null;
        private SQLiteParameter _insertParam12 = null;
        private SQLiteParameter _insertParam13 = null;
        private SQLiteParameter _insertParam14 = null;
        private SQLiteCommand _updateCommand = null;
        private static Object _updateLock = new Object();
        private SQLiteParameter _updateParam1 = null;
        private SQLiteParameter _updateParam2 = null;
        private SQLiteParameter _updateParam3 = null;
        private SQLiteParameter _updateParam4 = null;
        private SQLiteParameter _updateParam5 = null;
        private SQLiteParameter _updateParam6 = null;
        private SQLiteParameter _updateParam7 = null;
        private SQLiteParameter _updateParam8 = null;
        private SQLiteParameter _updateParam9 = null;
        private SQLiteParameter _updateParam10 = null;
        private SQLiteParameter _updateParam11 = null;
        private SQLiteParameter _updateParam12 = null;
        private SQLiteParameter _updateParam13 = null;
        private SQLiteParameter _updateParam14 = null;
        private SQLiteCommand _upsertCommand = null;
        private static Object _upsertLock = new Object();
        private SQLiteParameter _upsertParam1 = null;
        private SQLiteParameter _upsertParam2 = null;
        private SQLiteParameter _upsertParam3 = null;
        private SQLiteParameter _upsertParam4 = null;
        private SQLiteParameter _upsertParam5 = null;
        private SQLiteParameter _upsertParam6 = null;
        private SQLiteParameter _upsertParam7 = null;
        private SQLiteParameter _upsertParam8 = null;
        private SQLiteParameter _upsertParam9 = null;
        private SQLiteParameter _upsertParam10 = null;
        private SQLiteParameter _upsertParam11 = null;
        private SQLiteParameter _upsertParam12 = null;
        private SQLiteParameter _upsertParam13 = null;
        private SQLiteParameter _upsertParam14 = null;
        private SQLiteCommand _deleteCommand = null;
        private static Object _deleteLock = new Object();
        private SQLiteParameter _deleteParam1 = null;
        private SQLiteCommand _getCommand = null;
        private static Object _getLock = new Object();
        private SQLiteParameter _getParam1 = null;

        public TableMainIndexCRUD(DriveDatabase db) : base(db)
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
            _deleteCommand?.Dispose();
            _deleteCommand = null;
            _getCommand?.Dispose();
            _getCommand = null;
            _disposed = true;
        }

        public sealed override void EnsureTableExists(bool dropExisting = false)
        {
            using (var cmd = _database.CreateCommand())
            {
                if (dropExisting)
                {
                    cmd.CommandText = "DROP TABLE IF EXISTS mainIndex;";
                    cmd.ExecuteNonQuery();
                }
                cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS mainIndex("
                     +"fileId BLOB NOT NULL, "
                     +"globalTransitId BLOB  UNIQUE, "
                     +"userDate INT NOT NULL, "
                     +"fileType INT NOT NULL, "
                     +"dataType INT NOT NULL, "
                     +"isArchived INT NOT NULL, "
                     +"isHistory INT NOT NULL, "
                     +"senderId STRING , "
                     +"groupId BLOB , "
                     +"uniqueId BLOB  UNIQUE, "
                     +"requiredSecurityGroup INT NOT NULL, "
                     +"fileSystemType INT NOT NULL, "
                     +"created INT NOT NULL, "
                     +"modified INT  "
                     +", PRIMARY KEY (fileId)"
                     +");"
                     +"CREATE INDEX IF NOT EXISTS Idx0TableMainIndexCRUD ON mainIndex(globalTransitId);"
                     +"CREATE INDEX IF NOT EXISTS Idx1TableMainIndexCRUD ON mainIndex(modified);"
                     ;
                cmd.ExecuteNonQuery();
            }
        }

        public virtual int Insert(MainIndexItem item)
        {
            lock (_insertLock)
            {
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand();
                    _insertCommand.CommandText = "INSERT INTO mainIndex (fileId,globalTransitId,userDate,fileType,dataType,isArchived,isHistory,senderId,groupId,uniqueId,requiredSecurityGroup,fileSystemType,created,modified) " +
                                                 "VALUES ($fileId,$globalTransitId,$userDate,$fileType,$dataType,$isArchived,$isHistory,$senderId,$groupId,$uniqueId,$requiredSecurityGroup,$fileSystemType,$created,$modified)";
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
                    _insertParam6.ParameterName = "$isArchived";
                    _insertParam7 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam7);
                    _insertParam7.ParameterName = "$isHistory";
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
                _insertParam1.Value = item.fileId;
                _insertParam2.Value = item.globalTransitId;
                _insertParam3.Value = item.userDate;
                _insertParam4.Value = item.fileType;
                _insertParam5.Value = item.dataType;
                _insertParam6.Value = item.isArchived;
                _insertParam7.Value = item.isHistory;
                _insertParam8.Value = item.senderId;
                _insertParam9.Value = item.groupId;
                _insertParam10.Value = item.uniqueId;
                _insertParam11.Value = item.requiredSecurityGroup;
                _insertParam12.Value = item.fileSystemType;
                _insertParam13.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _insertParam14.Value = null;
                _database.BeginTransaction();
                return _insertCommand.ExecuteNonQuery();
            } // Lock
        }

        public virtual int Upsert(MainIndexItem item)
        {
            lock (_upsertLock)
            {
                if (_upsertCommand == null)
                {
                    _upsertCommand = _database.CreateCommand();
                    _upsertCommand.CommandText = "INSERT INTO mainIndex (fileId,globalTransitId,userDate,fileType,dataType,isArchived,isHistory,senderId,groupId,uniqueId,requiredSecurityGroup,fileSystemType,created,modified) " +
                                                 "VALUES ($fileId,$globalTransitId,$userDate,$fileType,$dataType,$isArchived,$isHistory,$senderId,$groupId,$uniqueId,$requiredSecurityGroup,$fileSystemType,$created,$modified)"+
                                                 "ON CONFLICT (fileId) DO UPDATE "+
                                                 "SET globalTransitId = $globalTransitId,userDate = $userDate,fileType = $fileType,dataType = $dataType,isArchived = $isArchived,isHistory = $isHistory,senderId = $senderId,groupId = $groupId,uniqueId = $uniqueId,requiredSecurityGroup = $requiredSecurityGroup,fileSystemType = $fileSystemType,modified = $modified;";
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
                    _upsertParam6.ParameterName = "$isArchived";
                    _upsertParam7 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam7);
                    _upsertParam7.ParameterName = "$isHistory";
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
                _upsertParam1.Value = item.fileId;
                _upsertParam2.Value = item.globalTransitId;
                _upsertParam3.Value = item.userDate;
                _upsertParam4.Value = item.fileType;
                _upsertParam5.Value = item.dataType;
                _upsertParam6.Value = item.isArchived;
                _upsertParam7.Value = item.isHistory;
                _upsertParam8.Value = item.senderId;
                _upsertParam9.Value = item.groupId;
                _upsertParam10.Value = item.uniqueId;
                _upsertParam11.Value = item.requiredSecurityGroup;
                _upsertParam12.Value = item.fileSystemType;
                _upsertParam13.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _upsertParam14.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _database.BeginTransaction();
                return _upsertCommand.ExecuteNonQuery();
            } // Lock
        }

        public virtual int Update(MainIndexItem item)
        {
            lock (_updateLock)
            {
                if (_updateCommand == null)
                {
                    _updateCommand = _database.CreateCommand();
                    _updateCommand.CommandText = "UPDATE mainIndex " +
                                                 "SET globalTransitId = $globalTransitId,userDate = $userDate,fileType = $fileType,dataType = $dataType,isArchived = $isArchived,isHistory = $isHistory,senderId = $senderId,groupId = $groupId,uniqueId = $uniqueId,requiredSecurityGroup = $requiredSecurityGroup,fileSystemType = $fileSystemType,modified = $modified "+
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
                    _updateParam6.ParameterName = "$isArchived";
                    _updateParam7 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam7);
                    _updateParam7.ParameterName = "$isHistory";
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
                _updateParam1.Value = item.fileId;
                _updateParam2.Value = item.globalTransitId;
                _updateParam3.Value = item.userDate;
                _updateParam4.Value = item.fileType;
                _updateParam5.Value = item.dataType;
                _updateParam6.Value = item.isArchived;
                _updateParam7.Value = item.isHistory;
                _updateParam8.Value = item.senderId;
                _updateParam9.Value = item.groupId;
                _updateParam10.Value = item.uniqueId;
                _updateParam11.Value = item.requiredSecurityGroup;
                _updateParam12.Value = item.fileSystemType;
                _updateParam13.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _updateParam14.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _database.BeginTransaction();
                return _updateCommand.ExecuteNonQuery();
            } // Lock
        }

        public int Delete(Guid fileId)
        {
            lock (_deleteLock)
            {
                if (_deleteCommand == null)
                {
                    _deleteCommand = _database.CreateCommand();
                    _deleteCommand.CommandText = "DELETE FROM mainIndex " +
                                                 "WHERE fileId = $fileId";
                    _deleteParam1 = _deleteCommand.CreateParameter();
                    _deleteCommand.Parameters.Add(_deleteParam1);
                    _deleteParam1.ParameterName = "$fileId";
                    _deleteCommand.Prepare();
                }
                _deleteParam1.Value = fileId;
                _database.BeginTransaction();
                return _deleteCommand.ExecuteNonQuery();
            } // Lock
        }

        public MainIndexItem Get(Guid fileId)
        {
            lock (_getLock)
            {
                if (_getCommand == null)
                {
                    _getCommand = _database.CreateCommand();
                    _getCommand.CommandText = "SELECT globalTransitId,userDate,fileType,dataType,isArchived,isHistory,senderId,groupId,uniqueId,requiredSecurityGroup,fileSystemType,created,modified FROM mainIndex " +
                                                 "WHERE fileId = $fileId;";
                    _getParam1 = _getCommand.CreateParameter();
                    _getCommand.Parameters.Add(_getParam1);
                    _getParam1.ParameterName = "$fileId";
                    _getCommand.Prepare();
                }
                _getParam1.Value = fileId;
                using (SQLiteDataReader rdr = _getCommand.ExecuteReader(System.Data.CommandBehavior.SingleRow))
                {
                    if (!rdr.Read())
                        return null;
                    var item = new MainIndexItem();
                    item.fileId = fileId;
                    byte[] _tmpbuf = new byte[65535+1];
                    long bytesRead;
                    var _guid = new byte[16];

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
                        item.userDate = rdr.GetInt64(1);
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
                        item.isArchived = rdr.GetInt32(4);
                    }

                    if (rdr.IsDBNull(5))
                        throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                    else
                    {
                        item.isHistory = rdr.GetInt32(5);
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
                        item.created = new UnixTimeUtcUnique((UInt64) rdr.GetInt64(11));
                    }

                    if (rdr.IsDBNull(12))
                        item.modified = null;
                    else
                    {
                        item.modified = new UnixTimeUtcUnique((UInt64) rdr.GetInt64(12));
                    }

                    return item;
                } // using
            } // lock
        }

    }
}
