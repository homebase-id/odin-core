using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Youverse.Core.Identity;

namespace Youverse.Core.Storage.Sqlite.IdentityDatabase
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
        private SqliteCommand _delete0Command = null;
        private static Object _delete0Lock = new Object();
        private SqliteParameter _delete0Param1 = null;
        private SqliteParameter _delete0Param2 = null;
        private SqliteParameter _delete0Param3 = null;
        private SqliteCommand _get0Command = null;
        private static Object _get0Lock = new Object();
        private SqliteParameter _get0Param1 = null;
        private SqliteParameter _get0Param2 = null;
        private SqliteParameter _get0Param3 = null;

        public TableFeedDistributionOutboxCRUD(IdentityDatabase db) : base(db)
        {
        }

        ~TableFeedDistributionOutboxCRUD()
        {
            if (_disposed == false) throw new Exception("TableFeedDistributionOutboxCRUD Not disposed properly");
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
                    cmd.CommandText = "DROP TABLE IF EXISTS feedDistributionOutbox;";
                    _database.ExecuteNonQuery(cmd);
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
                _database.ExecuteNonQuery(cmd);
                _database.Commit();
            }
        }

        public virtual int Insert(FeedDistributionOutboxRecord item)
        {
            lock (_insertLock)
            {
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand();
                    _insertCommand.CommandText = "INSERT INTO feedDistributionOutbox (fileId,driveId,recipient,timeStamp,value,popStamp,created,modified) " +
                                                 "VALUES ($fileId,$driveId,$recipient,$timeStamp,$value,$popStamp,$created,$modified)";
                    _insertParam1 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam1);
                    _insertParam1.ParameterName = "$fileId";
                    _insertParam2 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam2);
                    _insertParam2.ParameterName = "$driveId";
                    _insertParam3 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam3);
                    _insertParam3.ParameterName = "$recipient";
                    _insertParam4 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam4);
                    _insertParam4.ParameterName = "$timeStamp";
                    _insertParam5 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam5);
                    _insertParam5.ParameterName = "$value";
                    _insertParam6 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam6);
                    _insertParam6.ParameterName = "$popStamp";
                    _insertParam7 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam7);
                    _insertParam7.ParameterName = "$created";
                    _insertParam8 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam8);
                    _insertParam8.ParameterName = "$modified";
                    _insertCommand.Prepare();
                }
                _insertParam1.Value = item.fileId.ToByteArray();
                _insertParam2.Value = item.driveId.ToByteArray();
                _insertParam3.Value = item.recipient;
                _insertParam4.Value = item.timeStamp.milliseconds;
                _insertParam5.Value = item.value ?? (object)DBNull.Value;
                _insertParam6.Value = item.popStamp?.ToByteArray() ?? (object)DBNull.Value;
                _insertParam7.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _insertParam8.Value = DBNull.Value;
                return _database.ExecuteNonQuery(_insertCommand);
            } // Lock
        }

        public virtual int Upsert(FeedDistributionOutboxRecord item)
        {
            lock (_upsertLock)
            {
                if (_upsertCommand == null)
                {
                    _upsertCommand = _database.CreateCommand();
                    _upsertCommand.CommandText = "INSERT INTO feedDistributionOutbox (fileId,driveId,recipient,timeStamp,value,popStamp,created,modified) " +
                                                 "VALUES ($fileId,$driveId,$recipient,$timeStamp,$value,$popStamp,$created,$modified)"+
                                                 "ON CONFLICT (fileId,driveId,recipient) DO UPDATE "+
                                                 "SET timeStamp = $timeStamp,value = $value,popStamp = $popStamp,modified = $modified;";
                    _upsertParam1 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam1);
                    _upsertParam1.ParameterName = "$fileId";
                    _upsertParam2 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam2);
                    _upsertParam2.ParameterName = "$driveId";
                    _upsertParam3 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam3);
                    _upsertParam3.ParameterName = "$recipient";
                    _upsertParam4 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam4);
                    _upsertParam4.ParameterName = "$timeStamp";
                    _upsertParam5 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam5);
                    _upsertParam5.ParameterName = "$value";
                    _upsertParam6 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam6);
                    _upsertParam6.ParameterName = "$popStamp";
                    _upsertParam7 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam7);
                    _upsertParam7.ParameterName = "$created";
                    _upsertParam8 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam8);
                    _upsertParam8.ParameterName = "$modified";
                    _upsertCommand.Prepare();
                }
                _upsertParam1.Value = item.fileId.ToByteArray();
                _upsertParam2.Value = item.driveId.ToByteArray();
                _upsertParam3.Value = item.recipient;
                _upsertParam4.Value = item.timeStamp.milliseconds;
                _upsertParam5.Value = item.value ?? (object)DBNull.Value;
                _upsertParam6.Value = item.popStamp?.ToByteArray() ?? (object)DBNull.Value;
                _upsertParam7.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _upsertParam8.Value = UnixTimeUtcUnique.Now().uniqueTime;
                return _database.ExecuteNonQuery(_upsertCommand);
            } // Lock
        }

        public virtual int Update(FeedDistributionOutboxRecord item)
        {
            lock (_updateLock)
            {
                if (_updateCommand == null)
                {
                    _updateCommand = _database.CreateCommand();
                    _updateCommand.CommandText = "UPDATE feedDistributionOutbox " +
                                                 "SET timeStamp = $timeStamp,value = $value,popStamp = $popStamp,modified = $modified "+
                                                 "WHERE (fileId = $fileId,driveId = $driveId,recipient = $recipient)";
                    _updateParam1 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam1);
                    _updateParam1.ParameterName = "$fileId";
                    _updateParam2 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam2);
                    _updateParam2.ParameterName = "$driveId";
                    _updateParam3 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam3);
                    _updateParam3.ParameterName = "$recipient";
                    _updateParam4 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam4);
                    _updateParam4.ParameterName = "$timeStamp";
                    _updateParam5 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam5);
                    _updateParam5.ParameterName = "$value";
                    _updateParam6 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam6);
                    _updateParam6.ParameterName = "$popStamp";
                    _updateParam7 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam7);
                    _updateParam7.ParameterName = "$created";
                    _updateParam8 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam8);
                    _updateParam8.ParameterName = "$modified";
                    _updateCommand.Prepare();
                }
                _updateParam1.Value = item.fileId.ToByteArray();
                _updateParam2.Value = item.driveId.ToByteArray();
                _updateParam3.Value = item.recipient;
                _updateParam4.Value = item.timeStamp.milliseconds;
                _updateParam5.Value = item.value ?? (object)DBNull.Value;
                _updateParam6.Value = item.popStamp?.ToByteArray() ?? (object)DBNull.Value;
                _updateParam7.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _updateParam8.Value = UnixTimeUtcUnique.Now().uniqueTime;
                return _database.ExecuteNonQuery(_updateCommand);
            } // Lock
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

        public int Delete(Guid fileId,Guid driveId,string recipient)
        {
            if (recipient == null) throw new Exception("Cannot be null");
            if (recipient?.Length < 0) throw new Exception("Too short");
            if (recipient?.Length > 65535) throw new Exception("Too long");
            lock (_delete0Lock)
            {
                if (_delete0Command == null)
                {
                    _delete0Command = _database.CreateCommand();
                    _delete0Command.CommandText = "DELETE FROM feedDistributionOutbox " +
                                                 "WHERE fileId = $fileId AND driveId = $driveId AND recipient = $recipient";
                    _delete0Param1 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param1);
                    _delete0Param1.ParameterName = "$fileId";
                    _delete0Param2 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param2);
                    _delete0Param2.ParameterName = "$driveId";
                    _delete0Param3 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param3);
                    _delete0Param3.ParameterName = "$recipient";
                    _delete0Command.Prepare();
                }
                _delete0Param1.Value = fileId.ToByteArray();
                _delete0Param2.Value = driveId.ToByteArray();
                _delete0Param3.Value = recipient;
                return _database.ExecuteNonQuery(_delete0Command);
            } // Lock
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

        public FeedDistributionOutboxRecord Get(Guid fileId,Guid driveId,string recipient)
        {
            if (recipient == null) throw new Exception("Cannot be null");
            if (recipient?.Length < 0) throw new Exception("Too short");
            if (recipient?.Length > 65535) throw new Exception("Too long");
            lock (_get0Lock)
            {
                if (_get0Command == null)
                {
                    _get0Command = _database.CreateCommand();
                    _get0Command.CommandText = "SELECT timeStamp,value,popStamp,created,modified FROM feedDistributionOutbox " +
                                                 "WHERE fileId = $fileId AND driveId = $driveId AND recipient = $recipient LIMIT 1;";
                    _get0Param1 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param1);
                    _get0Param1.ParameterName = "$fileId";
                    _get0Param2 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param2);
                    _get0Param2.ParameterName = "$driveId";
                    _get0Param3 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param3);
                    _get0Param3.ParameterName = "$recipient";
                    _get0Command.Prepare();
                }
                _get0Param1.Value = fileId.ToByteArray();
                _get0Param2.Value = driveId.ToByteArray();
                _get0Param3.Value = recipient;
                using (SqliteDataReader rdr = _database.ExecuteReader(_get0Command, System.Data.CommandBehavior.SingleRow))
                {
                    if (!rdr.Read())
                        return null;
                    return ReadRecordFromReader0(rdr, fileId,driveId,recipient);
                } // using
            } // lock
        }

    }
}
