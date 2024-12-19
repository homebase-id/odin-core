using System;
using System.Data;
using System.Data.Common;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Odin.Core.Time;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.System.Connection;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Factory;
using Odin.Core.Util;

// THIS FILE IS AUTO GENERATED - DO NOT EDIT

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

    public class TableAttestationRequestCRUD
    {
        private readonly CacheHelper _cache;

        public TableAttestationRequestCRUD(CacheHelper cache)
        {
            _cache = cache;
        }


        public virtual async Task EnsureTableExistsAsync(DatabaseConnection conn, bool dropExisting = false)
        {
            await using var cmd = conn.db.CreateCommand();
            if (dropExisting)
            {
                cmd.CommandText = "DROP TABLE IF EXISTS attestationRequest;";
                await conn.ExecuteNonQueryAsync(cmd);
            }
            var rowid = "";
            cmd.CommandText =
                "CREATE TABLE IF NOT EXISTS attestationRequest("
                   +"attestationId TEXT NOT NULL UNIQUE, "
                   +"requestEnvelope TEXT NOT NULL UNIQUE, "
                   +"timestamp BIGINT NOT NULL "
                   + rowid
                   +", PRIMARY KEY (attestationId)"
                   +");"
                   ;
            await conn.ExecuteNonQueryAsync(cmd);
        }

        public virtual async Task<int> InsertAsync(DatabaseConnection conn, AttestationRequestRecord item)
        {
            using (var insertCommand = conn.db.CreateCommand())
            {
                insertCommand.CommandText = "INSERT INTO attestationRequest (attestationId,requestEnvelope,timestamp) " +
                                             "VALUES (@attestationId,@requestEnvelope,@timestamp)";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.ParameterName = "@attestationId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.ParameterName = "@requestEnvelope";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.ParameterName = "@timestamp";
                insertCommand.Parameters.Add(insertParam3);
                insertParam1.Value = item.attestationId;
                insertParam2.Value = item.requestEnvelope;
                insertParam3.Value = item.timestamp.milliseconds;
                var count = await conn.ExecuteNonQueryAsync(insertCommand);
                if (count > 0)
                {
                    _cache.AddOrUpdate("TableAttestationRequestCRUD", item.attestationId, item);
                }
                return count;
            }
        }

        public virtual async Task<bool> TryInsertAsync(DatabaseConnection conn, AttestationRequestRecord item)
        {
            using (var insertCommand = conn.db.CreateCommand())
            {
                insertCommand.CommandText = "INSERT INTO attestationRequest (attestationId,requestEnvelope,timestamp) " +
                                             "VALUES (@attestationId,@requestEnvelope,@timestamp) " +
                                             "ON CONFLICT DO NOTHING";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.ParameterName = "@attestationId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.ParameterName = "@requestEnvelope";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.ParameterName = "@timestamp";
                insertCommand.Parameters.Add(insertParam3);
                insertParam1.Value = item.attestationId;
                insertParam2.Value = item.requestEnvelope;
                insertParam3.Value = item.timestamp.milliseconds;
                var count = await conn.ExecuteNonQueryAsync(insertCommand);
                if (count > 0)
                {
                   _cache.AddOrUpdate("TableAttestationRequestCRUD", item.attestationId, item);
                }
                return count > 0;
            }
        }

        public virtual async Task<int> UpsertAsync(DatabaseConnection conn, AttestationRequestRecord item)
        {
            using (var upsertCommand = conn.db.CreateCommand())
            {
                upsertCommand.CommandText = "INSERT INTO attestationRequest (attestationId,requestEnvelope,timestamp) " +
                                             "VALUES (@attestationId,@requestEnvelope,@timestamp)"+
                                             "ON CONFLICT (attestationId) DO UPDATE "+
                                             "SET requestEnvelope = @requestEnvelope,timestamp = @timestamp "+
                                             ";";
                var upsertParam1 = upsertCommand.CreateParameter();
                upsertParam1.ParameterName = "@attestationId";
                upsertCommand.Parameters.Add(upsertParam1);
                var upsertParam2 = upsertCommand.CreateParameter();
                upsertParam2.ParameterName = "@requestEnvelope";
                upsertCommand.Parameters.Add(upsertParam2);
                var upsertParam3 = upsertCommand.CreateParameter();
                upsertParam3.ParameterName = "@timestamp";
                upsertCommand.Parameters.Add(upsertParam3);
                upsertParam1.Value = item.attestationId;
                upsertParam2.Value = item.requestEnvelope;
                upsertParam3.Value = item.timestamp.milliseconds;
                var count = await conn.ExecuteNonQueryAsync(upsertCommand);
                if (count > 0)
                    _cache.AddOrUpdate("TableAttestationRequestCRUD", item.attestationId, item);
                return count;
            }
        }
        public virtual async Task<int> UpdateAsync(DatabaseConnection conn, AttestationRequestRecord item)
        {
            using (var updateCommand = conn.db.CreateCommand())
            {
                updateCommand.CommandText = "UPDATE attestationRequest " +
                                             "SET requestEnvelope = @requestEnvelope,timestamp = @timestamp "+
                                             "WHERE (attestationId = @attestationId)";
                var updateParam1 = updateCommand.CreateParameter();
                updateParam1.ParameterName = "@attestationId";
                updateCommand.Parameters.Add(updateParam1);
                var updateParam2 = updateCommand.CreateParameter();
                updateParam2.ParameterName = "@requestEnvelope";
                updateCommand.Parameters.Add(updateParam2);
                var updateParam3 = updateCommand.CreateParameter();
                updateParam3.ParameterName = "@timestamp";
                updateCommand.Parameters.Add(updateParam3);
                updateParam1.Value = item.attestationId;
                updateParam2.Value = item.requestEnvelope;
                updateParam3.Value = item.timestamp.milliseconds;
                var count = await conn.ExecuteNonQueryAsync(updateCommand);
                if (count > 0)
                {
                    _cache.AddOrUpdate("TableAttestationRequestCRUD", item.attestationId, item);
                }
                return count;
            }
        }

        public virtual async Task<int> GetCountAsync(DatabaseConnection conn)
        {
            using (var getCountCommand = conn.db.CreateCommand())
            {
                 // TODO: this is SQLite specific
                getCountCommand.CommandText = "SELECT COUNT(*) FROM attestationRequest;";
                var count = await conn.ExecuteScalarAsync(getCountCommand);
                if (count == null || count == DBNull.Value || !(count is int || count is long))
                    return -1;
                else
                    return Convert.ToInt32(count);
            }
        }

        public List<string> GetColumnNames()
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
            byte[] tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new AttestationRequestRecord();
            item.attestationId = rdr.IsDBNull(0) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[0];
            item.requestEnvelope = rdr.IsDBNull(1) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[1];
            item.timestamp = rdr.IsDBNull(2) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[2]);
            return item;
       }

        public virtual async Task<int> DeleteAsync(DatabaseConnection conn, string attestationId)
        {
            if (attestationId == null) throw new Exception("Cannot be null");
            if (attestationId?.Length < 0) throw new Exception("Too short");
            if (attestationId?.Length > 65535) throw new Exception("Too long");
            using (var delete0Command = conn.db.CreateCommand())
            {
                delete0Command.CommandText = "DELETE FROM attestationRequest " +
                                             "WHERE attestationId = @attestationId";
                var delete0Param1 = delete0Command.CreateParameter();
                delete0Param1.ParameterName = "@attestationId";
                delete0Command.Parameters.Add(delete0Param1);

                delete0Param1.Value = attestationId;
                var count = await conn.ExecuteNonQueryAsync(delete0Command);
                if (count > 0)
                    _cache.Remove("TableAttestationRequestCRUD", attestationId);
                return count;
            }
        }

        public AttestationRequestRecord ReadRecordFromReader0(DbDataReader rdr, string attestationId)
        {
            if (attestationId == null) throw new Exception("Cannot be null");
            if (attestationId?.Length < 0) throw new Exception("Too short");
            if (attestationId?.Length > 65535) throw new Exception("Too long");
            var result = new List<AttestationRequestRecord>();
            byte[] tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new AttestationRequestRecord();
            item.attestationId = attestationId;

            item.requestEnvelope = rdr.IsDBNull(0) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[0];

            item.timestamp = rdr.IsDBNull(1) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[1]);
            return item;
       }

        public virtual async Task<AttestationRequestRecord> GetAsync(DatabaseConnection conn, string attestationId)
        {
            if (attestationId == null) throw new Exception("Cannot be null");
            if (attestationId?.Length < 0) throw new Exception("Too short");
            if (attestationId?.Length > 65535) throw new Exception("Too long");
            var (hit, cacheObject) = _cache.Get("TableAttestationRequestCRUD", attestationId);
            if (hit)
                return (AttestationRequestRecord)cacheObject;
            using (var get0Command = conn.db.CreateCommand())
            {
                get0Command.CommandText = "SELECT requestEnvelope,timestamp FROM attestationRequest " +
                                             "WHERE attestationId = @attestationId LIMIT 1;";
                var get0Param1 = get0Command.CreateParameter();
                get0Param1.ParameterName = "@attestationId";
                get0Command.Parameters.Add(get0Param1);

                get0Param1.Value = attestationId;
                {
                    using (var rdr = await conn.ExecuteReaderAsync(get0Command, System.Data.CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            _cache.AddOrUpdate("TableAttestationRequestCRUD", attestationId, null);
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr, attestationId);
                        _cache.AddOrUpdate("TableAttestationRequestCRUD", attestationId, r);
                        return r;
                    } // using
                } //
            } // using
        }

        public virtual async Task<(List<AttestationRequestRecord>, string nextCursor)> PagingByAttestationIdAsync(DatabaseConnection conn, int count, string inCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");
            if (inCursor == null)
                inCursor = "";

            using (var getPaging1Command = conn.db.CreateCommand())
            {
                getPaging1Command.CommandText = "SELECT attestationId,requestEnvelope,timestamp FROM attestationRequest " +
                                            "WHERE attestationId > @attestationId ORDER BY attestationId ASC LIMIT @count;";
                var getPaging1Param1 = getPaging1Command.CreateParameter();
                getPaging1Param1.ParameterName = "@attestationId";
                getPaging1Command.Parameters.Add(getPaging1Param1);
                var getPaging1Param2 = getPaging1Command.CreateParameter();
                getPaging1Param2.ParameterName = "@count";
                getPaging1Command.Parameters.Add(getPaging1Param2);

                getPaging1Param1.Value = inCursor;
                getPaging1Param2.Value = count+1;

                {
                    await using (var rdr = await conn.ExecuteReaderAsync(getPaging1Command, System.Data.CommandBehavior.Default))
                    {
                        var result = new List<AttestationRequestRecord>();
                        string nextCursor;
                        int n = 0;
                        while ((n < count) && await rdr.ReadAsync())
                        {
                            n++;
                            result.Add(ReadRecordFromReaderAll(rdr));
                        } // while
                        if ((n > 0) && await rdr.ReadAsync())
                        {
                                nextCursor = result[n - 1].attestationId;
                        }
                        else
                        {
                            nextCursor = null;
                        }
                        return (result, nextCursor);
                    } // using
                } //
            } // using 
        } // PagingGet

    }
}
