using System;
using System.Collections.Generic;
using System.Data.SQLite;
using Youverse.Core.Identity;

namespace Youverse.Core.Storage.SQLite.IdentityDatabase
{
    public class OutboxItem
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
        private string _recipient;
        public string recipient
        {
           get {
                   return _recipient;
               }
           set {
                  if (value == null) throw new Exception("Cannot be null");
                  if (value?.Length < 0) throw new Exception("Too short");
                  if (value?.Length > 65535) throw new Exception("Too long");
                  _recipient = value;
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
    } // End of class OutboxItem

    public class TableOutboxCRUD : TableBase
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
        private SQLiteCommand _delete0Command = null;
        private static Object _delete0Lock = new Object();
        private SQLiteParameter _delete0Param1 = null;
        private SQLiteParameter _delete0Param2 = null;
        private SQLiteCommand _get0Command = null;
        private static Object _get0Lock = new Object();
        private SQLiteParameter _get0Param1 = null;
        private SQLiteParameter _get0Param2 = null;

        public TableOutboxCRUD(IdentityDatabase db) : base(db)
        {
        }

        ~TableOutboxCRUD()
        {
            if (_disposed == false) throw new Exception("TableOutboxCRUD Not disposed properly");
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
                    cmd.CommandText = "DROP TABLE IF EXISTS outbox;";
                    cmd.ExecuteNonQuery();
                }
                cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS outbox("
                     +"fileId BLOB NOT NULL, "
                     +"recipient STRING NOT NULL, "
                     +"boxId BLOB NOT NULL, "
                     +"priority INT NOT NULL, "
                     +"timeStamp INT NOT NULL, "
                     +"value BLOB , "
                     +"popStamp BLOB , "
                     +"created INT NOT NULL, "
                     +"modified INT  "
                     +", PRIMARY KEY (fileId,recipient)"
                     +");"
                     +"CREATE INDEX IF NOT EXISTS Idx0TableOutboxCRUD ON outbox(timeStamp);"
                     +"CREATE INDEX IF NOT EXISTS Idx1TableOutboxCRUD ON outbox(boxId);"
                     +"CREATE INDEX IF NOT EXISTS Idx2TableOutboxCRUD ON outbox(popStamp);"
                     ;
                cmd.ExecuteNonQuery();
            }
        }

