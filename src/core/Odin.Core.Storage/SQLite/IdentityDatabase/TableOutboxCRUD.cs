using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;
using Odin.Core.Identity;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class OutboxRecord
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
        private Guid? _dependencyFileId;
        public Guid? dependencyFileId
        {
           get {
                   return _dependencyFileId;
               }
           set {
                  _dependencyFileId = value;
               }
        }
        private Int32 _checkOutCount;
        public Int32 checkOutCount
        {
           get {
                   return _checkOutCount;
               }
           set {
                  _checkOutCount = value;
               }
        }
        private UnixTimeUtc _nextRunTime;
        public UnixTimeUtc nextRunTime
        {
           get {
                   return _nextRunTime;
               }
           set {
                  _nextRunTime = value;
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
        private Guid? _checkOutStamp;
        public Guid? checkOutStamp
        {
           get {
                   return _checkOutStamp;
               }
           set {
                  _checkOutStamp = value;
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
    } // End of class OutboxRecord

    public class TableOutboxCRUD : TableBase
    {
        private bool _disposed = false;

        public TableOutboxCRUD(IdentityDatabase db, CacheHelper cache) : base(db)
        {
        }

        ~TableOutboxCRUD()
        {
            if (_disposed == false) throw new Exception("TableOutboxCRUD Not disposed properly");
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
                       cmd.CommandText = "DROP TABLE IF EXISTS outbox;";
                       conn.ExecuteNonQuery(cmd);
                    }
                    cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS outbox("
                     +"driveId BLOB NOT NULL, "
                     +"fileId BLOB NOT NULL, "
                     +"recipient STRING NOT NULL, "
                     +"type INT NOT NULL, "
                     +"priority INT NOT NULL, "
                     +"dependencyFileId BLOB , "
                     +"checkOutCount INT NOT NULL, "
                     +"nextRunTime INT NOT NULL, "
                     +"value BLOB , "
                     +"checkOutStamp BLOB , "
                     +"created INT NOT NULL, "
                     +"modified INT  "
                     +", PRIMARY KEY (driveId,fileId,recipient)"
                     +");"
                     +"CREATE INDEX IF NOT EXISTS Idx0TableOutboxCRUD ON outbox(nextRunTime);"
                     ;
                    conn.ExecuteNonQuery(cmd);
            }
        }

        public virtual int Insert(DatabaseConnection conn, OutboxRecord item)
        {
                using (var _insertCommand = _database.CreateCommand())
                {
                    _insertCommand.CommandText = "INSERT INTO outbox (driveId,fileId,recipient,type,priority,dependencyFileId,checkOutCount,nextRunTime,value,checkOutStamp,created,modified) " +
                                                 "VALUES ($driveId,$fileId,$recipient,$type,$priority,$dependencyFileId,$checkOutCount,$nextRunTime,$value,$checkOutStamp,$created,$modified)";
                    var _insertParam1 = _insertCommand.CreateParameter();
                    _insertParam1.ParameterName = "$driveId";
                    _insertCommand.Parameters.Add(_insertParam1);
                    var _insertParam2 = _insertCommand.CreateParameter();
                    _insertParam2.ParameterName = "$fileId";
                    _insertCommand.Parameters.Add(_insertParam2);
                    var _insertParam3 = _insertCommand.CreateParameter();
                    _insertParam3.ParameterName = "$recipient";
                    _insertCommand.Parameters.Add(_insertParam3);
                    var _insertParam4 = _insertCommand.CreateParameter();
                    _insertParam4.ParameterName = "$type";
                    _insertCommand.Parameters.Add(_insertParam4);
                    var _insertParam5 = _insertCommand.CreateParameter();
                    _insertParam5.ParameterName = "$priority";
                    _insertCommand.Parameters.Add(_insertParam5);
                    var _insertParam6 = _insertCommand.CreateParameter();
                    _insertParam6.ParameterName = "$dependencyFileId";
                    _insertCommand.Parameters.Add(_insertParam6);
                    var _insertParam7 = _insertCommand.CreateParameter();
                    _insertParam7.ParameterName = "$checkOutCount";
                    _insertCommand.Parameters.Add(_insertParam7);
                    var _insertParam8 = _insertCommand.CreateParameter();
                    _insertParam8.ParameterName = "$nextRunTime";
                    _insertCommand.Parameters.Add(_insertParam8);
                    var _insertParam9 = _insertCommand.CreateParameter();
                    _insertParam9.ParameterName = "$value";
                    _insertCommand.Parameters.Add(_insertParam9);
                    var _insertParam10 = _insertCommand.CreateParameter();
                    _insertParam10.ParameterName = "$checkOutStamp";
                    _insertCommand.Parameters.Add(_insertParam10);
                    var _insertParam11 = _insertCommand.CreateParameter();
                    _insertParam11.ParameterName = "$created";
                    _insertCommand.Parameters.Add(_insertParam11);
                    var _insertParam12 = _insertCommand.CreateParameter();
                    _insertParam12.ParameterName = "$modified";
                    _insertCommand.Parameters.Add(_insertParam12);
                _insertParam1.Value = item.driveId.ToByteArray();
                _insertParam2.Value = item.fileId.ToByteArray();
                _insertParam3.Value = item.recipient;
                _insertParam4.Value = item.type;
                _insertParam5.Value = item.priority;
                _insertParam6.Value = item.dependencyFileId?.ToByteArray() ?? (object)DBNull.Value;
                _insertParam7.Value = item.checkOutCount;
                _insertParam8.Value = item.nextRunTime.milliseconds;
                _insertParam9.Value = item.value ?? (object)DBNull.Value;
                _insertParam10.Value = item.checkOutStamp?.ToByteArray() ?? (object)DBNull.Value;
                var now = UnixTimeUtcUnique.Now();
                _insertParam11.Value = now.uniqueTime;
                item.modified = null;
                _insertParam12.Value = DBNull.Value;
                var count = conn.ExecuteNonQuery(_insertCommand);
                if (count > 0)
                 {
                     item.created = now;
                 }
                return count;
                } // Using
        }

        public virtual int Upsert(DatabaseConnection conn, OutboxRecord item)
        {
                using (var _upsertCommand = _database.CreateCommand())
                {
                    _upsertCommand.CommandText = "INSERT INTO outbox (driveId,fileId,recipient,type,priority,dependencyFileId,checkOutCount,nextRunTime,value,checkOutStamp,created) " +
                                                 "VALUES ($driveId,$fileId,$recipient,$type,$priority,$dependencyFileId,$checkOutCount,$nextRunTime,$value,$checkOutStamp,$created)"+
                                                 "ON CONFLICT (driveId,fileId,recipient) DO UPDATE "+
                                                 "SET type = $type,priority = $priority,dependencyFileId = $dependencyFileId,checkOutCount = $checkOutCount,nextRunTime = $nextRunTime,value = $value,checkOutStamp = $checkOutStamp,modified = $modified "+
                                                 "RETURNING created, modified;";
                    var _upsertParam1 = _upsertCommand.CreateParameter();
                    _upsertParam1.ParameterName = "$driveId";
                    _upsertCommand.Parameters.Add(_upsertParam1);
                    var _upsertParam2 = _upsertCommand.CreateParameter();
                    _upsertParam2.ParameterName = "$fileId";
                    _upsertCommand.Parameters.Add(_upsertParam2);
                    var _upsertParam3 = _upsertCommand.CreateParameter();
                    _upsertParam3.ParameterName = "$recipient";
                    _upsertCommand.Parameters.Add(_upsertParam3);
                    var _upsertParam4 = _upsertCommand.CreateParameter();
                    _upsertParam4.ParameterName = "$type";
                    _upsertCommand.Parameters.Add(_upsertParam4);
                    var _upsertParam5 = _upsertCommand.CreateParameter();
                    _upsertParam5.ParameterName = "$priority";
                    _upsertCommand.Parameters.Add(_upsertParam5);
                    var _upsertParam6 = _upsertCommand.CreateParameter();
                    _upsertParam6.ParameterName = "$dependencyFileId";
                    _upsertCommand.Parameters.Add(_upsertParam6);
                    var _upsertParam7 = _upsertCommand.CreateParameter();
                    _upsertParam7.ParameterName = "$checkOutCount";
                    _upsertCommand.Parameters.Add(_upsertParam7);
                    var _upsertParam8 = _upsertCommand.CreateParameter();
                    _upsertParam8.ParameterName = "$nextRunTime";
                    _upsertCommand.Parameters.Add(_upsertParam8);
                    var _upsertParam9 = _upsertCommand.CreateParameter();
                    _upsertParam9.ParameterName = "$value";
                    _upsertCommand.Parameters.Add(_upsertParam9);
                    var _upsertParam10 = _upsertCommand.CreateParameter();
                    _upsertParam10.ParameterName = "$checkOutStamp";
                    _upsertCommand.Parameters.Add(_upsertParam10);
                    var _upsertParam11 = _upsertCommand.CreateParameter();
                    _upsertParam11.ParameterName = "$created";
                    _upsertCommand.Parameters.Add(_upsertParam11);
                    var _upsertParam12 = _upsertCommand.CreateParameter();
                    _upsertParam12.ParameterName = "$modified";
                    _upsertCommand.Parameters.Add(_upsertParam12);
                var now = UnixTimeUtcUnique.Now();
                _upsertParam1.Value = item.driveId.ToByteArray();
                _upsertParam2.Value = item.fileId.ToByteArray();
                _upsertParam3.Value = item.recipient;
                _upsertParam4.Value = item.type;
                _upsertParam5.Value = item.priority;
                _upsertParam6.Value = item.dependencyFileId?.ToByteArray() ?? (object)DBNull.Value;
                _upsertParam7.Value = item.checkOutCount;
                _upsertParam8.Value = item.nextRunTime.milliseconds;
                _upsertParam9.Value = item.value ?? (object)DBNull.Value;
                _upsertParam10.Value = item.checkOutStamp?.ToByteArray() ?? (object)DBNull.Value;
                _upsertParam11.Value = now.uniqueTime;
                _upsertParam12.Value = now.uniqueTime;
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

        public virtual int Update(DatabaseConnection conn, OutboxRecord item)
        {
                using (var _updateCommand = _database.CreateCommand())
                {
                    _updateCommand.CommandText = "UPDATE outbox " +
                                                 "SET type = $type,priority = $priority,dependencyFileId = $dependencyFileId,checkOutCount = $checkOutCount,nextRunTime = $nextRunTime,value = $value,checkOutStamp = $checkOutStamp,modified = $modified "+
                                                 "WHERE (driveId = $driveId,fileId = $fileId,recipient = $recipient)";
                    var _updateParam1 = _updateCommand.CreateParameter();
                    _updateParam1.ParameterName = "$driveId";
                    _updateCommand.Parameters.Add(_updateParam1);
                    var _updateParam2 = _updateCommand.CreateParameter();
                    _updateParam2.ParameterName = "$fileId";
                    _updateCommand.Parameters.Add(_updateParam2);
                    var _updateParam3 = _updateCommand.CreateParameter();
                    _updateParam3.ParameterName = "$recipient";
                    _updateCommand.Parameters.Add(_updateParam3);
                    var _updateParam4 = _updateCommand.CreateParameter();
                    _updateParam4.ParameterName = "$type";
                    _updateCommand.Parameters.Add(_updateParam4);
                    var _updateParam5 = _updateCommand.CreateParameter();
                    _updateParam5.ParameterName = "$priority";
                    _updateCommand.Parameters.Add(_updateParam5);
                    var _updateParam6 = _updateCommand.CreateParameter();
                    _updateParam6.ParameterName = "$dependencyFileId";
                    _updateCommand.Parameters.Add(_updateParam6);
                    var _updateParam7 = _updateCommand.CreateParameter();
                    _updateParam7.ParameterName = "$checkOutCount";
                    _updateCommand.Parameters.Add(_updateParam7);
                    var _updateParam8 = _updateCommand.CreateParameter();
                    _updateParam8.ParameterName = "$nextRunTime";
                    _updateCommand.Parameters.Add(_updateParam8);
                    var _updateParam9 = _updateCommand.CreateParameter();
                    _updateParam9.ParameterName = "$value";
                    _updateCommand.Parameters.Add(_updateParam9);
                    var _updateParam10 = _updateCommand.CreateParameter();
                    _updateParam10.ParameterName = "$checkOutStamp";
                    _updateCommand.Parameters.Add(_updateParam10);
                    var _updateParam11 = _updateCommand.CreateParameter();
                    _updateParam11.ParameterName = "$created";
                    _updateCommand.Parameters.Add(_updateParam11);
                    var _updateParam12 = _updateCommand.CreateParameter();
                    _updateParam12.ParameterName = "$modified";
                    _updateCommand.Parameters.Add(_updateParam12);
                var now = UnixTimeUtcUnique.Now();
                _updateParam1.Value = item.driveId.ToByteArray();
                _updateParam2.Value = item.fileId.ToByteArray();
                _updateParam3.Value = item.recipient;
                _updateParam4.Value = item.type;
                _updateParam5.Value = item.priority;
                _updateParam6.Value = item.dependencyFileId?.ToByteArray() ?? (object)DBNull.Value;
                _updateParam7.Value = item.checkOutCount;
                _updateParam8.Value = item.nextRunTime.milliseconds;
                _updateParam9.Value = item.value ?? (object)DBNull.Value;
                _updateParam10.Value = item.checkOutStamp?.ToByteArray() ?? (object)DBNull.Value;
                _updateParam11.Value = now.uniqueTime;
                _updateParam12.Value = now.uniqueTime;
                var count = conn.ExecuteNonQuery(_updateCommand);
                if (count > 0)
                {
                     item.modified = now;
                }
                return count;
                } // Using
        }

        public virtual int GetCount(DatabaseConnection conn)
        {
                using (var _getCountCommand = _database.CreateCommand())
                {
                    _getCountCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM outbox; PRAGMA read_uncommitted = 0;";
                    var count = conn.ExecuteNonQuery(_getCountCommand);
                    return count;
                }
        }

        // SELECT rowid,driveId,fileId,recipient,type,priority,dependencyFileId,checkOutCount,nextRunTime,value,checkOutStamp,created,modified
        public OutboxRecord ReadRecordFromReaderAll(SqliteDataReader rdr)
        {
            var result = new List<OutboxRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new OutboxRecord();

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
                item.recipient = rdr.GetString(3);
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
                item.dependencyFileId = null;
            else
            {
                bytesRead = rdr.GetBytes(6, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in dependencyFileId...");
                item.dependencyFileId = new Guid(_guid);
            }

            if (rdr.IsDBNull(7))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.checkOutCount = rdr.GetInt32(7);
            }

            if (rdr.IsDBNull(8))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.nextRunTime = new UnixTimeUtc(rdr.GetInt64(8));
            }

            if (rdr.IsDBNull(9))
                item.value = null;
            else
            {
                bytesRead = rdr.GetBytes(9, 0, _tmpbuf, 0, 65535+1);
                if (bytesRead > 65535)
                    throw new Exception("Too much data in value...");
                if (bytesRead < 0)
                    throw new Exception("Too little data in value...");
                item.value = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.value, 0, (int) bytesRead);
            }

            if (rdr.IsDBNull(10))
                item.checkOutStamp = null;
            else
            {
                bytesRead = rdr.GetBytes(10, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in checkOutStamp...");
                item.checkOutStamp = new Guid(_guid);
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

        public int Delete(DatabaseConnection conn, Guid driveId,Guid fileId,string recipient)
        {
            if (recipient == null) throw new Exception("Cannot be null");
            if (recipient?.Length < 0) throw new Exception("Too short");
            if (recipient?.Length > 65535) throw new Exception("Too long");
                using (var _delete0Command = _database.CreateCommand())
                {
                    _delete0Command.CommandText = "DELETE FROM outbox " +
                                                 "WHERE driveId = $driveId AND fileId = $fileId AND recipient = $recipient";
                    var _delete0Param1 = _delete0Command.CreateParameter();
                    _delete0Param1.ParameterName = "$driveId";
                    _delete0Command.Parameters.Add(_delete0Param1);
                    var _delete0Param2 = _delete0Command.CreateParameter();
                    _delete0Param2.ParameterName = "$fileId";
                    _delete0Command.Parameters.Add(_delete0Param2);
                    var _delete0Param3 = _delete0Command.CreateParameter();
                    _delete0Param3.ParameterName = "$recipient";
                    _delete0Command.Parameters.Add(_delete0Param3);

                _delete0Param1.Value = driveId.ToByteArray();
                _delete0Param2.Value = fileId.ToByteArray();
                _delete0Param3.Value = recipient;
                var count = conn.ExecuteNonQuery(_delete0Command);
                return count;
                } // Using
        }

        public OutboxRecord ReadRecordFromReader0(SqliteDataReader rdr, Guid driveId,Guid fileId)
        {
            var result = new List<OutboxRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new OutboxRecord();
            item.driveId = driveId;
            item.fileId = fileId;

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.recipient = rdr.GetString(0);
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
                item.dependencyFileId = null;
            else
            {
                bytesRead = rdr.GetBytes(3, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in dependencyFileId...");
                item.dependencyFileId = new Guid(_guid);
            }

            if (rdr.IsDBNull(4))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.checkOutCount = rdr.GetInt32(4);
            }

            if (rdr.IsDBNull(5))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.nextRunTime = new UnixTimeUtc(rdr.GetInt64(5));
            }

            if (rdr.IsDBNull(6))
                item.value = null;
            else
            {
                bytesRead = rdr.GetBytes(6, 0, _tmpbuf, 0, 65535+1);
                if (bytesRead > 65535)
                    throw new Exception("Too much data in value...");
                if (bytesRead < 0)
                    throw new Exception("Too little data in value...");
                item.value = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.value, 0, (int) bytesRead);
            }

            if (rdr.IsDBNull(7))
                item.checkOutStamp = null;
            else
            {
                bytesRead = rdr.GetBytes(7, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in checkOutStamp...");
                item.checkOutStamp = new Guid(_guid);
            }

            if (rdr.IsDBNull(8))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.created = new UnixTimeUtcUnique(rdr.GetInt64(8));
            }

            if (rdr.IsDBNull(9))
                item.modified = null;
            else
            {
                item.modified = new UnixTimeUtcUnique(rdr.GetInt64(9));
            }
            return item;
       }

        public List<OutboxRecord> Get(DatabaseConnection conn, Guid driveId,Guid fileId)
        {
                using (var _get0Command = _database.CreateCommand())
                {
                    _get0Command.CommandText = "SELECT recipient,type,priority,dependencyFileId,checkOutCount,nextRunTime,value,checkOutStamp,created,modified FROM outbox " +
                                                 "WHERE driveId = $driveId AND fileId = $fileId;";
                    var _get0Param1 = _get0Command.CreateParameter();
                    _get0Param1.ParameterName = "$driveId";
                    _get0Command.Parameters.Add(_get0Param1);
                    var _get0Param2 = _get0Command.CreateParameter();
                    _get0Param2.ParameterName = "$fileId";
                    _get0Command.Parameters.Add(_get0Param2);

                _get0Param1.Value = driveId.ToByteArray();
                _get0Param2.Value = fileId.ToByteArray();
                    lock (conn._lock)
                    {
                using (SqliteDataReader rdr = conn.ExecuteReader(_get0Command, System.Data.CommandBehavior.Default))
                {
                    if (!rdr.Read())
                    {
                        return null;
                    }
                    var result = new List<OutboxRecord>();
                    while (true)
                    {
                        result.Add(ReadRecordFromReader0(rdr, driveId,fileId));
                        if (!rdr.Read())
                            break;
                    }
                    return result;
                } // using
            } // lock
            } // using
        }

        public OutboxRecord ReadRecordFromReader1(SqliteDataReader rdr, Guid driveId,Guid fileId,string recipient)
        {
            if (recipient == null) throw new Exception("Cannot be null");
            if (recipient?.Length < 0) throw new Exception("Too short");
            if (recipient?.Length > 65535) throw new Exception("Too long");
            var result = new List<OutboxRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new OutboxRecord();
            item.driveId = driveId;
            item.fileId = fileId;
            item.recipient = recipient;

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.type = rdr.GetInt32(0);
            }

            if (rdr.IsDBNull(1))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.priority = rdr.GetInt32(1);
            }

            if (rdr.IsDBNull(2))
                item.dependencyFileId = null;
            else
            {
                bytesRead = rdr.GetBytes(2, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in dependencyFileId...");
                item.dependencyFileId = new Guid(_guid);
            }

            if (rdr.IsDBNull(3))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.checkOutCount = rdr.GetInt32(3);
            }

            if (rdr.IsDBNull(4))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.nextRunTime = new UnixTimeUtc(rdr.GetInt64(4));
            }

            if (rdr.IsDBNull(5))
                item.value = null;
            else
            {
                bytesRead = rdr.GetBytes(5, 0, _tmpbuf, 0, 65535+1);
                if (bytesRead > 65535)
                    throw new Exception("Too much data in value...");
                if (bytesRead < 0)
                    throw new Exception("Too little data in value...");
                item.value = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.value, 0, (int) bytesRead);
            }

            if (rdr.IsDBNull(6))
                item.checkOutStamp = null;
            else
            {
                bytesRead = rdr.GetBytes(6, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in checkOutStamp...");
                item.checkOutStamp = new Guid(_guid);
            }

            if (rdr.IsDBNull(7))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.created = new UnixTimeUtcUnique(rdr.GetInt64(7));
            }

            if (rdr.IsDBNull(8))
                item.modified = null;
            else
            {
                item.modified = new UnixTimeUtcUnique(rdr.GetInt64(8));
            }
            return item;
       }

        public OutboxRecord Get(DatabaseConnection conn, Guid driveId,Guid fileId,string recipient)
        {
            if (recipient == null) throw new Exception("Cannot be null");
            if (recipient?.Length < 0) throw new Exception("Too short");
            if (recipient?.Length > 65535) throw new Exception("Too long");
                using (var _get1Command = _database.CreateCommand())
                {
                    _get1Command.CommandText = "SELECT type,priority,dependencyFileId,checkOutCount,nextRunTime,value,checkOutStamp,created,modified FROM outbox " +
                                                 "WHERE driveId = $driveId AND fileId = $fileId AND recipient = $recipient LIMIT 1;";
                    var _get1Param1 = _get1Command.CreateParameter();
                    _get1Param1.ParameterName = "$driveId";
                    _get1Command.Parameters.Add(_get1Param1);
                    var _get1Param2 = _get1Command.CreateParameter();
                    _get1Param2.ParameterName = "$fileId";
                    _get1Command.Parameters.Add(_get1Param2);
                    var _get1Param3 = _get1Command.CreateParameter();
                    _get1Param3.ParameterName = "$recipient";
                    _get1Command.Parameters.Add(_get1Param3);

                _get1Param1.Value = driveId.ToByteArray();
                _get1Param2.Value = fileId.ToByteArray();
                _get1Param3.Value = recipient;
                    lock (conn._lock)
                    {
                using (SqliteDataReader rdr = conn.ExecuteReader(_get1Command, System.Data.CommandBehavior.SingleRow))
                {
                    if (!rdr.Read())
                    {
                        return null;
                    }
                    var r = ReadRecordFromReader1(rdr, driveId,fileId,recipient);
                    return r;
                } // using
            } // lock
            } // using
        }

    }
}
