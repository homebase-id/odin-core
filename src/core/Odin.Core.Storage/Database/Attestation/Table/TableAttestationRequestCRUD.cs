using System;
using System.Data;
using System.Data.Common;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Odin.Core.Time;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Attestation.Connection;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.KeyChain.Connection;
using Odin.Core.Storage.Database.Notary.Connection;
using Odin.Core.Storage.Database.System.Connection;
using Odin.Core.Storage.Factory;
using Odin.Core.Storage;
using Odin.Core.Util;
using Odin.Core.Storage.Exceptions;
using Odin.Core.Storage.SQLite; //added for homebase social sync

// THIS FILE IS AUTO GENERATED - DO NOT EDIT

namespace Odin.Core.Storage.Database.Attestation.Table
{
    public record AttestationRequestRecord
    {
        public Int64 rowId { get; set; }
        public string attestationId { get; set; }
        public string requestEnvelope { get; set; }
        public UnixTimeUtc timestamp { get; set; }
        public void Validate()
        {
            if (attestationId == null) throw new OdinDatabaseValidationException("Cannot be null attestationId");
            if (attestationId?.Length < 0) throw new OdinDatabaseValidationException($"Too short attestationId, was {attestationId.Length} (min 0)");
            if (attestationId?.Length > 65535) throw new OdinDatabaseValidationException($"Too long attestationId, was {attestationId.Length} (max 65535)");
            if (requestEnvelope == null) throw new OdinDatabaseValidationException("Cannot be null requestEnvelope");
            if (requestEnvelope?.Length < 0) throw new OdinDatabaseValidationException($"Too short requestEnvelope, was {requestEnvelope.Length} (min 0)");
            if (requestEnvelope?.Length > 65535) throw new OdinDatabaseValidationException($"Too long requestEnvelope, was {requestEnvelope.Length} (max 65535)");
        }
    } // End of record AttestationRequestRecord

    public abstract class TableAttestationRequestCRUD : TableBase
    {
        private ScopedAttestationConnectionFactory _scopedConnectionFactory { get; init; }
        public override string TableName { get; } = "AttestationRequest";

        public TableAttestationRequestCRUD(ScopedAttestationConnectionFactory scopedConnectionFactory)
        {
            _scopedConnectionFactory = scopedConnectionFactory;
        }


