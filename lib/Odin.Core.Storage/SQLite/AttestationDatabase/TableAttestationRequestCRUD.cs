using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;
using Odin.Core.Identity;

namespace Odin.Core.Storage.SQLite.AttestationDatabase
{
    public class AttestationRequestRecord
    {
        private string _nonce;
        public string nonce
        {
           get {
                   return _nonce;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 0) throw new Exception("Too short");
                    if (value?.Length > 65535) throw new Exception("Too long");
                  _nonce = value;
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
        }

        public sealed override void EnsureTableExists(bool dropExisting = false)
        {
            using (var cmd = _database.CreateCommand())
            {
                if (dropExisting)
                {
                    cmd.CommandText = "DROP TABLE IF EXISTS attestationRequest;";
                    _database.ExecuteNonQuery(cmd);
                }
                cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS attestationRequest("
                     +"nonce STRING NOT NULL UNIQUE, "
                     +"requestEnvelope STRING NOT NULL UNIQUE, "
                     +"timestamp INT NOT NULL "
                     +", PRIMARY KEY (nonce)"
                     +");"
                     ;
                _database.ExecuteNonQuery(cmd);
                _database.Commit();
            }
        }

        public virtual int Insert(AttestationRequestRecord item)
        {
            lock (_insertLock)
            {
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand();
                    _insertCommand.CommandText = "INSERT INTO attestationRequest (nonce,requestEnvelope,timestamp) " +
                                                 "VALUES ($nonce,$requestEnvelope,$timestamp)";
                    _insertParam1 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam1);
                    _insertParam1.ParameterName = "$nonce";
                    _insertParam2 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam2);
                    _insertParam2.ParameterName = "$requestEnvelope";
                    _insertParam3 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam3);
                    _insertParam3.ParameterName = "$timestamp";
                    _insertCommand.Prepare();
                }
                _insertParam1.Value = item.nonce;
                _insertParam2.Value = item.requestEnvelope;
                _insertParam3.Value = item.timestamp.milliseconds;
                var count = _database.ExecuteNonQuery(_insertCommand);
                if (count > 0)
                    _cache.AddOrUpdate("TableAttestationRequestCRUD", item.nonce.ToString(), item);
                return count;
            } // Lock
        }

        public virtual int Upsert(AttestationRequestRecord item)
        {
            lock (_upsertLock)
            {
                if (_upsertCommand == null)
                {
                    _upsertCommand = _database.CreateCommand();
                    _upsertCommand.CommandText = "INSERT INTO attestationRequest (nonce,requestEnvelope,timestamp) " +
                                                 "VALUES ($nonce,$requestEnvelope,$timestamp)"+
                                                 "ON CONFLICT (nonce) DO UPDATE "+
                                                 "SET requestEnvelope = $requestEnvelope,timestamp = $timestamp;";
                    _upsertParam1 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam1);
                    _upsertParam1.ParameterName = "$nonce";
                    _upsertParam2 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam2);
                    _upsertParam2.ParameterName = "$requestEnvelope";
                    _upsertParam3 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam3);
                    _upsertParam3.ParameterName = "$timestamp";
                    _upsertCommand.Prepare();
                }
                _upsertParam1.Value = item.nonce;
                _upsertParam2.Value = item.requestEnvelope;
                _upsertParam3.Value = item.timestamp.milliseconds;
                var count = _database.ExecuteNonQuery(_upsertCommand);
                if (count > 0)
                    _cache.AddOrUpdate("TableAttestationRequestCRUD", item.nonce.ToString(), item);
                return count;
            } // Lock
        }

        public virtual int Update(AttestationRequestRecord item)
        {
            lock (_updateLock)
            {
                if (_updateCommand == null)
                {
                    _updateCommand = _database.CreateCommand();
                    _updateCommand.CommandText = "UPDATE attestationRequest " +
                                                 "SET requestEnvelope = $requestEnvelope,timestamp = $timestamp "+
                                                 "WHERE (nonce = $nonce)";
                    _updateParam1 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam1);
                    _updateParam1.ParameterName = "$nonce";
                    _updateParam2 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam2);
                    _updateParam2.ParameterName = "$requestEnvelope";
                    _updateParam3 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam3);
                    _updateParam3.ParameterName = "$timestamp";
                    _updateCommand.Prepare();
                }
                _updateParam1.Value = item.nonce;
                _updateParam2.Value = item.requestEnvelope;
                _updateParam3.Value = item.timestamp.milliseconds;
                var count = _database.ExecuteNonQuery(_updateCommand);
                if (count > 0)
                    _cache.AddOrUpdate("TableAttestationRequestCRUD", item.nonce.ToString(), item);
                return count;
            } // Lock
        }

        // SELECT nonce,requestEnvelope,timestamp
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
                item.nonce = rdr.GetString(0);
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

        public int Delete(string nonce)
        {
            if (nonce == null) throw new Exception("Cannot be null");
            if (nonce?.Length < 0) throw new Exception("Too short");
            if (nonce?.Length > 65535) throw new Exception("Too long");
            lock (_delete0Lock)
            {
                if (_delete0Command == null)
                {
                    _delete0Command = _database.CreateCommand();
                    _delete0Command.CommandText = "DELETE FROM attestationRequest " +
                                                 "WHERE nonce = $nonce";
                    _delete0Param1 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param1);
                    _delete0Param1.ParameterName = "$nonce";
                    _delete0Command.Prepare();
                }
                _delete0Param1.Value = nonce;
                var count = _database.ExecuteNonQuery(_delete0Command);
                if (count > 0)
                    _cache.Remove("TableAttestationRequestCRUD", nonce.ToString());
                return count;
            } // Lock
        }

        public AttestationRequestRecord ReadRecordFromReader0(SqliteDataReader rdr, string nonce)
        {
            if (nonce == null) throw new Exception("Cannot be null");
            if (nonce?.Length < 0) throw new Exception("Too short");
            if (nonce?.Length > 65535) throw new Exception("Too long");
            var result = new List<AttestationRequestRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new AttestationRequestRecord();
            item.nonce = nonce;

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

        public AttestationRequestRecord Get(string nonce)
        {
            if (nonce == null) throw new Exception("Cannot be null");
            if (nonce?.Length < 0) throw new Exception("Too short");
            if (nonce?.Length > 65535) throw new Exception("Too long");
            var (hit, cacheObject) = _cache.Get("TableAttestationRequestCRUD", nonce.ToString());
            if (hit)
                return (AttestationRequestRecord)cacheObject;
            lock (_get0Lock)
            {
                if (_get0Command == null)
                {
                    _get0Command = _database.CreateCommand();
                    _get0Command.CommandText = "SELECT requestEnvelope,timestamp FROM attestationRequest " +
                                                 "WHERE nonce = $nonce LIMIT 1;";
                    _get0Param1 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param1);
                    _get0Param1.ParameterName = "$nonce";
                    _get0Command.Prepare();
                }
                _get0Param1.Value = nonce;
                using (SqliteDataReader rdr = _database.ExecuteReader(_get0Command, System.Data.CommandBehavior.SingleRow))
                {
                    if (!rdr.Read())
                    {
                        _cache.AddOrUpdate("TableAttestationRequestCRUD", nonce.ToString(), null);
                        return null;
                    }
                    var r = ReadRecordFromReader0(rdr, nonce);
                    _cache.AddOrUpdate("TableAttestationRequestCRUD", nonce.ToString(), r);
                    return r;
                } // using
            } // lock
        }

        public List<AttestationRequestRecord> PagingByNonce(int count, string inCursor, out string nextCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");
            if (inCursor == null)
                inCursor = "";

            lock (_getPaging1Lock)
            {
                if (_getPaging1Command == null)
                {
                    _getPaging1Command = _database.CreateCommand();
                    _getPaging1Command.CommandText = "SELECT nonce,requestEnvelope,timestamp FROM attestationRequest " +
                                                 "WHERE nonce > $nonce ORDER BY nonce ASC LIMIT $_count;";
                    _getPaging1Param1 = _getPaging1Command.CreateParameter();
                    _getPaging1Command.Parameters.Add(_getPaging1Param1);
                    _getPaging1Param1.ParameterName = "$nonce";
                    _getPaging1Param2 = _getPaging1Command.CreateParameter();
                    _getPaging1Command.Parameters.Add(_getPaging1Param2);
                    _getPaging1Param2.ParameterName = "$_count";
                    _getPaging1Command.Prepare();
                }
                _getPaging1Param1.Value = inCursor;
                _getPaging1Param2.Value = count+1;

                using (SqliteDataReader rdr = _database.ExecuteReader(_getPaging1Command, System.Data.CommandBehavior.Default))
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
                            nextCursor = result[n - 1].nonce;
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
