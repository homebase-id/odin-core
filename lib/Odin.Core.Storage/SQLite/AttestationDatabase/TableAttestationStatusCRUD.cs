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
        private SqliteCommand _insertCommand = null;
        private static Object _insertLock = new Object();
        private SqliteParameter _insertParam1 = null;
        private SqliteParameter _insertParam2 = null;
        private SqliteParameter _insertParam3 = null;
        private SqliteParameter _insertParam4 = null;
        private SqliteCommand _updateCommand = null;
        private static Object _updateLock = new Object();
        private SqliteParameter _updateParam1 = null;
        private SqliteParameter _updateParam2 = null;
        private SqliteParameter _updateParam3 = null;
        private SqliteParameter _updateParam4 = null;
        private SqliteCommand _upsertCommand = null;
        private static Object _upsertLock = new Object();
        private SqliteParameter _upsertParam1 = null;
        private SqliteParameter _upsertParam2 = null;
        private SqliteParameter _upsertParam3 = null;
        private SqliteParameter _upsertParam4 = null;
        private SqliteCommand _delete0Command = null;
        private static Object _delete0Lock = new Object();
        private SqliteParameter _delete0Param1 = null;
        private SqliteCommand _get0Command = null;
        private static Object _get0Lock = new Object();
        private SqliteParameter _get0Param1 = null;
        private readonly CacheHelper _cache;

        public TableAttestationStatusCRUD(AttestationDatabase db, CacheHelper cache) : base(db)
        {
            _cache = cache;
        }

        ~TableAttestationStatusCRUD()
        {
            if (_disposed == false) throw new Exception("TableAttestationStatusCRUD Not disposed properly");
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
                    cmd.CommandText = "DROP TABLE IF EXISTS attestationStatus;";
                    _database.ExecuteNonQuery(cmd);
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
                _database.ExecuteNonQuery(cmd);
                _database.Commit();
            }
        }

        public virtual int Insert(AttestationStatusRecord item)
        {
            lock (_insertLock)
            {
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand();
                    _insertCommand.CommandText = "INSERT INTO attestationStatus (attestationId,status,created,modified) " +
                                                 "VALUES ($attestationId,$status,$created,$modified)";
                    _insertParam1 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam1);
                    _insertParam1.ParameterName = "$attestationId";
                    _insertParam2 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam2);
                    _insertParam2.ParameterName = "$status";
                    _insertParam3 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam3);
                    _insertParam3.ParameterName = "$created";
                    _insertParam4 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam4);
                    _insertParam4.ParameterName = "$modified";
                    _insertCommand.Prepare();
                }
                _insertParam1.Value = item.attestationId;
                _insertParam2.Value = item.status;
                item.created = UnixTimeUtcUnique.Now();
                _insertParam3.Value = item.created.uniqueTime;
                item.modified = null;
                _insertParam4.Value = DBNull.Value;
                var count = _database.ExecuteNonQuery(_insertCommand);
                if (count > 0)
                    _cache.AddOrUpdate("TableAttestationStatusCRUD", item.attestationId.ToString(), item);
                return count;
            } // Lock
        }

        public virtual int Upsert(AttestationStatusRecord item)
        {
            lock (_upsertLock)
            {
                if (_upsertCommand == null)
                {
                    _upsertCommand = _database.CreateCommand();
                    _upsertCommand.CommandText = "INSERT INTO attestationStatus (attestationId,status,created,modified) " +
                                                 "VALUES ($attestationId,$status,$created,$modified)"+
                                                 "ON CONFLICT (attestationId) DO UPDATE "+
                                                 "SET status = $status,modified = $modified;";
                    _upsertParam1 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam1);
                    _upsertParam1.ParameterName = "$attestationId";
                    _upsertParam2 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam2);
                    _upsertParam2.ParameterName = "$status";
                    _upsertParam3 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam3);
                    _upsertParam3.ParameterName = "$created";
                    _upsertParam4 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam4);
                    _upsertParam4.ParameterName = "$modified";
                    _upsertCommand.Prepare();
                }
                _upsertParam1.Value = item.attestationId;
                _upsertParam2.Value = item.status;
                if (item.created.uniqueTime == 0) item.created = UnixTimeUtcUnique.Now();
                _upsertParam3.Value = item.created.uniqueTime;
                item.modified = UnixTimeUtcUnique.Now();
                _upsertParam4.Value = item.modified.HasValue ? item.modified.Value.uniqueTime : DBNull.Value;
                var count = _database.ExecuteNonQuery(_upsertCommand);
                if (count > 0)
                    _cache.AddOrUpdate("TableAttestationStatusCRUD", item.attestationId.ToString(), item);
                return count;
            } // Lock
        }

        public virtual int Update(AttestationStatusRecord item)
        {
            lock (_updateLock)
            {
                if (_updateCommand == null)
                {
                    _updateCommand = _database.CreateCommand();
                    _updateCommand.CommandText = "UPDATE attestationStatus " +
                                                 "SET status = $status,modified = $modified "+
                                                 "WHERE (attestationId = $attestationId)";
                    _updateParam1 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam1);
                    _updateParam1.ParameterName = "$attestationId";
                    _updateParam2 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam2);
                    _updateParam2.ParameterName = "$status";
                    _updateParam3 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam3);
                    _updateParam3.ParameterName = "$created";
                    _updateParam4 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam4);
                    _updateParam4.ParameterName = "$modified";
                    _updateCommand.Prepare();
                }
                _updateParam1.Value = item.attestationId;
                _updateParam2.Value = item.status;
                _updateParam3.Value = UnixTimeUtcUnique.Now().uniqueTime;
                item.modified = UnixTimeUtcUnique.Now();
                _updateParam4.Value = item.modified.HasValue ? item.modified.Value.uniqueTime : DBNull.Value;
                var count = _database.ExecuteNonQuery(_updateCommand);
                if (count > 0)
                    _cache.AddOrUpdate("TableAttestationStatusCRUD", item.attestationId.ToString(), item);
                return count;
            } // Lock
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

        public int Delete(byte[] attestationId)
        {
            if (attestationId == null) throw new Exception("Cannot be null");
            if (attestationId?.Length < 16) throw new Exception("Too short");
            if (attestationId?.Length > 64) throw new Exception("Too long");
            lock (_delete0Lock)
            {
                if (_delete0Command == null)
                {
                    _delete0Command = _database.CreateCommand();
                    _delete0Command.CommandText = "DELETE FROM attestationStatus " +
                                                 "WHERE attestationId = $attestationId";
                    _delete0Param1 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param1);
                    _delete0Param1.ParameterName = "$attestationId";
                    _delete0Command.Prepare();
                }
                _delete0Param1.Value = attestationId;
                var count = _database.ExecuteNonQuery(_delete0Command);
                if (count > 0)
                    _cache.Remove("TableAttestationStatusCRUD", attestationId.ToString());
                return count;
            } // Lock
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

        public AttestationStatusRecord Get(byte[] attestationId)
        {
            if (attestationId == null) throw new Exception("Cannot be null");
            if (attestationId?.Length < 16) throw new Exception("Too short");
            if (attestationId?.Length > 64) throw new Exception("Too long");
            var (hit, cacheObject) = _cache.Get("TableAttestationStatusCRUD", attestationId.ToString());
            if (hit)
                return (AttestationStatusRecord)cacheObject;
            lock (_get0Lock)
            {
                if (_get0Command == null)
                {
                    _get0Command = _database.CreateCommand();
                    _get0Command.CommandText = "SELECT status,created,modified FROM attestationStatus " +
                                                 "WHERE attestationId = $attestationId LIMIT 1;";
                    _get0Param1 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param1);
                    _get0Param1.ParameterName = "$attestationId";
                    _get0Command.Prepare();
                }
                _get0Param1.Value = attestationId;
                using (SqliteDataReader rdr = _database.ExecuteReader(_get0Command, System.Data.CommandBehavior.SingleRow))
                {
                    if (!rdr.Read())
                    {
                        _cache.AddOrUpdate("TableAttestationStatusCRUD", attestationId.ToString(), null);
                        return null;
                    }
                    var r = ReadRecordFromReader0(rdr, attestationId);
                    _cache.AddOrUpdate("TableAttestationStatusCRUD", attestationId.ToString(), r);
                    return r;
                } // using
            } // lock
        }

    }
}
