using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;
using Odin.Core.Identity;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class InboxRecord
    {
        private Int32 _rowid;
        public Int32 rowid
        {
           get {
                   return _rowid;
               }
           set {
                  _rowid = value;
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
        private string _sender;
        public string sender
        {
           get {
                   return _sender;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 0) throw new Exception("Too short");
                    if (value?.Length > 65535) throw new Exception("Too long");
                  _sender = value;
               }
        }
        private Int32 _type;
        public Int32 type
        {
           get {
                   return _type;
               }
           set {
                  _type = value;
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
        private UnixTimeUtc _timeStamp;
        public UnixTimeUtc timeStamp
        {
           get {
                   return _timeStamp;
               }
           set {
                  _timeStamp = value;
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
        private Guid? _popStamp;
        public Guid? popStamp
        {
           get {
                   return _popStamp;
               }
           set {
                  _popStamp = value;
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
    } // End of class InboxRecord

    public class TableInboxCRUD : TableBase
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
        private SqliteCommand _delete0Command = null;
        private static Object _delete0Lock = new Object();
        private SqliteParameter _delete0Param1 = null;
        private SqliteParameter _delete0Param2 = null;
        private SqliteCommand _get0Command = null;
        private static Object _get0Lock = new Object();
        private SqliteParameter _get0Param1 = null;
        private SqliteParameter _get0Param2 = null;

        public TableInboxCRUD(IdentityDatabase db, CacheHelper cache) : base(db)
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
            _delete0Command?.Dispose();
            _delete0Command = null;
            _get0Command?.Dispose();
            _get0Command = null;
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        public sealed override void EnsureTableExists(DatabaseBase.DatabaseConnection conn, bool dropExisting = false)
        {
                using (var cmd = _database.CreateCommand(conn))
                {
                    if (dropExisting)
                    {
                       cmd.CommandText = "DROP TABLE IF EXISTS inbox;";
                        _database.ExecuteNonQuery(conn, cmd);
                    }
                    cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS inbox("
                     +"driveId BLOB NOT NULL, "
                     +"fileId BLOB NOT NULL UNIQUE, "
                     +"sender STRING NOT NULL, "
                     +"type INT NOT NULL, "
                     +"priority INT NOT NULL, "
                     +"timeStamp INT NOT NULL, "
                     +"value BLOB , "
                     +"popStamp BLOB , "
                     +"created INT NOT NULL, "
                     +"modified INT  "
                     +", PRIMARY KEY (driveId,fileId)"
                     +");"
                     +"CREATE INDEX IF NOT EXISTS Idx0TableInboxCRUD ON inbox(timeStamp);"
                     +"CREATE INDEX IF NOT EXISTS Idx1TableInboxCRUD ON inbox(popStamp);"
                     ;
                    _database.ExecuteNonQuery(conn, cmd);
            }
        }

        public virtual int Insert(DatabaseBase.DatabaseConnection conn, InboxRecord item)
        {
            lock (_insertLock)
            {
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand(conn);
                    _insertCommand.CommandText = "INSERT INTO inbox (driveId,fileId,sender,type,priority,timeStamp,value,popStamp,created,modified) " +
                                                 "VALUES ($driveId,$fileId,$sender,$type,$priority,$timeStamp,$value,$popStamp,$created,$modified)";
                    _insertParam1 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam1);
                    _insertParam1.ParameterName = "$driveId";
                    _insertParam2 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam2);
                    _insertParam2.ParameterName = "$fileId";
                    _insertParam3 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam3);
                    _insertParam3.ParameterName = "$sender";
                    _insertParam4 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam4);
                    _insertParam4.ParameterName = "$type";
                    _insertParam5 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam5);
                    _insertParam5.ParameterName = "$priority";
                    _insertParam6 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam6);
                    _insertParam6.ParameterName = "$timeStamp";
                    _insertParam7 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam7);
                    _insertParam7.ParameterName = "$value";
                    _insertParam8 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam8);
                    _insertParam8.ParameterName = "$popStamp";
                    _insertParam9 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam9);
                    _insertParam9.ParameterName = "$created";
                    _insertParam10 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam10);
                    _insertParam10.ParameterName = "$modified";
                    _insertCommand.Prepare();
                }
                _insertParam1.Value = item.driveId.ToByteArray();
                _insertParam2.Value = item.fileId.ToByteArray();
                _insertParam3.Value = item.sender;
                _insertParam4.Value = item.type;
                _insertParam5.Value = item.priority;
                _insertParam6.Value = item.timeStamp.milliseconds;
                _insertParam7.Value = item.value ?? (object)DBNull.Value;
                _insertParam8.Value = item.popStamp?.ToByteArray() ?? (object)DBNull.Value;
                var now = UnixTimeUtcUnique.Now();
                _insertParam9.Value = now.uniqueTime;
                item.modified = null;
                _insertParam10.Value = DBNull.Value;
                var count = _database.ExecuteNonQuery(conn, _insertCommand);
                if (count > 0)
                 {
                     item.created = now;
                 }
                return count;
            } // Lock
        }

        public virtual int Upsert(DatabaseBase.DatabaseConnection conn, InboxRecord item)
        {
            lock (_upsertLock)
            {
                if (_upsertCommand == null)
                {
                    _upsertCommand = _database.CreateCommand(conn);
                    _upsertCommand.CommandText = "INSERT INTO inbox (driveId,fileId,sender,type,priority,timeStamp,value,popStamp,created) " +
                                                 "VALUES ($driveId,$fileId,$sender,$type,$priority,$timeStamp,$value,$popStamp,$created)"+
                                                 "ON CONFLICT (driveId,fileId) DO UPDATE "+
                                                 "SET sender = $sender,type = $type,priority = $priority,timeStamp = $timeStamp,value = $value,popStamp = $popStamp,modified = $modified "+
                                                 "RETURNING created, modified;";
                    _upsertParam1 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam1);
                    _upsertParam1.ParameterName = "$driveId";
                    _upsertParam2 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam2);
                    _upsertParam2.ParameterName = "$fileId";
                    _upsertParam3 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam3);
                    _upsertParam3.ParameterName = "$sender";
                    _upsertParam4 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam4);
                    _upsertParam4.ParameterName = "$type";
                    _upsertParam5 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam5);
                    _upsertParam5.ParameterName = "$priority";
                    _upsertParam6 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam6);
                    _upsertParam6.ParameterName = "$timeStamp";
                    _upsertParam7 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam7);
                    _upsertParam7.ParameterName = "$value";
                    _upsertParam8 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam8);
                    _upsertParam8.ParameterName = "$popStamp";
                    _upsertParam9 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam9);
                    _upsertParam9.ParameterName = "$created";
                    _upsertParam10 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam10);
                    _upsertParam10.ParameterName = "$modified";
                    _upsertCommand.Prepare();
                }
                var now = UnixTimeUtcUnique.Now();
                _upsertParam1.Value = item.driveId.ToByteArray();
                _upsertParam2.Value = item.fileId.ToByteArray();
                _upsertParam3.Value = item.sender;
                _upsertParam4.Value = item.type;
                _upsertParam5.Value = item.priority;
                _upsertParam6.Value = item.timeStamp.milliseconds;
                _upsertParam7.Value = item.value ?? (object)DBNull.Value;
                _upsertParam8.Value = item.popStamp?.ToByteArray() ?? (object)DBNull.Value;
                _upsertParam9.Value = now.uniqueTime;
                _upsertParam10.Value = now.uniqueTime;
                using (SqliteDataReader rdr = _database.ExecuteReader(conn, _upsertCommand, System.Data.CommandBehavior.SingleRow))
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
            } // Lock
            return 0;
        }

        public virtual int Update(DatabaseBase.DatabaseConnection conn, InboxRecord item)
        {
            lock (_updateLock)
            {
                if (_updateCommand == null)
                {
                    _updateCommand = _database.CreateCommand(conn);
                    _updateCommand.CommandText = "UPDATE inbox " +
                                                 "SET sender = $sender,type = $type,priority = $priority,timeStamp = $timeStamp,value = $value,popStamp = $popStamp,modified = $modified "+
                                                 "WHERE (driveId = $driveId,fileId = $fileId)";
                    _updateParam1 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam1);
                    _updateParam1.ParameterName = "$driveId";
                    _updateParam2 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam2);
                    _updateParam2.ParameterName = "$fileId";
                    _updateParam3 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam3);
                    _updateParam3.ParameterName = "$sender";
                    _updateParam4 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam4);
                    _updateParam4.ParameterName = "$type";
                    _updateParam5 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam5);
                    _updateParam5.ParameterName = "$priority";
                    _updateParam6 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam6);
                    _updateParam6.ParameterName = "$timeStamp";
                    _updateParam7 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam7);
                    _updateParam7.ParameterName = "$value";
                    _updateParam8 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam8);
                    _updateParam8.ParameterName = "$popStamp";
                    _updateParam9 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam9);
                    _updateParam9.ParameterName = "$created";
                    _updateParam10 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam10);
                    _updateParam10.ParameterName = "$modified";
                    _updateCommand.Prepare();
                }
                var now = UnixTimeUtcUnique.Now();
                _updateParam1.Value = item.driveId.ToByteArray();
                _updateParam2.Value = item.fileId.ToByteArray();
                _updateParam3.Value = item.sender;
                _updateParam4.Value = item.type;
                _updateParam5.Value = item.priority;
                _updateParam6.Value = item.timeStamp.milliseconds;
                _updateParam7.Value = item.value ?? (object)DBNull.Value;
                _updateParam8.Value = item.popStamp?.ToByteArray() ?? (object)DBNull.Value;
                _updateParam9.Value = now.uniqueTime;
                _updateParam10.Value = now.uniqueTime;
                var count = _database.ExecuteNonQuery(conn, _updateCommand);
                if (count > 0)
                {
                     item.modified = now;
                }
                return count;
            } // Lock
        }

        // SELECT rowid,driveId,fileId,sender,type,priority,timeStamp,value,popStamp,created,modified
        public InboxRecord ReadRecordFromReaderAll(SqliteDataReader rdr)
        {
            var result = new List<InboxRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new InboxRecord();

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.rowid = rdr.GetInt32(0);
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
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.sender = rdr.GetString(3);
            }

            if (rdr.IsDBNull(4))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.type = rdr.GetInt32(4);
            }

            if (rdr.IsDBNull(5))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.priority = rdr.GetInt32(5);
            }

            if (rdr.IsDBNull(6))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.timeStamp = new UnixTimeUtc(rdr.GetInt64(6));
            }

            if (rdr.IsDBNull(7))
                item.value = null;
            else
            {
                bytesRead = rdr.GetBytes(7, 0, _tmpbuf, 0, 65535+1);
                if (bytesRead > 65535)
                    throw new Exception("Too much data in value...");
                if (bytesRead < 0)
                    throw new Exception("Too little data in value...");
                item.value = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.value, 0, (int) bytesRead);
            }

            if (rdr.IsDBNull(8))
                item.popStamp = null;
            else
            {
                bytesRead = rdr.GetBytes(8, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in popStamp...");
                item.popStamp = new Guid(_guid);
            }

            if (rdr.IsDBNull(9))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.created = new UnixTimeUtcUnique(rdr.GetInt64(9));
            }

            if (rdr.IsDBNull(10))
                item.modified = null;
            else
            {
                item.modified = new UnixTimeUtcUnique(rdr.GetInt64(10));
            }
            return item;
       }

        public int Delete(DatabaseBase.DatabaseConnection conn, Guid driveId,Guid fileId)
        {
            lock (_delete0Lock)
            {
                if (_delete0Command == null)
                {
                    _delete0Command = _database.CreateCommand(conn);
                    _delete0Command.CommandText = "DELETE FROM inbox " +
                                                 "WHERE driveId = $driveId AND fileId = $fileId";
                    _delete0Param1 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param1);
                    _delete0Param1.ParameterName = "$driveId";
                    _delete0Param2 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param2);
                    _delete0Param2.ParameterName = "$fileId";
                    _delete0Command.Prepare();
                }
                _delete0Param1.Value = driveId.ToByteArray();
                _delete0Param2.Value = fileId.ToByteArray();
                var count = _database.ExecuteNonQuery(conn, _delete0Command);
                return count;
            } // Lock
        }

        public InboxRecord ReadRecordFromReader0(SqliteDataReader rdr, Guid driveId,Guid fileId)
        {
            var result = new List<InboxRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new InboxRecord();
            item.driveId = driveId;
            item.fileId = fileId;

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.sender = rdr.GetString(0);
            }

            if (rdr.IsDBNull(1))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.type = rdr.GetInt32(1);
            }

            if (rdr.IsDBNull(2))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.priority = rdr.GetInt32(2);
            }

            if (rdr.IsDBNull(3))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.timeStamp = new UnixTimeUtc(rdr.GetInt64(3));
            }

            if (rdr.IsDBNull(4))
                item.value = null;
            else
            {
                bytesRead = rdr.GetBytes(4, 0, _tmpbuf, 0, 65535+1);
                if (bytesRead > 65535)
                    throw new Exception("Too much data in value...");
                if (bytesRead < 0)
                    throw new Exception("Too little data in value...");
                item.value = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.value, 0, (int) bytesRead);
            }

            if (rdr.IsDBNull(5))
                item.popStamp = null;
            else
            {
                bytesRead = rdr.GetBytes(5, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in popStamp...");
                item.popStamp = new Guid(_guid);
            }

            if (rdr.IsDBNull(6))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.created = new UnixTimeUtcUnique(rdr.GetInt64(6));
            }

            if (rdr.IsDBNull(7))
                item.modified = null;
            else
            {
                item.modified = new UnixTimeUtcUnique(rdr.GetInt64(7));
            }
            return item;
       }

        public InboxRecord Get(DatabaseBase.DatabaseConnection conn, Guid driveId,Guid fileId)
        {
            lock (_get0Lock)
            {
                if (_get0Command == null)
                {
                    _get0Command = _database.CreateCommand(conn);
                    _get0Command.CommandText = "SELECT sender,type,priority,timeStamp,value,popStamp,created,modified FROM inbox " +
                                                 "WHERE driveId = $driveId AND fileId = $fileId LIMIT 1;";
                    _get0Param1 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param1);
                    _get0Param1.ParameterName = "$driveId";
                    _get0Param2 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param2);
                    _get0Param2.ParameterName = "$fileId";
                    _get0Command.Prepare();
                }
                _get0Param1.Value = driveId.ToByteArray();
                _get0Param2.Value = fileId.ToByteArray();
                using (SqliteDataReader rdr = _database.ExecuteReader(conn, _get0Command, System.Data.CommandBehavior.SingleRow))
                {
                    if (!rdr.Read())
                    {
                        return null;
                    }
                    var r = ReadRecordFromReader0(rdr, driveId,fileId);
                    return r;
                } // using
            } // lock
        }

    }
}