        public override async Task EnsureTableExistsAsync(bool dropExisting = false)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            if (dropExisting)
                await SqlHelper.DeleteTableAsync(cn, "AttestationRequest");
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE AttestationRequest IS '{ \"Version\": 0 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS AttestationRequest( -- { \"Version\": 0 }\n"
                   +rowid
                   +"attestationId TEXT NOT NULL UNIQUE, "
                   +"requestEnvelope TEXT NOT NULL UNIQUE, "
                   +"timestamp BIGINT NOT NULL "
                   +$"){wori};"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "AttestationRequest", createSql, commentSql);
        }

        public virtual async Task<int> InsertAsync(AttestationRequestRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO AttestationRequest (attestationId,requestEnvelope,timestamp) " +
                                           $"VALUES (@attestationId,@requestEnvelope,@timestamp)"+
                                            "RETURNING -1,-1,rowId;";
                insertCommand.AddParameter("@attestationId", DbType.String, item.attestationId);
                insertCommand.AddParameter("@requestEnvelope", DbType.String, item.requestEnvelope);
                insertCommand.AddParameter("@timestamp", DbType.Int64, item.timestamp.milliseconds);
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        public virtual async Task<bool> TryInsertAsync(AttestationRequestRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO AttestationRequest (attestationId,requestEnvelope,timestamp) " +
                                            $"VALUES (@attestationId,@requestEnvelope,@timestamp) " +
                                            "ON CONFLICT DO NOTHING "+
                                            "RETURNING -1,-1,rowId;";
                insertCommand.AddParameter("@attestationId", DbType.String, item.attestationId);
                insertCommand.AddParameter("@requestEnvelope", DbType.String, item.requestEnvelope);
                insertCommand.AddParameter("@timestamp", DbType.Int64, item.timestamp.milliseconds);
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                    return true;
                }
                return false;
            }
        }

        public virtual async Task<int> UpsertAsync(AttestationRequestRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                upsertCommand.CommandText = "INSERT INTO AttestationRequest (attestationId,requestEnvelope,timestamp) " +
                                            $"VALUES (@attestationId,@requestEnvelope,@timestamp)"+
                                            "ON CONFLICT (attestationId) DO UPDATE "+
                                            $"SET requestEnvelope = @requestEnvelope,timestamp = @timestamp "+
                                            "RETURNING -1,-1,rowId;";
                upsertCommand.AddParameter("@attestationId", DbType.String, item.attestationId);
                upsertCommand.AddParameter("@requestEnvelope", DbType.String, item.requestEnvelope);
                upsertCommand.AddParameter("@timestamp", DbType.Int64, item.timestamp.milliseconds);
                await using var rdr = await upsertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        public virtual async Task<int> UpdateAsync(AttestationRequestRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                updateCommand.CommandText = "UPDATE AttestationRequest " +
                                            $"SET requestEnvelope = @requestEnvelope,timestamp = @timestamp "+
                                            "WHERE (attestationId = @attestationId) "+
                                            "RETURNING -1,-1,rowId;";
                updateCommand.AddParameter("@attestationId", DbType.String, item.attestationId);
                updateCommand.AddParameter("@requestEnvelope", DbType.String, item.requestEnvelope);
                updateCommand.AddParameter("@timestamp", DbType.Int64, item.timestamp.milliseconds);
                await using var rdr = await updateCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        public new async Task<int> GetCountAsync()
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getCountCommand = cn.CreateCommand();
            {
                 // TODO: this is SQLite specific
                getCountCommand.CommandText = "SELECT COUNT(*) FROM AttestationRequest;";
                var count = await getCountCommand.ExecuteScalarAsync();
                if (count == null || count == DBNull.Value || !(count is int || count is long))
                    return -1;
                else
                    return Convert.ToInt32(count);
            }
        }

        public new static List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("rowId");
            sl.Add("attestationId");
            sl.Add("requestEnvelope");
            sl.Add("timestamp");
            return sl;
        }

        // SELECT rowId,attestationId,requestEnvelope,timestamp
        public AttestationRequestRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<AttestationRequestRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new AttestationRequestRecord();
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.attestationId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[1];
            item.requestEnvelope = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[2];
            item.timestamp = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[3]);
            return item;
       }

        public virtual async Task<int> DeleteAsync(string attestationId)
        {
            if (attestationId == null) throw new OdinDatabaseValidationException("Cannot be null attestationId");
            if (attestationId?.Length < 0) throw new OdinDatabaseValidationException($"Too short attestationId, was {attestationId.Length} (min 0)");
            if (attestationId?.Length > 65535) throw new OdinDatabaseValidationException($"Too long attestationId, was {attestationId.Length} (max 65535)");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM AttestationRequest " +
                                             "WHERE attestationId = @attestationId";

                delete0Command.AddParameter("@attestationId", DbType.String, attestationId);
                var count = await delete0Command.ExecuteNonQueryAsync();
                return count;
            }
        }

        public virtual async Task<AttestationRequestRecord> PopAsync(string attestationId)
        {
            if (attestationId == null) throw new OdinDatabaseValidationException("Cannot be null attestationId");
            if (attestationId?.Length < 0) throw new OdinDatabaseValidationException($"Too short attestationId, was {attestationId.Length} (min 0)");
            if (attestationId?.Length > 65535) throw new OdinDatabaseValidationException($"Too long attestationId, was {attestationId.Length} (max 65535)");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var deleteCommand = cn.CreateCommand();
            {
                deleteCommand.CommandText = "DELETE FROM AttestationRequest " +
                                             "WHERE attestationId = @attestationId " + 
                                             "RETURNING rowId,requestEnvelope,timestamp";

                deleteCommand.AddParameter("@attestationId", DbType.String, attestationId);
                using (var rdr = await deleteCommand.ExecuteReaderAsync(CommandBehavior.SingleRow))
                {
                    if (await rdr.ReadAsync())
                    {
                       return ReadRecordFromReader0(rdr,attestationId);
                    }
                    else
                    {
                       return null;
                    }
                }
            }
        }

        public AttestationRequestRecord ReadRecordFromReader0(DbDataReader rdr,string attestationId)
        {
            if (attestationId == null) throw new OdinDatabaseValidationException("Cannot be null attestationId");
            if (attestationId?.Length < 0) throw new OdinDatabaseValidationException($"Too short attestationId, was {attestationId.Length} (min 0)");
            if (attestationId?.Length > 65535) throw new OdinDatabaseValidationException($"Too long attestationId, was {attestationId.Length} (max 65535)");
            var result = new List<AttestationRequestRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new AttestationRequestRecord();
            item.attestationId = attestationId;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.requestEnvelope = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[1];
            item.timestamp = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[2]);
            return item;
       }

        public virtual async Task<AttestationRequestRecord> GetAsync(string attestationId)
        {
            if (attestationId == null) throw new OdinDatabaseValidationException("Cannot be null attestationId");
            if (attestationId?.Length < 0) throw new OdinDatabaseValidationException($"Too short attestationId, was {attestationId.Length} (min 0)");
            if (attestationId?.Length > 65535) throw new OdinDatabaseValidationException($"Too long attestationId, was {attestationId.Length} (max 65535)");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT rowId,requestEnvelope,timestamp FROM AttestationRequest " +
                                             "WHERE attestationId = @attestationId LIMIT 1;"+
                                             ";";

                get0Command.AddParameter("@attestationId", DbType.String, attestationId);
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr,attestationId);
                        return r;
                    } // using
                } //
            } // using
        }

        public virtual async Task<(List<AttestationRequestRecord>, string nextCursor)> PagingByAttestationIdAsync(int count, string inCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");
            if (count == int.MaxValue)
                count--; // avoid overflow when doing +1 on the param below
            if (inCursor == null)
                inCursor = "";

            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getPaging1Command = cn.CreateCommand();
            {
                getPaging1Command.CommandText = "SELECT rowId,attestationId,requestEnvelope,timestamp FROM AttestationRequest " +
                                            "WHERE attestationId > @attestationId  ORDER BY attestationId ASC  LIMIT @count;";

                getPaging1Command.AddParameter("@attestationId", DbType.String, inCursor);
                getPaging1Command.AddParameter("@count", DbType.Int64, count+1);

                {
                    await using (var rdr = await getPaging1Command.ExecuteReaderAsync(CommandBehavior.Default))
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
