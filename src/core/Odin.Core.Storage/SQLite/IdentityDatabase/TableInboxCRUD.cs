using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;
using Odin.Core.Identity;
using System.Runtime.CompilerServices;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class InboxRecord
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

        public TableInboxCRUD(IdentityDatabase db, CacheHelper cache) : base(db, "inbox")
        {
        }

        ~TableInboxCRUD()
        {
            if (_disposed == false) throw new Exception("TableInboxCRUD Not disposed properly");
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
                       cmd.CommandText = "DROP TABLE IF EXISTS inbox;";
                       conn.ExecuteNonQuery(cmd);
                    }
                    cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS inbox("
                     +"identityId BLOB NOT NULL, "
                     +"fileId BLOB NOT NULL UNIQUE, "
                     +"boxId BLOB NOT NULL, "
                     +"priority INT NOT NULL, "
                     +"timeStamp INT NOT NULL, "
                     +"value BLOB , "
                     +"popStamp BLOB , "
                     +"created INT NOT NULL, "
                     +"modified INT  "
                     +", PRIMARY KEY (identityId,fileId)"
                     +");"
                     +"CREATE INDEX IF NOT EXISTS Idx0TableInboxCRUD ON inbox(identityId,timeStamp);"
                     +"CREATE INDEX IF NOT EXISTS Idx1TableInboxCRUD ON inbox(identityId,boxId);"
                     +"CREATE INDEX IF NOT EXISTS Idx2TableInboxCRUD ON inbox(identityId,popStamp);"
                     ;
                    conn.ExecuteNonQuery(cmd);
            }
        }

        internal virtual int Insert(DatabaseConnection conn, InboxRecord item)
        {
            using (var _insertCommand = _database.CreateCommand())
            {
                _insertCommand.CommandText = "INSERT INTO inbox (identityId,fileId,boxId,priority,timeStamp,value,popStamp,created,modified) " +
                                             "VALUES (@identityId,@fileId,@boxId,@priority,@timeStamp,@value,@popStamp,@created,@modified)";
                var _insertParam1 = _insertCommand.CreateParameter();
                _insertParam1.ParameterName = "@identityId";
                _insertCommand.Parameters.Add(_insertParam1);
                var _insertParam2 = _insertCommand.CreateParameter();
                _insertParam2.ParameterName = "@fileId";
                _insertCommand.Parameters.Add(_insertParam2);
                var _insertParam3 = _insertCommand.CreateParameter();
                _insertParam3.ParameterName = "@boxId";
                _insertCommand.Parameters.Add(_insertParam3);
                var _insertParam4 = _insertCommand.CreateParameter();
                _insertParam4.ParameterName = "@priority";
                _insertCommand.Parameters.Add(_insertParam4);
                var _insertParam5 = _insertCommand.CreateParameter();
                _insertParam5.ParameterName = "@timeStamp";
                _insertCommand.Parameters.Add(_insertParam5);
                var _insertParam6 = _insertCommand.CreateParameter();
                _insertParam6.ParameterName = "@value";
                _insertCommand.Parameters.Add(_insertParam6);
                var _insertParam7 = _insertCommand.CreateParameter();
                _insertParam7.ParameterName = "@popStamp";
                _insertCommand.Parameters.Add(_insertParam7);
                var _insertParam8 = _insertCommand.CreateParameter();
                _insertParam8.ParameterName = "@created";
                _insertCommand.Parameters.Add(_insertParam8);
                var _insertParam9 = _insertCommand.CreateParameter();
                _insertParam9.ParameterName = "@modified";
                _insertCommand.Parameters.Add(_insertParam9);
                _insertParam1.Value = item.identityId.ToByteArray();
                _insertParam2.Value = item.fileId.ToByteArray();
                _insertParam3.Value = item.boxId.ToByteArray();
                _insertParam4.Value = item.priority;
                _insertParam5.Value = item.timeStamp.milliseconds;
                _insertParam6.Value = item.value ?? (object)DBNull.Value;
                _insertParam7.Value = item.popStamp?.ToByteArray() ?? (object)DBNull.Value;
                var now = UnixTimeUtcUnique.Now();
                _insertParam8.Value = now.uniqueTime;
                item.modified = null;
                _insertParam9.Value = DBNull.Value;
                var count = conn.ExecuteNonQuery(_insertCommand);
                if (count > 0)
                {
                     item.created = now;
                }
                return count;
            } // Using
        }

        internal virtual int TryInsert(DatabaseConnection conn, InboxRecord item)
        {
            using (var _insertCommand = _database.CreateCommand())
            {
                _insertCommand.CommandText = "INSERT OR IGNORE INTO inbox (identityId,fileId,boxId,priority,timeStamp,value,popStamp,created,modified) " +
                                             "VALUES (@identityId,@fileId,@boxId,@priority,@timeStamp,@value,@popStamp,@created,@modified)";
                var _insertParam1 = _insertCommand.CreateParameter();
                _insertParam1.ParameterName = "@identityId";
                _insertCommand.Parameters.Add(_insertParam1);
                var _insertParam2 = _insertCommand.CreateParameter();
                _insertParam2.ParameterName = "@fileId";
                _insertCommand.Parameters.Add(_insertParam2);
                var _insertParam3 = _insertCommand.CreateParameter();
                _insertParam3.ParameterName = "@boxId";
                _insertCommand.Parameters.Add(_insertParam3);
                var _insertParam4 = _insertCommand.CreateParameter();
                _insertParam4.ParameterName = "@priority";
                _insertCommand.Parameters.Add(_insertParam4);
                var _insertParam5 = _insertCommand.CreateParameter();
                _insertParam5.ParameterName = "@timeStamp";
                _insertCommand.Parameters.Add(_insertParam5);
                var _insertParam6 = _insertCommand.CreateParameter();
                _insertParam6.ParameterName = "@value";
                _insertCommand.Parameters.Add(_insertParam6);
                var _insertParam7 = _insertCommand.CreateParameter();
                _insertParam7.ParameterName = "@popStamp";
                _insertCommand.Parameters.Add(_insertParam7);
                var _insertParam8 = _insertCommand.CreateParameter();
                _insertParam8.ParameterName = "@created";
                _insertCommand.Parameters.Add(_insertParam8);
                var _insertParam9 = _insertCommand.CreateParameter();
                _insertParam9.ParameterName = "@modified";
                _insertCommand.Parameters.Add(_insertParam9);
                _insertParam1.Value = item.identityId.ToByteArray();
                _insertParam2.Value = item.fileId.ToByteArray();
                _insertParam3.Value = item.boxId.ToByteArray();
                _insertParam4.Value = item.priority;
                _insertParam5.Value = item.timeStamp.milliseconds;
                _insertParam6.Value = item.value ?? (object)DBNull.Value;
                _insertParam7.Value = item.popStamp?.ToByteArray() ?? (object)DBNull.Value;
                var now = UnixTimeUtcUnique.Now();
                _insertParam8.Value = now.uniqueTime;
                item.modified = null;
                _insertParam9.Value = DBNull.Value;
                var count = conn.ExecuteNonQuery(_insertCommand);
                if (count > 0)
                {
                    item.created = now;
                }
                return count;
            } // Using
        }

        internal virtual int Upsert(DatabaseConnection conn, InboxRecord item)
        {
            using (var _upsertCommand = _database.CreateCommand())
            {
                _upsertCommand.CommandText = "INSERT INTO inbox (identityId,fileId,boxId,priority,timeStamp,value,popStamp,created) " +
                                             "VALUES (@identityId,@fileId,@boxId,@priority,@timeStamp,@value,@popStamp,@created)"+
                                             "ON CONFLICT (identityId,fileId) DO UPDATE "+
                                             "SET boxId = @boxId,priority = @priority,timeStamp = @timeStamp,value = @value,popStamp = @popStamp,modified = @modified "+
                                             "RETURNING created, modified;";
                var _upsertParam1 = _upsertCommand.CreateParameter();
                _upsertParam1.ParameterName = "@identityId";
                _upsertCommand.Parameters.Add(_upsertParam1);
                var _upsertParam2 = _upsertCommand.CreateParameter();
                _upsertParam2.ParameterName = "@fileId";
                _upsertCommand.Parameters.Add(_upsertParam2);
                var _upsertParam3 = _upsertCommand.CreateParameter();
                _upsertParam3.ParameterName = "@boxId";
                _upsertCommand.Parameters.Add(_upsertParam3);
                var _upsertParam4 = _upsertCommand.CreateParameter();
                _upsertParam4.ParameterName = "@priority";
                _upsertCommand.Parameters.Add(_upsertParam4);
                var _upsertParam5 = _upsertCommand.CreateParameter();
                _upsertParam5.ParameterName = "@timeStamp";
                _upsertCommand.Parameters.Add(_upsertParam5);
                var _upsertParam6 = _upsertCommand.CreateParameter();
                _upsertParam6.ParameterName = "@value";
                _upsertCommand.Parameters.Add(_upsertParam6);
                var _upsertParam7 = _upsertCommand.CreateParameter();
                _upsertParam7.ParameterName = "@popStamp";
                _upsertCommand.Parameters.Add(_upsertParam7);
                var _upsertParam8 = _upsertCommand.CreateParameter();
                _upsertParam8.ParameterName = "@created";
                _upsertCommand.Parameters.Add(_upsertParam8);
                var _upsertParam9 = _upsertCommand.CreateParameter();
                _upsertParam9.ParameterName = "@modified";
                _upsertCommand.Parameters.Add(_upsertParam9);
                var now = UnixTimeUtcUnique.Now();
                _upsertParam1.Value = item.identityId.ToByteArray();
                _upsertParam2.Value = item.fileId.ToByteArray();
                _upsertParam3.Value = item.boxId.ToByteArray();
                _upsertParam4.Value = item.priority;
                _upsertParam5.Value = item.timeStamp.milliseconds;
                _upsertParam6.Value = item.value ?? (object)DBNull.Value;
                _upsertParam7.Value = item.popStamp?.ToByteArray() ?? (object)DBNull.Value;
                _upsertParam8.Value = now.uniqueTime;
                _upsertParam9.Value = now.uniqueTime;
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

        internal virtual int Update(DatabaseConnection conn, InboxRecord item)
        {
            using (var _updateCommand = _database.CreateCommand())
            {
                _updateCommand.CommandText = "UPDATE inbox " +
                                             "SET boxId = @boxId,priority = @priority,timeStamp = @timeStamp,value = @value,popStamp = @popStamp,modified = @modified "+
                                             "WHERE (identityId = @identityId AND fileId = @fileId)";
                var _updateParam1 = _updateCommand.CreateParameter();
                _updateParam1.ParameterName = "@identityId";
                _updateCommand.Parameters.Add(_updateParam1);
                var _updateParam2 = _updateCommand.CreateParameter();
                _updateParam2.ParameterName = "@fileId";
                _updateCommand.Parameters.Add(_updateParam2);
                var _updateParam3 = _updateCommand.CreateParameter();
                _updateParam3.ParameterName = "@boxId";
                _updateCommand.Parameters.Add(_updateParam3);
                var _updateParam4 = _updateCommand.CreateParameter();
                _updateParam4.ParameterName = "@priority";
                _updateCommand.Parameters.Add(_updateParam4);
                var _updateParam5 = _updateCommand.CreateParameter();
                _updateParam5.ParameterName = "@timeStamp";
                _updateCommand.Parameters.Add(_updateParam5);
                var _updateParam6 = _updateCommand.CreateParameter();
                _updateParam6.ParameterName = "@value";
                _updateCommand.Parameters.Add(_updateParam6);
                var _updateParam7 = _updateCommand.CreateParameter();
                _updateParam7.ParameterName = "@popStamp";
                _updateCommand.Parameters.Add(_updateParam7);
                var _updateParam8 = _updateCommand.CreateParameter();
                _updateParam8.ParameterName = "@created";
                _updateCommand.Parameters.Add(_updateParam8);
                var _updateParam9 = _updateCommand.CreateParameter();
                _updateParam9.ParameterName = "@modified";
                _updateCommand.Parameters.Add(_updateParam9);
             var now = UnixTimeUtcUnique.Now();
                _updateParam1.Value = item.identityId.ToByteArray();
                _updateParam2.Value = item.fileId.ToByteArray();
                _updateParam3.Value = item.boxId.ToByteArray();
                _updateParam4.Value = item.priority;
                _updateParam5.Value = item.timeStamp.milliseconds;
                _updateParam6.Value = item.value ?? (object)DBNull.Value;
                _updateParam7.Value = item.popStamp?.ToByteArray() ?? (object)DBNull.Value;
                _updateParam8.Value = now.uniqueTime;
                _updateParam9.Value = now.uniqueTime;
                var count = conn.ExecuteNonQuery(_updateCommand);
                if (count > 0)
                {
                     item.modified = now;
                }
                return count;
            } // Using
        }

        internal virtual int GetCountDirty(DatabaseConnection conn)
        {
            using (var _getCountCommand = _database.CreateCommand())
            {
                _getCountCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM inbox; PRAGMA read_uncommitted = 0;";
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
            sl.Add("fileId");
            sl.Add("boxId");
            sl.Add("priority");
            sl.Add("timeStamp");
            sl.Add("value");
            sl.Add("popStamp");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        // SELECT identityId,fileId,boxId,priority,timeStamp,value,popStamp,created,modified
        internal InboxRecord ReadRecordFromReaderAll(SqliteDataReader rdr)
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
                    throw new Exception("Not a GUID in fileId...");
                item.fileId = new Guid(_guid);
            }

            if (rdr.IsDBNull(2))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(2, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in boxId...");
                item.boxId = new Guid(_guid);
            }

            if (rdr.IsDBNull(3))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.priority = rdr.GetInt32(3);
            }

            if (rdr.IsDBNull(4))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.timeStamp = new UnixTimeUtc(rdr.GetInt64(4));
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
                item.popStamp = null;
            else
            {
                bytesRead = rdr.GetBytes(6, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in popStamp...");
                item.popStamp = new Guid(_guid);
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

        internal int Delete(DatabaseConnection conn, Guid identityId,Guid fileId)
        {
            using (var _delete0Command = _database.CreateCommand())
            {
                _delete0Command.CommandText = "DELETE FROM inbox " +
                                             "WHERE identityId = @identityId AND fileId = @fileId";
                var _delete0Param1 = _delete0Command.CreateParameter();
                _delete0Param1.ParameterName = "@identityId";
                _delete0Command.Parameters.Add(_delete0Param1);
                var _delete0Param2 = _delete0Command.CreateParameter();
                _delete0Param2.ParameterName = "@fileId";
                _delete0Command.Parameters.Add(_delete0Param2);

                _delete0Param1.Value = identityId.ToByteArray();
                _delete0Param2.Value = fileId.ToByteArray();
                var count = conn.ExecuteNonQuery(_delete0Command);
                return count;
            } // Using
        }

        internal InboxRecord ReadRecordFromReader0(SqliteDataReader rdr, Guid identityId,Guid fileId)
        {
            var result = new List<InboxRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new InboxRecord();
            item.identityId = identityId;
            item.fileId = fileId;

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
                item.timeStamp = new UnixTimeUtc(rdr.GetInt64(2));
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
                item.value = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.value, 0, (int) bytesRead);
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
                item.created = new UnixTimeUtcUnique(rdr.GetInt64(5));
            }

            if (rdr.IsDBNull(6))
                item.modified = null;
            else
            {
                item.modified = new UnixTimeUtcUnique(rdr.GetInt64(6));
            }
            return item;
       }

        internal InboxRecord Get(DatabaseConnection conn, Guid identityId,Guid fileId)
        {
            using (var _get0Command = _database.CreateCommand())
            {
                _get0Command.CommandText = "SELECT boxId,priority,timeStamp,value,popStamp,created,modified FROM inbox " +
                                             "WHERE identityId = @identityId AND fileId = @fileId LIMIT 1;";
                var _get0Param1 = _get0Command.CreateParameter();
                _get0Param1.ParameterName = "@identityId";
                _get0Command.Parameters.Add(_get0Param1);
                var _get0Param2 = _get0Command.CreateParameter();
                _get0Param2.ParameterName = "@fileId";
                _get0Command.Parameters.Add(_get0Param2);

                _get0Param1.Value = identityId.ToByteArray();
                _get0Param2.Value = fileId.ToByteArray();
                lock (conn._lock)
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_get0Command, System.Data.CommandBehavior.SingleRow))
                    {
                        if (!rdr.Read())
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr, identityId,fileId);
                        return r;
                    } // using
                } // lock
            } // using
        }

    }
}
