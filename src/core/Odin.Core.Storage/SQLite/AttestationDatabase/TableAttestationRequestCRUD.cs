using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;
using Odin.Core.Identity;

namespace Odin.Core.Storage.SQLite.AttestationDatabase
{
    public class AttestationRequestRecord
    {
        private string _attestationId;
        public string attestationId
        {
           get {
                   return _attestationId;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 0) throw new Exception("Too short");
                    if (value?.Length > 65535) throw new Exception("Too long");
                  _attestationId = value;
               }
        }
        private string _requestEnvelope;
        public string requestEnvelope
        {
           get {
                   return _requestEnvelope;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 0) throw new Exception("Too short");
                    if (value?.Length > 65535) throw new Exception("Too long");
                  _requestEnvelope = value;
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
    } // End of class AttestationRequestRecord

    public class TableAttestationRequestCRUD : TableBase
    {
        private bool _disposed = false;
        private SqliteCommand _insertCommand = null;
        private static Object _insertLock = new Object();
        private SqliteParameter _insertParam1 = null;
        private SqliteParameter _insertParam2 = null;
        private SqliteParameter _insertParam3 = null;
        private SqliteCommand _updateCommand = null;
        private static Object _updateLock = new Object();
        private SqliteParameter _updateParam1 = null;
        private SqliteParameter _updateParam2 = null;
        private SqliteParameter _updateParam3 = null;
        private SqliteCommand _upsertCommand = null;
        private static Object _upsertLock = new Object();
        private SqliteParameter _upsertParam1 = null;
        private SqliteParameter _upsertParam2 = null;
        private SqliteParameter _upsertParam3 = null;
        private SqliteCommand _delete0Command = null;
        private static Object _delete0Lock = new Object();
        private SqliteParameter _delete0Param1 = null;
        private SqliteCommand _get0Command = null;
        private static Object _get0Lock = new Object();
        private SqliteParameter _get0Param1 = null;
        private SqliteCommand _getPaging1Command = null;
        private static Object _getPaging1Lock = new Object();
        private SqliteParameter _getPaging1Param1 = null;
        private SqliteParameter _getPaging1Param2 = null;
        private readonly CacheHelper _cache;

        public TableAttestationRequestCRUD(AttestationDatabase db, CacheHelper cache) : base(db)
        {
            _cache = cache;
        }

