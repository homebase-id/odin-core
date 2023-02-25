using System;
using System.Collections.Generic;
using System.Data.SQLite;


namespace Youverse.Core.Storage.SQLite.IdentityDatabase
{
    public class InboxItem
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
        private Guid _boxId;
        public Guid boxId
        {
           get {
                   return _boxId;
               }
           set {
                  _boxId = value;
               }
        }
        private Int32 _priority;
        public Int32 priority
        {
           get {
                   return _priority;
               }
           set {
                  _priority = value;
               }
        }
        private UnixTimeUtc _timestamp;
        public UnixTimeUtc timestamp
        {
           get {
                   return _timestamp;
               }
           set {
                  _timestamp = value;
               }
        }
        private byte[] _value;
        public byte[] value
        {
           get {
                   return _value;
               }
           set {
                  if (value?.Length < 0) throw new Exception("Too short");
                  if (value?.Length > 65535) throw new Exception("Too long");
                  _value = value;
               }
        }
        private Guid? _popstamp;
        public Guid? popstamp
        {
           get {
                   return _popstamp;
               }
           set {
                  _popstamp = value;
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
    } // End of class InboxItem

    public class TableInboxCRUD : TableBase
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
        private SQLiteCommand _deleteCommand = null;
        private static Object _deleteLock = new Object();
        private SQLiteParameter _deleteParam1 = null;
        private SQLiteCommand _getCommand = null;
        private static Object _getLock = new Object();
        private SQLiteParameter _getParam1 = null;

        public TableInboxCRUD(IdentityDatabase db) : base(db)
        {
        }

        ~TableInboxCRUD()
        {
            if (_disposed == false) throw new Exception("TableInboxCRUD Not disposed properly");
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
                    cmd.CommandText = "DROP TABLE IF EXISTS inbox;";
                    cmd.ExecuteNonQuery();
                }
                cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS inbox("
                     +"fileId BLOB NOT NULL UNIQUE, "
                     +"boxId BLOB NOT NULL, "
                     +"priority INT NOT NULL, "
                     +"timestamp INT NOT NULL, "
                     +"value BLOB , "
                     +"popstamp BLOB , "
                     +"created INT NOT NULL, "
                     +"modified INT NOT NULL "
                     +", PRIMARY KEY (fileId)"
                     +");"
                     +"CREATE INDEX IF NOT EXISTS Idx0TableInboxCRUD ON inbox(timestamp);"
                     +"CREATE INDEX IF NOT EXISTS Idx1TableInboxCRUD ON inbox(boxId);"
                     +"CREATE INDEX IF NOT EXISTS Idx2TableInboxCRUD ON inbox(popstamp);"
                     ;
                cmd.ExecuteNonQuery();
            }
        }

