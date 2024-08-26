using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;
using Odin.Core.Identity;

namespace Odin.Core.Storage.SQLite.AttestationDatabase
{
    public class AttestationStatusRecord
    {
        private byte[] _attestationId;
        public byte[] attestationId
        {
           get {
                   return _attestationId;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 16) throw new Exception("Too short");
                    if (value?.Length > 64) throw new Exception("Too long");
                  _attestationId = value;
               }
        }
        private Int32 _status;
        public Int32 status
        {
           get {
                   return _status;
               }
           set {
                  _status = value;
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
    } // End of class AttestationStatusRecord

    public class TableAttestationStatusCRUD : TableBase
    {
        private bool _disposed = false;
        private readonly CacheHelper _cache;

        public TableAttestationStatusCRUD(AttestationDatabase db, CacheHelper cache) : base(db, "attestationStatus")
        {
            _cache = cache;
        }

        ~TableAttestationStatusCRUD()
        {
            if (_disposed == false) throw new Exception("TableAttestationStatusCRUD Not disposed properly");
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
                       cmd.CommandText = "DROP TABLE IF EXISTS attestationStatus;";
                       conn.ExecuteNonQuery(cmd);
                    }
                    cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS attestationStatus("
                     +"attestationId BLOB NOT NULL UNIQUE, "
                     +"status INT NOT NULL, "
                     +"created INT NOT NULL, "
                     +"modified INT  "
                     +", PRIMARY KEY (attestationId)"
                     +");"
                     ;
                    conn.ExecuteNonQuery(cmd);
            }
        }

        public virtual int Insert(DatabaseConnection conn, AttestationStatusRecord item)
        {
            using (var _insertCommand = _database.CreateCommand())
            {
                _insertCommand.CommandText = "INSERT INTO attestationStatus (attestationId,status,created,modified) " +
                                             "VALUES (@attestationId,@status,@created,@modified)";
                var _insertParam1 = _insertCommand.CreateParameter();
                _insertParam1.ParameterName = "@attestationId";
                _insertCommand.Parameters.Add(_insertParam1);
                var _insertParam2 = _insertCommand.CreateParameter();
                _insertParam2.ParameterName = "@status";
                _insertCommand.Parameters.Add(_insertParam2);
                var _insertParam3 = _insertCommand.CreateParameter();
                _insertParam3.ParameterName = "@created";
                _insertCommand.Parameters.Add(_insertParam3);
                var _insertParam4 = _insertCommand.CreateParameter();
                _insertParam4.ParameterName = "@modified";
                _insertCommand.Parameters.Add(_insertParam4);
                _insertParam1.Value = item.attestationId;
                _insertParam2.Value = item.status;
                var now = UnixTimeUtcUnique.Now();
                _insertParam3.Value = now.uniqueTime;
                item.modified = null;
                _insertParam4.Value = DBNull.Value;
                var count = conn.ExecuteNonQuery(_insertCommand);
                if (count > 0)
                {
                     item.created = now;
                    _cache.AddOrUpdate("TableAttestationStatusCRUD", item.attestationId.ToBase64(), item);
                }
                return count;
            } // Using
        }

        public virtual int TryInsert(DatabaseConnection conn, AttestationStatusRecord item)
        {
            using (var _insertCommand = _database.CreateCommand())
            {
                _insertCommand.CommandText = "INSERT OR IGNORE INTO attestationStatus (attestationId,status,created,modified) " +
                                             "VALUES (@attestationId,@status,@created,@modified)";
                var _insertParam1 = _insertCommand.CreateParameter();
                _insertParam1.ParameterName = "@attestationId";
                _insertCommand.Parameters.Add(_insertParam1);
                var _insertParam2 = _insertCommand.CreateParameter();
                _insertParam2.ParameterName = "@status";
                _insertCommand.Parameters.Add(_insertParam2);
                var _insertParam3 = _insertCommand.CreateParameter();
                _insertParam3.ParameterName = "@created";
                _insertCommand.Parameters.Add(_insertParam3);
                var _insertParam4 = _insertCommand.CreateParameter();
                _insertParam4.ParameterName = "@modified";
                _insertCommand.Parameters.Add(_insertParam4);
                _insertParam1.Value = item.attestationId;
                _insertParam2.Value = item.status;
                var now = UnixTimeUtcUnique.Now();
                _insertParam3.Value = now.uniqueTime;
                item.modified = null;
                _insertParam4.Value = DBNull.Value;
                var count = conn.ExecuteNonQuery(_insertCommand);
                if (count > 0)
                {
                    item.created = now;
                   _cache.AddOrUpdate("TableAttestationStatusCRUD", item.attestationId.ToBase64(), item);
                }
                return count;
            } // Using
        }

        public virtual int Upsert(DatabaseConnection conn, AttestationStatusRecord item)
        {
            using (var _upsertCommand = _database.CreateCommand())
            {
                _upsertCommand.CommandText = "INSERT INTO attestationStatus (attestationId,status,created) " +
                                             "VALUES (@attestationId,@status,@created)"+
                                             "ON CONFLICT (attestationId) DO UPDATE "+
                                             "SET status = @status,modified = @modified "+
                                             "RETURNING created, modified;";
                var _upsertParam1 = _upsertCommand.CreateParameter();
                _upsertParam1.ParameterName = "@attestationId";
                _upsertCommand.Parameters.Add(_upsertParam1);
                var _upsertParam2 = _upsertCommand.CreateParameter();
                _upsertParam2.ParameterName = "@status";
                _upsertCommand.Parameters.Add(_upsertParam2);
                var _upsertParam3 = _upsertCommand.CreateParameter();
                _upsertParam3.ParameterName = "@created";
                _upsertCommand.Parameters.Add(_upsertParam3);
                var _upsertParam4 = _upsertCommand.CreateParameter();
                _upsertParam4.ParameterName = "@modified";
                _upsertCommand.Parameters.Add(_upsertParam4);
                var now = UnixTimeUtcUnique.Now();
                _upsertParam1.Value = item.attestationId;
                _upsertParam2.Value = item.status;
                _upsertParam3.Value = now.uniqueTime;
                _upsertParam4.Value = now.uniqueTime;
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
                      _cache.AddOrUpdate("TableAttestationStatusCRUD", item.attestationId.ToBase64(), item);
                      return 1;
                   }
                }
                return 0;
            } // Using
        }

        public virtual int Update(DatabaseConnection conn, AttestationStatusRecord item)
        {
            using (var _updateCommand = _database.CreateCommand())
            {
                _updateCommand.CommandText = "UPDATE attestationStatus " +
                                             "SET status = @status,modified = @modified "+
                                             "WHERE (attestationId = @attestationId)";
                var _updateParam1 = _updateCommand.CreateParameter();
                _updateParam1.ParameterName = "@attestationId";
                _updateCommand.Parameters.Add(_updateParam1);
                var _updateParam2 = _updateCommand.CreateParameter();
                _updateParam2.ParameterName = "@status";
                _updateCommand.Parameters.Add(_updateParam2);
                var _updateParam3 = _updateCommand.CreateParameter();
                _updateParam3.ParameterName = "@created";
                _updateCommand.Parameters.Add(_updateParam3);
                var _updateParam4 = _updateCommand.CreateParameter();
                _updateParam4.ParameterName = "@modified";
                _updateCommand.Parameters.Add(_updateParam4);
             var now = UnixTimeUtcUnique.Now();
                _updateParam1.Value = item.attestationId;
                _updateParam2.Value = item.status;
                _updateParam3.Value = now.uniqueTime;
                _updateParam4.Value = now.uniqueTime;
                var count = conn.ExecuteNonQuery(_updateCommand);
                if (count > 0)
                {
                     item.modified = now;
                    _cache.AddOrUpdate("TableAttestationStatusCRUD", item.attestationId.ToBase64(), item);
                }
                return count;
            } // Using
        }

        public virtual int GetCountDirty(DatabaseConnection conn)
        {
            using (var _getCountCommand = _database.CreateCommand())
            {
                _getCountCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM attestationStatus; PRAGMA read_uncommitted = 0;";
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
            sl.Add("attestationId");
            sl.Add("status");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        // SELECT attestationId,status,created,modified
        public AttestationStatusRecord ReadRecordFromReaderAll(SqliteDataReader rdr)
        {
            var result = new List<AttestationStatusRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new AttestationStatusRecord();

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(0, 0, _tmpbuf, 0, 64+1);
                if (bytesRead > 64)
                    throw new Exception("Too much data in attestationId...");
                if (bytesRead < 16)
                    throw new Exception("Too little data in attestationId...");
                item.attestationId = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.attestationId, 0, (int) bytesRead);
            }

            if (rdr.IsDBNull(1))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.status = rdr.GetInt32(1);
            }

            if (rdr.IsDBNull(2))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.created = new UnixTimeUtcUnique(rdr.GetInt64(2));
            }

            if (rdr.IsDBNull(3))
                item.modified = null;
            else
            {
                item.modified = new UnixTimeUtcUnique(rdr.GetInt64(3));
            }
            return item;
       }

        public int Delete(DatabaseConnection conn, byte[] attestationId)
        {
            if (attestationId == null) throw new Exception("Cannot be null");
            if (attestationId?.Length < 16) throw new Exception("Too short");
            if (attestationId?.Length > 64) throw new Exception("Too long");
            using (var _delete0Command = _database.CreateCommand())
            {
                _delete0Command.CommandText = "DELETE FROM attestationStatus " +
                                             "WHERE attestationId = @attestationId";
                var _delete0Param1 = _delete0Command.CreateParameter();
                _delete0Param1.ParameterName = "@attestationId";
                _delete0Command.Parameters.Add(_delete0Param1);

                _delete0Param1.Value = attestationId;
                var count = conn.ExecuteNonQuery(_delete0Command);
                if (count > 0)
                    _cache.Remove("TableAttestationStatusCRUD", attestationId.ToBase64());
                return count;
            } // Using
        }

        public AttestationStatusRecord ReadRecordFromReader0(SqliteDataReader rdr, byte[] attestationId)
        {
            if (attestationId == null) throw new Exception("Cannot be null");
            if (attestationId?.Length < 16) throw new Exception("Too short");
            if (attestationId?.Length > 64) throw new Exception("Too long");
            var result = new List<AttestationStatusRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new AttestationStatusRecord();
            item.attestationId = attestationId;

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.status = rdr.GetInt32(0);
            }

            if (rdr.IsDBNull(1))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.created = new UnixTimeUtcUnique(rdr.GetInt64(1));
            }

            if (rdr.IsDBNull(2))
                item.modified = null;
            else
            {
                item.modified = new UnixTimeUtcUnique(rdr.GetInt64(2));
            }
            return item;
       }

        public AttestationStatusRecord Get(DatabaseConnection conn, byte[] attestationId)
        {
            if (attestationId == null) throw new Exception("Cannot be null");
            if (attestationId?.Length < 16) throw new Exception("Too short");
            if (attestationId?.Length > 64) throw new Exception("Too long");
            var (hit, cacheObject) = _cache.Get("TableAttestationStatusCRUD", attestationId.ToBase64());
            if (hit)
                return (AttestationStatusRecord)cacheObject;
            using (var _get0Command = _database.CreateCommand())
            {
                _get0Command.CommandText = "SELECT status,created,modified FROM attestationStatus " +
                                             "WHERE attestationId = @attestationId LIMIT 1;";
                var _get0Param1 = _get0Command.CreateParameter();
                _get0Param1.ParameterName = "@attestationId";
                _get0Command.Parameters.Add(_get0Param1);

                _get0Param1.Value = attestationId;
                lock (conn._lock)
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_get0Command, System.Data.CommandBehavior.SingleRow))
                    {
                        if (!rdr.Read())
                        {
                            _cache.AddOrUpdate("TableAttestationStatusCRUD", attestationId.ToBase64(), null);
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr, attestationId);
                        _cache.AddOrUpdate("TableAttestationStatusCRUD", attestationId.ToBase64(), r);
                        return r;
                    } // using
                } // lock
            } // using
        }

    }
}
