using System;
using System.Data.Common;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;
using Odin.Core.Identity;
using System.Runtime.CompilerServices;

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
        private readonly CacheHelper _cache;

        public TableAttestationRequestCRUD(CacheHelper cache) : base("attestationRequest")
        {
            _cache = cache;
        }

        ~TableAttestationRequestCRUD()
        {
            if (_disposed == false) throw new Exception("TableAttestationRequestCRUD Not disposed properly");
        }

        public override void Dispose()
        {
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        public sealed override void EnsureTableExists(DatabaseConnection conn, bool dropExisting = false)
        {
                using (var cmd = conn.db.CreateCommand())
                {
                    if (dropExisting)
                    {
                       cmd.CommandText = "DROP TABLE IF EXISTS attestationRequest;";
                       conn.ExecuteNonQuery(cmd);
                    }
                    cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS attestationRequest("
                     +"attestationId STRING NOT NULL UNIQUE, "
                     +"requestEnvelope STRING NOT NULL UNIQUE, "
                     +"timestamp INT NOT NULL "
                     +", PRIMARY KEY (attestationId)"
                     +");"
                     ;
                    conn.ExecuteNonQuery(cmd);
            }
        }

        public virtual int Insert(DatabaseConnection conn, AttestationRequestRecord item)
        {
            using (var _insertCommand = conn.db.CreateCommand())
            {
                _insertCommand.CommandText = "INSERT INTO attestationRequest (attestationId,requestEnvelope,timestamp) " +
                                             "VALUES (@attestationId,@requestEnvelope,@timestamp)";
                var _insertParam1 = _insertCommand.CreateParameter();
                _insertParam1.ParameterName = "@attestationId";
                _insertCommand.Parameters.Add(_insertParam1);
                var _insertParam2 = _insertCommand.CreateParameter();
                _insertParam2.ParameterName = "@requestEnvelope";
                _insertCommand.Parameters.Add(_insertParam2);
                var _insertParam3 = _insertCommand.CreateParameter();
                _insertParam3.ParameterName = "@timestamp";
                _insertCommand.Parameters.Add(_insertParam3);
                _insertParam1.Value = item.attestationId;
                _insertParam2.Value = item.requestEnvelope;
                _insertParam3.Value = item.timestamp.milliseconds;
                var count = conn.ExecuteNonQuery(_insertCommand);
                if (count > 0)
                {
                    _cache.AddOrUpdate("TableAttestationRequestCRUD", item.attestationId, item);
                }
                return count;
            } // Using
        }

        public virtual int TryInsert(DatabaseConnection conn, AttestationRequestRecord item)
        {
            using (var _insertCommand = conn.db.CreateCommand())
            {
                _insertCommand.CommandText = "INSERT OR IGNORE INTO attestationRequest (attestationId,requestEnvelope,timestamp) " +
                                             "VALUES (@attestationId,@requestEnvelope,@timestamp)";
                var _insertParam1 = _insertCommand.CreateParameter();
                _insertParam1.ParameterName = "@attestationId";
                _insertCommand.Parameters.Add(_insertParam1);
                var _insertParam2 = _insertCommand.CreateParameter();
                _insertParam2.ParameterName = "@requestEnvelope";
                _insertCommand.Parameters.Add(_insertParam2);
                var _insertParam3 = _insertCommand.CreateParameter();
                _insertParam3.ParameterName = "@timestamp";
                _insertCommand.Parameters.Add(_insertParam3);
                _insertParam1.Value = item.attestationId;
                _insertParam2.Value = item.requestEnvelope;
                _insertParam3.Value = item.timestamp.milliseconds;
                var count = conn.ExecuteNonQuery(_insertCommand);
                if (count > 0)
                {
                   _cache.AddOrUpdate("TableAttestationRequestCRUD", item.attestationId, item);
                }
                return count;
            } // Using
        }

        public virtual int Upsert(DatabaseConnection conn, AttestationRequestRecord item)
        {
            using (var _upsertCommand = conn.db.CreateCommand())
            {
                _upsertCommand.CommandText = "INSERT INTO attestationRequest (attestationId,requestEnvelope,timestamp) " +
                                             "VALUES (@attestationId,@requestEnvelope,@timestamp)"+
                                             "ON CONFLICT (attestationId) DO UPDATE "+
                                             "SET requestEnvelope = @requestEnvelope,timestamp = @timestamp "+
                                             ";";
                var _upsertParam1 = _upsertCommand.CreateParameter();
                _upsertParam1.ParameterName = "@attestationId";
                _upsertCommand.Parameters.Add(_upsertParam1);
                var _upsertParam2 = _upsertCommand.CreateParameter();
                _upsertParam2.ParameterName = "@requestEnvelope";
                _upsertCommand.Parameters.Add(_upsertParam2);
                var _upsertParam3 = _upsertCommand.CreateParameter();
                _upsertParam3.ParameterName = "@timestamp";
                _upsertCommand.Parameters.Add(_upsertParam3);
                _upsertParam1.Value = item.attestationId;
                _upsertParam2.Value = item.requestEnvelope;
                _upsertParam3.Value = item.timestamp.milliseconds;
                var count = conn.ExecuteNonQuery(_upsertCommand);
                if (count > 0)
                    _cache.AddOrUpdate("TableAttestationRequestCRUD", item.attestationId, item);
                return count;
            } // Using
        }
        public virtual int Update(DatabaseConnection conn, AttestationRequestRecord item)
        {
            using (var _updateCommand = conn.db.CreateCommand())
            {
                _updateCommand.CommandText = "UPDATE attestationRequest " +
                                             "SET requestEnvelope = @requestEnvelope,timestamp = @timestamp "+
                                             "WHERE (attestationId = @attestationId)";
                var _updateParam1 = _updateCommand.CreateParameter();
                _updateParam1.ParameterName = "@attestationId";
                _updateCommand.Parameters.Add(_updateParam1);
                var _updateParam2 = _updateCommand.CreateParameter();
                _updateParam2.ParameterName = "@requestEnvelope";
                _updateCommand.Parameters.Add(_updateParam2);
                var _updateParam3 = _updateCommand.CreateParameter();
                _updateParam3.ParameterName = "@timestamp";
                _updateCommand.Parameters.Add(_updateParam3);
                _updateParam1.Value = item.attestationId;
                _updateParam2.Value = item.requestEnvelope;
                _updateParam3.Value = item.timestamp.milliseconds;
                var count = conn.ExecuteNonQuery(_updateCommand);
                if (count > 0)
                {
                    _cache.AddOrUpdate("TableAttestationRequestCRUD", item.attestationId, item);
                }
                return count;
            } // Using
        }

        public virtual int GetCountDirty(DatabaseConnection conn)
        {
            using (var _getCountCommand = conn.db.CreateCommand())
            {
                _getCountCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM attestationRequest; PRAGMA read_uncommitted = 0;";
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
            sl.Add("requestEnvelope");
            sl.Add("timestamp");
            return sl;
        }

        // SELECT attestationId,requestEnvelope,timestamp
        public AttestationRequestRecord ReadRecordFromReaderAll(DbDataReader rdr)
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

        public int Delete(DatabaseConnection conn, string attestationId)
        {
            if (attestationId == null) throw new Exception("Cannot be null");
            if (attestationId?.Length < 0) throw new Exception("Too short");
            if (attestationId?.Length > 65535) throw new Exception("Too long");
            using (var _delete0Command = conn.db.CreateCommand())
            {
                _delete0Command.CommandText = "DELETE FROM attestationRequest " +
                                             "WHERE attestationId = @attestationId";
                var _delete0Param1 = _delete0Command.CreateParameter();
                _delete0Param1.ParameterName = "@attestationId";
                _delete0Command.Parameters.Add(_delete0Param1);

                _delete0Param1.Value = attestationId;
                var count = conn.ExecuteNonQuery(_delete0Command);
                if (count > 0)
                    _cache.Remove("TableAttestationRequestCRUD", attestationId);
                return count;
            } // Using
        }

        public AttestationRequestRecord ReadRecordFromReader0(DbDataReader rdr, string attestationId)
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

        public AttestationRequestRecord Get(DatabaseConnection conn, string attestationId)
        {
            if (attestationId == null) throw new Exception("Cannot be null");
            if (attestationId?.Length < 0) throw new Exception("Too short");
            if (attestationId?.Length > 65535) throw new Exception("Too long");
            var (hit, cacheObject) = _cache.Get("TableAttestationRequestCRUD", attestationId);
            if (hit)
                return (AttestationRequestRecord)cacheObject;
            using (var _get0Command = conn.db.CreateCommand())
            {
                _get0Command.CommandText = "SELECT requestEnvelope,timestamp FROM attestationRequest " +
                                             "WHERE attestationId = @attestationId LIMIT 1;";
                var _get0Param1 = _get0Command.CreateParameter();
                _get0Param1.ParameterName = "@attestationId";
                _get0Command.Parameters.Add(_get0Param1);

                _get0Param1.Value = attestationId;
                lock (conn._lock)
                {
                    using (DbDataReader rdr = conn.ExecuteReader(_get0Command, System.Data.CommandBehavior.SingleRow))
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
            } // using
        }

        public List<AttestationRequestRecord> PagingByAttestationId(DatabaseConnection conn, int count, string inCursor, out string nextCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");
            if (inCursor == null)
                inCursor = "";

            using (var _getPaging1Command = conn.db.CreateCommand())
            {
                _getPaging1Command.CommandText = "SELECT attestationId,requestEnvelope,timestamp FROM attestationRequest " +
                                            "WHERE attestationId > @attestationId ORDER BY attestationId ASC LIMIT $_count;";
                var _getPaging1Param1 = _getPaging1Command.CreateParameter();
                _getPaging1Param1.ParameterName = "@attestationId";
                _getPaging1Command.Parameters.Add(_getPaging1Param1);
                var _getPaging1Param2 = _getPaging1Command.CreateParameter();
                _getPaging1Param2.ParameterName = "$_count";
                _getPaging1Command.Parameters.Add(_getPaging1Param2);

                _getPaging1Param1.Value = inCursor;
                _getPaging1Param2.Value = count+1;

                lock (conn._lock)
                {
                    using (DbDataReader rdr = conn.ExecuteReader(_getPaging1Command, System.Data.CommandBehavior.Default))
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
                } // Lock
            } // using 
        } // PagingGet

    }
}