        public int Insert(InboxItem item)
        {
            lock (_insertLock)
            {
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand();
                    _insertCommand.CommandText = "INSERT INTO inbox (fileId,boxId,priority,timestamp,value,popstamp,created,modified) " +
                                                 "VALUES ($fileId,$boxId,$priority,$timestamp,$value,$popstamp,$created,$modified)";
                    _insertParam1 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam1);
                    _insertParam1.ParameterName = "$fileId";
                    _insertParam2 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam2);
                    _insertParam2.ParameterName = "$boxId";
                    _insertParam3 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam3);
                    _insertParam3.ParameterName = "$priority";
                    _insertParam4 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam4);
                    _insertParam4.ParameterName = "$timestamp";
                    _insertParam5 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam5);
                    _insertParam5.ParameterName = "$value";
                    _insertParam6 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam6);
                    _insertParam6.ParameterName = "$popstamp";
                    _insertParam7 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam7);
                    _insertParam7.ParameterName = "$created";
                    _insertParam8 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam8);
                    _insertParam8.ParameterName = "$modified";
                    _insertCommand.Prepare();
                }
                _insertParam1.Value = item.fileId;
                _insertParam2.Value = item.boxId;
                _insertParam3.Value = item.priority;
                _insertParam4.Value = UnixTimeUtc.Now().milliseconds;
                _insertParam5.Value = item.value;
                _insertParam6.Value = item.popstamp;
                _insertParam7.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _insertParam8.Value = UnixTimeUtc.Now().milliseconds;
                _database.BeginTransaction();
                return _insertCommand.ExecuteNonQuery();
            } // Lock
        }

        public int Upsert(InboxItem item)
        {
            lock (_upsertLock)
            {
                if (_upsertCommand == null)
                {
                    _upsertCommand = _database.CreateCommand();
                    _upsertCommand.CommandText = "INSERT INTO inbox (fileId,boxId,priority,timestamp,value,popstamp,created,modified) " +
                                                 "VALUES ($fileId,$boxId,$priority,$timestamp,$value,$popstamp,$created,$modified)"+
                                                 "ON CONFLICT (fileId) DO UPDATE "+
                                                 "SET boxId = $boxId,priority = $priority,timestamp = $timestamp,value = $value,popstamp = $popstamp,modified = $modified;";
                    _upsertParam1 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam1);
                    _upsertParam1.ParameterName = "$fileId";
                    _upsertParam2 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam2);
                    _upsertParam2.ParameterName = "$boxId";
                    _upsertParam3 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam3);
                    _upsertParam3.ParameterName = "$priority";
                    _upsertParam4 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam4);
                    _upsertParam4.ParameterName = "$timestamp";
                    _upsertParam5 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam5);
                    _upsertParam5.ParameterName = "$value";
                    _upsertParam6 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam6);
                    _upsertParam6.ParameterName = "$popstamp";
                    _upsertParam7 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam7);
                    _upsertParam7.ParameterName = "$created";
                    _upsertParam8 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam8);
                    _upsertParam8.ParameterName = "$modified";
                    _upsertCommand.Prepare();
                }
                _upsertParam1.Value = item.fileId;
                _upsertParam2.Value = item.boxId;
                _upsertParam3.Value = item.priority;
                _upsertParam4.Value = item.timestamp;
                _upsertParam5.Value = item.value;
                _upsertParam6.Value = item.popstamp;
                _upsertParam7.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _upsertParam8.Value = UnixTimeUtc.Now().milliseconds;
                _database.BeginTransaction();
                return _upsertCommand.ExecuteNonQuery();
            } // Lock
        }

        public int Update(InboxItem item)
        {
            lock (_updateLock)
            {
                if (_updateCommand == null)
                {
                    _updateCommand = _database.CreateCommand();
                    _updateCommand.CommandText = "UPDATE inbox (fileId,boxId,priority,timestamp,value,popstamp,created,modified) " +
                                                 "VALUES ($boxId,$priority,$timestamp,$value,$popstamp,$modified)"+
                                                 "WHERE (fileId = $fileId)";
                    _updateParam1 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam1);
                    _updateParam1.ParameterName = "$fileId";
                    _updateParam2 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam2);
                    _updateParam2.ParameterName = "$boxId";
                    _updateParam3 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam3);
                    _updateParam3.ParameterName = "$priority";
                    _updateParam4 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam4);
                    _updateParam4.ParameterName = "$timestamp";
                    _updateParam5 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam5);
                    _updateParam5.ParameterName = "$value";
                    _updateParam6 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam6);
                    _updateParam6.ParameterName = "$popstamp";
                    _updateParam7 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam7);
                    _updateParam7.ParameterName = "$created";
                    _updateParam8 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam8);
                    _updateParam8.ParameterName = "$modified";
                    _updateCommand.Prepare();
                }
                _updateParam1.Value = item.fileId;
                _updateParam2.Value = item.boxId;
                _updateParam3.Value = item.priority;
                _updateParam4.Value = item.timestamp;
                _updateParam5.Value = item.value;
                _updateParam6.Value = item.popstamp;
                _updateParam7.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _updateParam8.Value = UnixTimeUtc.Now().milliseconds;
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
                    _deleteCommand.CommandText = "DELETE FROM inbox " +
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

        public InboxItem Get(Guid fileId)
        {
            lock (_getLock)
            {
                if (_getCommand == null)
                {
                    _getCommand = _database.CreateCommand();
                    _getCommand.CommandText = "SELECT boxId,priority,timestamp,value,popstamp,created,modified FROM inbox " +
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
                    var item = new InboxItem();
                    item.fileId = fileId;
                    byte[] _tmpbuf = new byte[65535+1];
                    long bytesRead;
                    var _guid = new byte[16];

                    if (rdr.IsDBNull(0))
                        throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                    else
                    {
                        bytesRead = rdr.GetBytes(0, 0, _guid, 0, 16);
                        if (bytesRead != 16)
                            throw new Exception("Not a GUID in boxId...");
                        item.boxId = new Guid(_guid);
                    }

                    if (rdr.IsDBNull(1))
                        throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                    else
                    {
                        item.priority = rdr.GetInt32(1);
                    }

                    if (rdr.IsDBNull(2))
                        throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                    else
                    {
                        item.timestamp = new UnixTimeUtc((UInt64) rdr.GetInt64(2));
                    }

                    if (rdr.IsDBNull(3))
                        item.value = null;
                    else
                    {
                        bytesRead = rdr.GetBytes(3, 0, _tmpbuf, 0, 65535+1);
                        if (bytesRead > 65535)
                            throw new Exception("Too much data in value...");
                        if (bytesRead < 0)
                            throw new Exception("Too little data in value...");
                        if (bytesRead > 0)
                        {
                            item.value = new byte[bytesRead];
                            Buffer.BlockCopy(_tmpbuf, 0, item.value, 0, (int) bytesRead);
                        }
                    }

                    if (rdr.IsDBNull(4))
                        item.popstamp = null;
                    else
                    {
                        bytesRead = rdr.GetBytes(4, 0, _guid, 0, 16);
                        if (bytesRead != 16)
                            throw new Exception("Not a GUID in popstamp...");
                        item.popstamp = new Guid(_guid);
                    }

                    if (rdr.IsDBNull(5))
                        throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                    else
                    {
                        item.created = new UnixTimeUtcUnique((UInt64) rdr.GetInt64(5));
                    }

                    if (rdr.IsDBNull(6))
                        throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                    else
                    {
                        item.modified = new UnixTimeUtc((UInt64) rdr.GetInt64(6));
                    }

                    return item;
                } // using
            } // lock
        }

    }
}
