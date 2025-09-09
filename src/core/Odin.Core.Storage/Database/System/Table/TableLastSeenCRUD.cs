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

namespace Odin.Core.Storage.Database.System.Table
{
    public record LastSeenRecord
    {
        public Int64 rowId { get; set; }
        public string odinId { get; set; }
        public UnixTimeUtc timestamp { get; set; }
        public void Validate()
        {
            if (odinId == null) throw new OdinDatabaseValidationException("Cannot be null odinId");
            if (odinId?.Length < 3) throw new OdinDatabaseValidationException($"Too short odinId, was {odinId.Length} (min 3)");
            if (odinId?.Length > 256) throw new OdinDatabaseValidationException($"Too long odinId, was {odinId.Length} (max 256)");
        }
    } // End of record LastSeenRecord

    public abstract class TableLastSeenCRUD : TableBase
    {
        private ScopedSystemConnectionFactory _scopedConnectionFactory { get; init; }
        public override string TableName { get; } = "LastSeen";

        public TableLastSeenCRUD(ScopedSystemConnectionFactory scopedConnectionFactory)
        {
            _scopedConnectionFactory = scopedConnectionFactory;
        }


        public override async Task EnsureTableExistsAsync(bool dropExisting = false)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            if (dropExisting)
                await SqlHelper.DeleteTableAsync(cn, "LastSeen");
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE LastSeen IS '{ \"Version\": 202509090509 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS LastSeen( -- { \"Version\": 202509090509 }\n"
                   +rowid
                   +"odinId TEXT NOT NULL UNIQUE, "
                   +"timestamp BIGINT NOT NULL "
                   +$"){wori};"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "LastSeen", createSql, commentSql);
        }

        public virtual async Task<int> InsertAsync(LastSeenRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO LastSeen (odinId,timestamp) " +
                                           $"VALUES (@odinId,@timestamp)"+
                                            "RETURNING -1,-1,rowId;";
                insertCommand.AddParameter("@odinId", DbType.String, item.odinId);
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

        public virtual async Task<bool> TryInsertAsync(LastSeenRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO LastSeen (odinId,timestamp) " +
                                            $"VALUES (@odinId,@timestamp) " +
                                            "ON CONFLICT DO NOTHING "+
                                            "RETURNING -1,-1,rowId;";
                insertCommand.AddParameter("@odinId", DbType.String, item.odinId);
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

        public virtual async Task<int> UpsertAsync(LastSeenRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                upsertCommand.CommandText = "INSERT INTO LastSeen (odinId,timestamp) " +
                                            $"VALUES (@odinId,@timestamp)"+
                                            "ON CONFLICT (odinId) DO UPDATE "+
                                            $"SET timestamp = @timestamp "+
                                            "RETURNING -1,-1,rowId;";
                upsertCommand.AddParameter("@odinId", DbType.String, item.odinId);
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

        public virtual async Task<int> UpdateAsync(LastSeenRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                updateCommand.CommandText = "UPDATE LastSeen " +
                                            $"SET timestamp = @timestamp "+
                                            "WHERE (odinId = @odinId) "+
                                            "RETURNING -1,-1,rowId;";
                updateCommand.AddParameter("@odinId", DbType.String, item.odinId);
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
                getCountCommand.CommandText = "SELECT COUNT(*) FROM LastSeen;";
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
            sl.Add("odinId");
            sl.Add("timestamp");
            return sl;
        }

        // SELECT rowId,odinId,timestamp
        public LastSeenRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<LastSeenRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new LastSeenRecord();
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.odinId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[1];
            item.timestamp = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[2]);
            return item;
       }

        public virtual async Task<int> DeleteAsync(string odinId)
        {
            if (odinId == null) throw new OdinDatabaseValidationException("Cannot be null odinId");
            if (odinId?.Length < 3) throw new OdinDatabaseValidationException($"Too short odinId, was {odinId.Length} (min 3)");
            if (odinId?.Length > 256) throw new OdinDatabaseValidationException($"Too long odinId, was {odinId.Length} (max 256)");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM LastSeen " +
                                             "WHERE odinId = @odinId";

                delete0Command.AddParameter("@odinId", DbType.String, odinId);
                var count = await delete0Command.ExecuteNonQueryAsync();
                return count;
            }
        }

        public virtual async Task<LastSeenRecord> PopAsync(string odinId)
        {
            if (odinId == null) throw new OdinDatabaseValidationException("Cannot be null odinId");
            if (odinId?.Length < 3) throw new OdinDatabaseValidationException($"Too short odinId, was {odinId.Length} (min 3)");
            if (odinId?.Length > 256) throw new OdinDatabaseValidationException($"Too long odinId, was {odinId.Length} (max 256)");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var deleteCommand = cn.CreateCommand();
            {
                deleteCommand.CommandText = "DELETE FROM LastSeen " +
                                             "WHERE odinId = @odinId " + 
                                             "RETURNING rowId,timestamp";

                deleteCommand.AddParameter("@odinId", DbType.String, odinId);
                using (var rdr = await deleteCommand.ExecuteReaderAsync(CommandBehavior.SingleRow))
                {
                    if (await rdr.ReadAsync())
                    {
                       return ReadRecordFromReader0(rdr,odinId);
                    }
                    else
                    {
                       return null;
                    }
                }
            }
        }

        public LastSeenRecord ReadRecordFromReader0(DbDataReader rdr,string odinId)
        {
            if (odinId == null) throw new OdinDatabaseValidationException("Cannot be null odinId");
            if (odinId?.Length < 3) throw new OdinDatabaseValidationException($"Too short odinId, was {odinId.Length} (min 3)");
            if (odinId?.Length > 256) throw new OdinDatabaseValidationException($"Too long odinId, was {odinId.Length} (max 256)");
            var result = new List<LastSeenRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new LastSeenRecord();
            item.odinId = odinId;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.timestamp = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[1]);
            return item;
       }

        public virtual async Task<LastSeenRecord> GetAsync(string odinId)
        {
            if (odinId == null) throw new OdinDatabaseValidationException("Cannot be null odinId");
            if (odinId?.Length < 3) throw new OdinDatabaseValidationException($"Too short odinId, was {odinId.Length} (min 3)");
            if (odinId?.Length > 256) throw new OdinDatabaseValidationException($"Too long odinId, was {odinId.Length} (max 256)");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT rowId,timestamp FROM LastSeen " +
                                             "WHERE odinId = @odinId LIMIT 1;"+
                                             ";";

                get0Command.AddParameter("@odinId", DbType.String, odinId);
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr,odinId);
                        return r;
                    } // using
                } //
            } // using
        }

    }
}
