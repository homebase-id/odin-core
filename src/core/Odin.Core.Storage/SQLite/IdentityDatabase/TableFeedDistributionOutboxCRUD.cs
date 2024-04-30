using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;
using Odin.Core.Identity;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class FeedDistributionOutboxRecord
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
    } // End of class FeedDistributionOutboxRecord

    public class TableFeedDistributionOutboxCRUD : TableBase
    {
        private bool _disposed = false;

        public TableFeedDistributionOutboxCRUD(IdentityDatabase db, CacheHelper cache) : base(db)
        {
        }

        ~TableFeedDistributionOutboxCRUD()
        {
            if (_disposed == false) throw new Exception("TableFeedDistributionOutboxCRUD Not disposed properly");
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
                       cmd.CommandText = "DROP TABLE IF EXISTS feedDistributionOutbox;";
                       conn.ExecuteNonQuery(cmd);
                    }
                    cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS feedDistributionOutbox("
                     +"fileId BLOB NOT NULL, "
                     +"driveId BLOB NOT NULL, "
                     +"recipient STRING NOT NULL, "
                     +"timeStamp INT NOT NULL, "
                     +"value BLOB , "
                     +"popStamp BLOB , "
                     +"created INT NOT NULL, "
                     +"modified INT  "
                     +", PRIMARY KEY (fileId,driveId,recipient)"
                     +");"
                     +"CREATE INDEX IF NOT EXISTS Idx0TableFeedDistributionOutboxCRUD ON feedDistributionOutbox(timeStamp);"
                     +"CREATE INDEX IF NOT EXISTS Idx1TableFeedDistributionOutboxCRUD ON feedDistributionOutbox(popStamp);"
                     ;
                    conn.ExecuteNonQuery(cmd);
            }
        }

        public virtual int Insert(DatabaseConnection conn, FeedDistributionOutboxRecord item)
        {
                using (var _insertCommand = _database.CreateCommand())
                {
                    _insertCommand.CommandText = "INSERT INTO feedDistributionOutbox (fileId,driveId,recipient,timeStamp,value,popStamp,created,modified) " +
                                                 "VALUES ($fileId,$driveId,$recipient,$timeStamp,$value,$popStamp,$created,$modified)";
                    var _insertParam1 = _insertCommand.CreateParameter();
                    _insertParam1.ParameterName = "$fileId";
                    _insertCommand.Parameters.Add(_insertParam1);
                    var _insertParam2 = _insertCommand.CreateParameter();
                    _insertParam2.ParameterName = "$driveId";
                    _insertCommand.Parameters.Add(_insertParam2);
                    var _insertParam3 = _insertCommand.CreateParameter();
                    _insertParam3.ParameterName = "$recipient";
                    _insertCommand.Parameters.Add(_insertParam3);
                    var _insertParam4 = _insertCommand.CreateParameter();
                    _insertParam4.ParameterName = "$timeStamp";
                    _insertCommand.Parameters.Add(_insertParam4);
                    var _insertParam5 = _insertCommand.CreateParameter();
                    _insertParam5.ParameterName = "$value";
                    _insertCommand.Parameters.Add(_insertParam5);
                    var _insertParam6 = _insertCommand.CreateParameter();
                    _insertParam6.ParameterName = "$popStamp";
                    _insertCommand.Parameters.Add(_insertParam6);
                    var _insertParam7 = _insertCommand.CreateParameter();
                    _insertParam7.ParameterName = "$created";
                    _insertCommand.Parameters.Add(_insertParam7);
                    var _insertParam8 = _insertCommand.CreateParameter();
                    _insertParam8.ParameterName = "$modified";
                    _insertCommand.Parameters.Add(_insertParam8);
                _insertParam1.Value = item.fileId.ToByteArray();
                _insertParam2.Value = item.driveId.ToByteArray();
                _insertParam3.Value = item.recipient;
                _insertParam4.Value = item.timeStamp.milliseconds;
                _insertParam5.Value = item.value ?? (object)DBNull.Value;
                _insertParam6.Value = item.popStamp?.ToByteArray() ?? (object)DBNull.Value;
                var now = UnixTimeUtcUnique.Now();
                _insertParam7.Value = now.uniqueTime;
                item.modified = null;
                _insertParam8.Value = DBNull.Value;
                var count = conn.ExecuteNonQuery(_insertCommand);
                if (count > 0)
                 {
                     item.created = now;
                 }
                return count;
                } // Using
        }

        public virtual int Upsert(DatabaseConnection conn, FeedDistributionOutboxRecord item)
        {
                using (var _upsertCommand = _database.CreateCommand())
                {
                    _upsertCommand.CommandText = "INSERT INTO feedDistributionOutbox (fileId,driveId,recipient,timeStamp,value,popStamp,created) " +
                                                 "VALUES ($fileId,$driveId,$recipient,$timeStamp,$value,$popStamp,$created)"+
                                                 "ON CONFLICT (fileId,driveId,recipient) DO UPDATE "+
                                                 "SET timeStamp = $timeStamp,value = $value,popStamp = $popStamp,modified = $modified "+
                                                 "RETURNING created, modified;";
                    var _upsertParam1 = _upsertCommand.CreateParameter();
                    _upsertParam1.ParameterName = "$fileId";
                    _upsertCommand.Parameters.Add(_upsertParam1);
                    var _upsertParam2 = _upsertCommand.CreateParameter();
                    _upsertParam2.ParameterName = "$driveId";
                    _upsertCommand.Parameters.Add(_upsertParam2);
                    var _upsertParam3 = _upsertCommand.CreateParameter();
                    _upsertParam3.ParameterName = "$recipient";
                    _upsertCommand.Parameters.Add(_upsertParam3);
                    var _upsertParam4 = _upsertCommand.CreateParameter();
                    _upsertParam4.ParameterName = "$timeStamp";
                    _upsertCommand.Parameters.Add(_upsertParam4);
                    var _upsertParam5 = _upsertCommand.CreateParameter();
                    _upsertParam5.ParameterName = "$value";
                    _upsertCommand.Parameters.Add(_upsertParam5);
                    var _upsertParam6 = _upsertCommand.CreateParameter();
                    _upsertParam6.ParameterName = "$popStamp";
                    _upsertCommand.Parameters.Add(_upsertParam6);
                    var _upsertParam7 = _upsertCommand.CreateParameter();
                    _upsertParam7.ParameterName = "$created";
                    _upsertCommand.Parameters.Add(_upsertParam7);
                    var _upsertParam8 = _upsertCommand.CreateParameter();
                    _upsertParam8.ParameterName = "$modified";
                    _upsertCommand.Parameters.Add(_upsertParam8);
                var now = UnixTimeUtcUnique.Now();
                _upsertParam1.Value = item.fileId.ToByteArray();
                _upsertParam2.Value = item.driveId.ToByteArray();
                _upsertParam3.Value = item.recipient;
                _upsertParam4.Value = item.timeStamp.milliseconds;
                _upsertParam5.Value = item.value ?? (object)DBNull.Value;
                _upsertParam6.Value = item.popStamp?.ToByteArray() ?? (object)DBNull.Value;
                _upsertParam7.Value = now.uniqueTime;
                _upsertParam8.Value = now.uniqueTime;
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

        public virtual int Update(DatabaseConnection conn, FeedDistributionOutboxRecord item)
        {
                using (var _updateCommand = _database.CreateCommand())
                {
                    _updateCommand.CommandText = "UPDATE feedDistributionOutbox " +
                                                 "SET timeStamp = $timeStamp,value = $value,popStamp = $popStamp,modified = $modified "+
                                                 "WHERE (fileId = $fileId,driveId = $driveId,recipient = $recipient)";
                    var _updateParam1 = _updateCommand.CreateParameter();
                    _updateParam1.ParameterName = "$fileId";
                    _updateCommand.Parameters.Add(_updateParam1);
                    var _updateParam2 = _updateCommand.CreateParameter();
                    _updateParam2.ParameterName = "$driveId";
                    _updateCommand.Parameters.Add(_updateParam2);
                    var _updateParam3 = _updateCommand.CreateParameter();
                    _updateParam3.ParameterName = "$recipient";
                    _updateCommand.Parameters.Add(_updateParam3);
                    var _updateParam4 = _updateCommand.CreateParameter();
                    _updateParam4.ParameterName = "$timeStamp";
                    _updateCommand.Parameters.Add(_updateParam4);
                    var _updateParam5 = _updateCommand.CreateParameter();
                    _updateParam5.ParameterName = "$value";
                    _updateCommand.Parameters.Add(_updateParam5);
                    var _updateParam6 = _updateCommand.CreateParameter();
                    _updateParam6.ParameterName = "$popStamp";
                    _updateCommand.Parameters.Add(_updateParam6);
                    var _updateParam7 = _updateCommand.CreateParameter();
                    _updateParam7.ParameterName = "$created";
                    _updateCommand.Parameters.Add(_updateParam7);
                    var _updateParam8 = _updateCommand.CreateParameter();
                    _updateParam8.ParameterName = "$modified";
                    _updateCommand.Parameters.Add(_updateParam8);
                var now = UnixTimeUtcUnique.Now();
                _updateParam1.Value = item.fileId.ToByteArray();
                _updateParam2.Value = item.driveId.ToByteArray();
                _updateParam3.Value = item.recipient;
                _updateParam4.Value = item.timeStamp.milliseconds;
                _updateParam5.Value = item.value ?? (object)DBNull.Value;
                _updateParam6.Value = item.popStamp?.ToByteArray() ?? (object)DBNull.Value;
                _updateParam7.Value = now.uniqueTime;
                _updateParam8.Value = now.uniqueTime;
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
                    _getCountCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM feedDistributionOutbox; PRAGMA read_uncommitted = 0;";
                    var count = conn.ExecuteNonQuery(_getCountCommand);
                    return count;
                }
        }

        // SELECT fileId,driveId,recipient,timeStamp,value,popStamp,created,modified
        public FeedDistributionOutboxRecord ReadRecordFromReaderAll(SqliteDataReader rdr)
        {
            var result = new List<FeedDistributionOutboxRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new FeedDistributionOutboxRecord();

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
                bytesRead = rdr.GetBytes(1, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in driveId...");
                item.driveId = new Guid(_guid);
            }

            if (rdr.IsDBNull(2))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.recipient = rdr.GetString(2);
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

        public int Delete(DatabaseConnection conn, Guid fileId,Guid driveId,string recipient)
        {
            if (recipient == null) throw new Exception("Cannot be null");
            if (recipient?.Length < 0) throw new Exception("Too short");
            if (recipient?.Length > 65535) throw new Exception("Too long");
                using (var _delete0Command = _database.CreateCommand())
                {
                    _delete0Command.CommandText = "DELETE FROM feedDistributionOutbox " +
                                                 "WHERE fileId = $fileId AND driveId = $driveId AND recipient = $recipient";
                    var _delete0Param1 = _delete0Command.CreateParameter();
                    _delete0Param1.ParameterName = "$fileId";
                    _delete0Command.Parameters.Add(_delete0Param1);
                    var _delete0Param2 = _delete0Command.CreateParameter();
                    _delete0Param2.ParameterName = "$driveId";
                    _delete0Command.Parameters.Add(_delete0Param2);
                    var _delete0Param3 = _delete0Command.CreateParameter();
                    _delete0Param3.ParameterName = "$recipient";
                    _delete0Command.Parameters.Add(_delete0Param3);

                _delete0Param1.Value = fileId.ToByteArray();
                _delete0Param2.Value = driveId.ToByteArray();
                _delete0Param3.Value = recipient;
                var count = conn.ExecuteNonQuery(_delete0Command);
                return count;
                } // Using
        }

        public FeedDistributionOutboxRecord ReadRecordFromReader0(SqliteDataReader rdr, Guid fileId,Guid driveId,string recipient)
        {
            if (recipient == null) throw new Exception("Cannot be null");
            if (recipient?.Length < 0) throw new Exception("Too short");
            if (recipient?.Length > 65535) throw new Exception("Too long");
            var result = new List<FeedDistributionOutboxRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new FeedDistributionOutboxRecord();
            item.fileId = fileId;
            item.driveId = driveId;
            item.recipient = recipient;

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.timeStamp = new UnixTimeUtc(rdr.GetInt64(0));
            }

            if (rdr.IsDBNull(1))
                item.value = null;
            else
            {
                bytesRead = rdr.GetBytes(1, 0, _tmpbuf, 0, 65535+1);
                if (bytesRead > 65535)
                    throw new Exception("Too much data in value...");
                if (bytesRead < 0)
                    throw new Exception("Too little data in value...");
                item.value = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.value, 0, (int) bytesRead);
            }

            if (rdr.IsDBNull(2))
                item.popStamp = null;
            else
            {
                bytesRead = rdr.GetBytes(2, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in popStamp...");
                item.popStamp = new Guid(_guid);
            }

            if (rdr.IsDBNull(3))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.created = new UnixTimeUtcUnique(rdr.GetInt64(3));
            }

            if (rdr.IsDBNull(4))
                item.modified = null;
            else
            {
                item.modified = new UnixTimeUtcUnique(rdr.GetInt64(4));
            }
            return item;
       }

        public FeedDistributionOutboxRecord Get(DatabaseConnection conn, Guid fileId,Guid driveId,string recipient)
        {
            if (recipient == null) throw new Exception("Cannot be null");
            if (recipient?.Length < 0) throw new Exception("Too short");
            if (recipient?.Length > 65535) throw new Exception("Too long");
                using (var _get0Command = _database.CreateCommand())
                {
                    _get0Command.CommandText = "SELECT timeStamp,value,popStamp,created,modified FROM feedDistributionOutbox " +
                                                 "WHERE fileId = $fileId AND driveId = $driveId AND recipient = $recipient LIMIT 1;";
                    var _get0Param1 = _get0Command.CreateParameter();
                    _get0Param1.ParameterName = "$fileId";
                    _get0Command.Parameters.Add(_get0Param1);
                    var _get0Param2 = _get0Command.CreateParameter();
                    _get0Param2.ParameterName = "$driveId";
                    _get0Command.Parameters.Add(_get0Param2);
                    var _get0Param3 = _get0Command.CreateParameter();
                    _get0Param3.ParameterName = "$recipient";
                    _get0Command.Parameters.Add(_get0Param3);

                _get0Param1.Value = fileId.ToByteArray();
                _get0Param2.Value = driveId.ToByteArray();
                _get0Param3.Value = recipient;
                    lock (conn._lock)
                    {
                using (SqliteDataReader rdr = conn.ExecuteReader(_get0Command, System.Data.CommandBehavior.SingleRow))
                {
                    if (!rdr.Read())
                    {
                        return null;
                    }
                    var r = ReadRecordFromReader0(rdr, fileId,driveId,recipient);
                    return r;
                } // using
            } // lock
            } // using
        }

    }
}