        ~TableAttestationRequestCRUD()
        {
            if (_disposed == false) throw new Exception("TableAttestationRequestCRUD Not disposed properly");
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
            _getPaging1Command?.Dispose();
            _getPaging1Command = null;
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        public sealed override void EnsureTableExists(DatabaseBase.DatabaseConnection conn, bool dropExisting = false)
        {
                using (var cmd = _database.CreateCommand(conn))
                {
                    if (dropExisting)
                    {
                       cmd.CommandText = "DROP TABLE IF EXISTS attestationRequest;";
                        _database.ExecuteNonQuery(conn, cmd);
                    }
                    cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS attestationRequest("
                     +"attestationId STRING NOT NULL UNIQUE, "
                     +"requestEnvelope STRING NOT NULL UNIQUE, "
                     +"timestamp INT NOT NULL "
                     +", PRIMARY KEY (attestationId)"
                     +");"
                     ;
                    _database.ExecuteNonQuery(conn, cmd);
            }
        }

        public virtual int Insert(DatabaseBase.DatabaseConnection conn, AttestationRequestRecord item)
        {
            lock (_insertLock)
            {
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand(conn);
                    _insertCommand.CommandText = "INSERT INTO attestationRequest (attestationId,requestEnvelope,timestamp) " +
                                                 "VALUES ($attestationId,$requestEnvelope,$timestamp)";
                    _insertParam1 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam1);
                    _insertParam1.ParameterName = "$attestationId";
                    _insertParam2 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam2);
                    _insertParam2.ParameterName = "$requestEnvelope";
                    _insertParam3 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam3);
                    _insertParam3.ParameterName = "$timestamp";
                    _insertCommand.Prepare();
                }
                _insertParam1.Value = item.attestationId;
                _insertParam2.Value = item.requestEnvelope;
                _insertParam3.Value = item.timestamp.milliseconds;
                var count = _database.ExecuteNonQuery(conn, _insertCommand);
                if (count > 0)
                 {
                    _cache.AddOrUpdate("TableAttestationRequestCRUD", item.attestationId, item);
                 }
                return count;
            } // Lock
        }

        public virtual int Upsert(DatabaseBase.DatabaseConnection conn, AttestationRequestRecord item)
        {
            lock (_upsertLock)
            {
                if (_upsertCommand == null)
                {
                    _upsertCommand = _database.CreateCommand(conn);
                    _upsertCommand.CommandText = "INSERT INTO attestationRequest (attestationId,requestEnvelope,timestamp) " +
                                                 "VALUES ($attestationId,$requestEnvelope,$timestamp)"+
                                                 "ON CONFLICT (attestationId) DO UPDATE "+
                                                 "SET requestEnvelope = $requestEnvelope,timestamp = $timestamp "+
                                                 ";";
                    _upsertParam1 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam1);
                    _upsertParam1.ParameterName = "$attestationId";
                    _upsertParam2 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam2);
                    _upsertParam2.ParameterName = "$requestEnvelope";
                    _upsertParam3 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam3);
                    _upsertParam3.ParameterName = "$timestamp";
                    _upsertCommand.Prepare();
                }
                _upsertParam1.Value = item.attestationId;
                _upsertParam2.Value = item.requestEnvelope;
                _upsertParam3.Value = item.timestamp.milliseconds;
                var count = _database.ExecuteNonQuery(conn, _upsertCommand);
                if (count > 0)
                    _cache.AddOrUpdate("TableAttestationRequestCRUD", item.attestationId, item);
                return count;
            } // Lock
        }
        public virtual int Update(DatabaseBase.DatabaseConnection conn, AttestationRequestRecord item)
        {
            lock (_updateLock)
            {
                if (_updateCommand == null)
                {
                    _updateCommand = _database.CreateCommand(conn);
                    _updateCommand.CommandText = "UPDATE attestationRequest " +
                                                 "SET requestEnvelope = $requestEnvelope,timestamp = $timestamp "+
                                                 "WHERE (attestationId = $attestationId)";
                    _updateParam1 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam1);
                    _updateParam1.ParameterName = "$attestationId";
                    _updateParam2 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam2);
                    _updateParam2.ParameterName = "$requestEnvelope";
                    _updateParam3 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam3);
                    _updateParam3.ParameterName = "$timestamp";
                    _updateCommand.Prepare();
                }
                _updateParam1.Value = item.attestationId;
                _updateParam2.Value = item.requestEnvelope;
                _updateParam3.Value = item.timestamp.milliseconds;
                var count = _database.ExecuteNonQuery(conn, _updateCommand);
                if (count > 0)
                {
                    _cache.AddOrUpdate("TableAttestationRequestCRUD", item.attestationId, item);
                }
                return count;
            } // Lock
        }

        // SELECT attestationId,requestEnvelope,timestamp
        public AttestationRequestRecord ReadRecordFromReaderAll(SqliteDataReader rdr)
        {
            var result = new List<AttestationRequestRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new AttestationRequestRecord();

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.attestationId = rdr.GetString(0);
            }

            if (rdr.IsDBNull(1))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.requestEnvelope = rdr.GetString(1);
            }

            if (rdr.IsDBNull(2))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.timestamp = new UnixTimeUtc(rdr.GetInt64(2));
            }
            return item;
       }

        public int Delete(DatabaseBase.DatabaseConnection conn, string attestationId)
        {
            if (attestationId == null) throw new Exception("Cannot be null");
            if (attestationId?.Length < 0) throw new Exception("Too short");
            if (attestationId?.Length > 65535) throw new Exception("Too long");
            lock (_delete0Lock)
            {
                if (_delete0Command == null)
                {
                    _delete0Command = _database.CreateCommand(conn);
                    _delete0Command.CommandText = "DELETE FROM attestationRequest " +
                                                 "WHERE attestationId = $attestationId";
                    _delete0Param1 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param1);
                    _delete0Param1.ParameterName = "$attestationId";
                    _delete0Command.Prepare();
                }
                _delete0Param1.Value = attestationId;
                var count = _database.ExecuteNonQuery(conn, _delete0Command);
                if (count > 0)
                    _cache.Remove("TableAttestationRequestCRUD", attestationId);
                return count;
            } // Lock
        }

        public AttestationRequestRecord ReadRecordFromReader0(SqliteDataReader rdr, string attestationId)
        {
            if (attestationId == null) throw new Exception("Cannot be null");
            if (attestationId?.Length < 0) throw new Exception("Too short");
            if (attestationId?.Length > 65535) throw new Exception("Too long");
            var result = new List<AttestationRequestRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new AttestationRequestRecord();
            item.attestationId = attestationId;

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.requestEnvelope = rdr.GetString(0);
            }

            if (rdr.IsDBNull(1))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.timestamp = new UnixTimeUtc(rdr.GetInt64(1));
            }
            return item;
       }

        public AttestationRequestRecord Get(DatabaseBase.DatabaseConnection conn, string attestationId)
        {
            if (attestationId == null) throw new Exception("Cannot be null");
            if (attestationId?.Length < 0) throw new Exception("Too short");
            if (attestationId?.Length > 65535) throw new Exception("Too long");
            var (hit, cacheObject) = _cache.Get("TableAttestationRequestCRUD", attestationId);
            if (hit)
                return (AttestationRequestRecord)cacheObject;
            lock (_get0Lock)
            {
                if (_get0Command == null)
                {
                    _get0Command = _database.CreateCommand(conn);
                    _get0Command.CommandText = "SELECT requestEnvelope,timestamp FROM attestationRequest " +
                                                 "WHERE attestationId = $attestationId LIMIT 1;";
                    _get0Param1 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param1);
                    _get0Param1.ParameterName = "$attestationId";
                    _get0Command.Prepare();
                }
                _get0Param1.Value = attestationId;
                using (SqliteDataReader rdr = _database.ExecuteReader(conn, _get0Command, System.Data.CommandBehavior.SingleRow))
                {
                    if (!rdr.Read())
                    {
                        _cache.AddOrUpdate("TableAttestationRequestCRUD", attestationId, null);
                        return null;
                    }
                    var r = ReadRecordFromReader0(rdr, attestationId);
                    _cache.AddOrUpdate("TableAttestationRequestCRUD", attestationId, r);
                    return r;
                } // using
            } // lock
        }

        public List<AttestationRequestRecord> PagingByAttestationId(DatabaseBase.DatabaseConnection conn, int count, string inCursor, out string nextCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");
            if (inCursor == null)
                inCursor = "";

            lock (_getPaging1Lock)
            {
                if (_getPaging1Command == null)
                {
                    _getPaging1Command = _database.CreateCommand(conn);
                    _getPaging1Command.CommandText = "SELECT attestationId,requestEnvelope,timestamp FROM attestationRequest " +
                                                 "WHERE attestationId > $attestationId ORDER BY attestationId ASC LIMIT $_count;";
                    _getPaging1Param1 = _getPaging1Command.CreateParameter();
                    _getPaging1Command.Parameters.Add(_getPaging1Param1);
                    _getPaging1Param1.ParameterName = "$attestationId";
                    _getPaging1Param2 = _getPaging1Command.CreateParameter();
                    _getPaging1Command.Parameters.Add(_getPaging1Param2);
                    _getPaging1Param2.ParameterName = "$_count";
                    _getPaging1Command.Prepare();
                }
                _getPaging1Param1.Value = inCursor;
                _getPaging1Param2.Value = count+1;

                using (SqliteDataReader rdr = _database.ExecuteReader(conn, _getPaging1Command, System.Data.CommandBehavior.Default))
                {
                    var result = new List<AttestationRequestRecord>();
                    int n = 0;
                    while ((n < count) && rdr.Read())
                    {
                        n++;
                        result.Add(ReadRecordFromReaderAll(rdr));
                    } // while
                    if ((n > 0) && rdr.Read())
                    {
                            nextCursor = result[n - 1].attestationId;
                    }
                    else
                    {
                        nextCursor = null;
                    }

                    return result;
                } // using
            } // lock
        } // PagingGet

    }
}