        public virtual int Insert(OutboxItem item)
        {
            lock (_insertLock)
            {
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand();
                    _insertCommand.CommandText = "INSERT INTO outbox (fileId,recipient,boxId,priority,timeStamp,value,popStamp,created,modified) " +
                                                 "VALUES ($fileId,$recipient,$boxId,$priority,$timeStamp,$value,$popStamp,$created,$modified)";
                    _insertParam1 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam1);
                    _insertParam1.ParameterName = "$fileId";
                    _insertParam2 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam2);
                    _insertParam2.ParameterName = "$recipient";
                    _insertParam3 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam3);
                    _insertParam3.ParameterName = "$boxId";
                    _insertParam4 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam4);
                    _insertParam4.ParameterName = "$priority";
                    _insertParam5 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam5);
                    _insertParam5.ParameterName = "$timeStamp";
                    _insertParam6 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam6);
                    _insertParam6.ParameterName = "$value";
                    _insertParam7 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam7);
                    _insertParam7.ParameterName = "$popStamp";
                    _insertParam8 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam8);
                    _insertParam8.ParameterName = "$created";
                    _insertParam9 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam9);
                    _insertParam9.ParameterName = "$modified";
                    _insertCommand.Prepare();
                }
                _insertParam1.Value = item.fileId;
                _insertParam2.Value = item.recipient;
                _insertParam3.Value = item.boxId;
                _insertParam4.Value = item.priority;
                _insertParam5.Value = item.timeStamp.milliseconds;
                _insertParam6.Value = item.value;
                _insertParam7.Value = item.popStamp;
                _insertParam8.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _insertParam9.Value = null;
                _database.BeginTransaction();
                return _insertCommand.ExecuteNonQuery();
            } // Lock
        }

        public virtual int Upsert(OutboxItem item)
        {
            lock (_upsertLock)
            {
                if (_upsertCommand == null)
                {
                    _upsertCommand = _database.CreateCommand();
                    _upsertCommand.CommandText = "INSERT INTO outbox (fileId,recipient,boxId,priority,timeStamp,value,popStamp,created,modified) " +
                                                 "VALUES ($fileId,$recipient,$boxId,$priority,$timeStamp,$value,$popStamp,$created,$modified)"+
                                                 "ON CONFLICT (fileId,recipient) DO UPDATE "+
                                                 "SET boxId = $boxId,priority = $priority,timeStamp = $timeStamp,value = $value,popStamp = $popStamp,modified = $modified;";
                    _upsertParam1 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam1);
                    _upsertParam1.ParameterName = "$fileId";
                    _upsertParam2 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam2);
                    _upsertParam2.ParameterName = "$recipient";
                    _upsertParam3 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam3);
                    _upsertParam3.ParameterName = "$boxId";
                    _upsertParam4 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam4);
                    _upsertParam4.ParameterName = "$priority";
                    _upsertParam5 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam5);
                    _upsertParam5.ParameterName = "$timeStamp";
                    _upsertParam6 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam6);
                    _upsertParam6.ParameterName = "$value";
                    _upsertParam7 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam7);
                    _upsertParam7.ParameterName = "$popStamp";
                    _upsertParam8 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam8);
                    _upsertParam8.ParameterName = "$created";
                    _upsertParam9 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam9);
                    _upsertParam9.ParameterName = "$modified";
                    _upsertCommand.Prepare();
                }
                _upsertParam1.Value = item.fileId;
                _upsertParam2.Value = item.recipient;
                _upsertParam3.Value = item.boxId;
                _upsertParam4.Value = item.priority;
                _upsertParam5.Value = item.timeStamp.milliseconds;
                _upsertParam6.Value = item.value;
                _upsertParam7.Value = item.popStamp;
                _upsertParam8.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _upsertParam9.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _database.BeginTransaction();
                return _upsertCommand.ExecuteNonQuery();
            } // Lock
        }

        public virtual int Update(OutboxItem item)
        {
            lock (_updateLock)
            {
                if (_updateCommand == null)
                {
                    _updateCommand = _database.CreateCommand();
                    _updateCommand.CommandText = "UPDATE outbox " +
                                                 "SET boxId = $boxId,priority = $priority,timeStamp = $timeStamp,value = $value,popStamp = $popStamp,modified = $modified "+
                                                 "WHERE (fileId = $fileId,recipient = $recipient)";
                    _updateParam1 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam1);
                    _updateParam1.ParameterName = "$fileId";
                    _updateParam2 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam2);
                    _updateParam2.ParameterName = "$recipient";
                    _updateParam3 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam3);
                    _updateParam3.ParameterName = "$boxId";
                    _updateParam4 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam4);
                    _updateParam4.ParameterName = "$priority";
                    _updateParam5 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam5);
                    _updateParam5.ParameterName = "$timeStamp";
                    _updateParam6 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam6);
                    _updateParam6.ParameterName = "$value";
                    _updateParam7 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam7);
                    _updateParam7.ParameterName = "$popStamp";
                    _updateParam8 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam8);
                    _updateParam8.ParameterName = "$created";
                    _updateParam9 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam9);
                    _updateParam9.ParameterName = "$modified";
                    _updateCommand.Prepare();
                }
                _updateParam1.Value = item.fileId;
                _updateParam2.Value = item.recipient;
                _updateParam3.Value = item.boxId;
                _updateParam4.Value = item.priority;
                _updateParam5.Value = item.timeStamp.milliseconds;
                _updateParam6.Value = item.value;
                _updateParam7.Value = item.popStamp;
                _updateParam8.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _updateParam9.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _database.BeginTransaction();
                return _updateCommand.ExecuteNonQuery();
            } // Lock
        }

        public int Delete(Guid fileId,string recipient)
        {
            lock (_delete0Lock)
            {
                if (_delete0Command == null)
                {
                    _delete0Command = _database.CreateCommand();
                    _delete0Command.CommandText = "DELETE FROM outbox " +
                                                 "WHERE fileId = $fileId AND recipient = $recipient";
                    _delete0Param1 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param1);
                    _delete0Param1.ParameterName = "$fileId";
                    _delete0Param2 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param2);
                    _delete0Param2.ParameterName = "$recipient";
                    _delete0Command.Prepare();
                }
                _delete0Param1.Value = fileId;
                _delete0Param2.Value = recipient;
                _database.BeginTransaction();
                return _delete0Command.ExecuteNonQuery();
            } // Lock
        }

        public OutboxItem Get(Guid fileId,string recipient)
        {
            lock (_get0Lock)
            {
                if (_get0Command == null)
                {
                    _get0Command = _database.CreateCommand();
                    _get0Command.CommandText = "SELECT boxId,priority,timeStamp,value,popStamp,created,modified FROM outbox " +
                                                 "WHERE fileId = $fileId AND recipient = $recipient LIMIT 1;";
                    _get0Param1 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param1);
                    _get0Param1.ParameterName = "$fileId";
                    _get0Param2 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param2);
                    _get0Param2.ParameterName = "$recipient";
                    _get0Command.Prepare();
                }
                _get0Param1.Value = fileId;
                _get0Param2.Value = recipient;
                using (SQLiteDataReader rdr = _get0Command.ExecuteReader(System.Data.CommandBehavior.SingleRow))
                {
                    var result = new OutboxItem();
                    if (!rdr.Read())
                        return null;
                    byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
                    long bytesRead;
#pragma warning restore CS0168
                    var _guid = new byte[16];
                        var item = new OutboxItem();
                        item.fileId = fileId;
                        item.recipient = recipient;

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
                            item.timeStamp = new UnixTimeUtc((UInt64) rdr.GetInt64(2));
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
                            item.popStamp = null;
                        else
                        {
                            bytesRead = rdr.GetBytes(4, 0, _guid, 0, 16);
                            if (bytesRead != 16)
                                throw new Exception("Not a GUID in popStamp...");
                            item.popStamp = new Guid(_guid);
                        }

                        if (rdr.IsDBNull(5))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            item.created = new UnixTimeUtcUnique((UInt64) rdr.GetInt64(5));
                        }

                        if (rdr.IsDBNull(6))
                            item.modified = null;
                        else
                        {
                            item.modified = new UnixTimeUtcUnique((UInt64) rdr.GetInt64(6));
                        }
                    return item;
                } // using
            } // lock
        }

    }
}
