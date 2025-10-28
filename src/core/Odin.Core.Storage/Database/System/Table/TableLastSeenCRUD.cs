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
        public string subject { get; set; }
        public UnixTimeUtc timestamp { get; set; }
        public void Validate()
        {
            if (subject == null) throw new OdinDatabaseValidationException("Cannot be null subject");
            if (subject?.Length < 0) throw new OdinDatabaseValidationException($"Too short subject, was {subject.Length} (min 0)");
            if (subject?.Length > 65535) throw new OdinDatabaseValidationException($"Too long subject, was {subject.Length} (max 65535)");
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


       /*
        * This method is no longer used.
        * It is kept here, commented-out, so you can see how the table is created without having to locate its latest migration.
        *
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
                   +"subject TEXT NOT NULL UNIQUE, "
                   +"timestamp BIGINT NOT NULL "
                   +$"){wori};"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "LastSeen", createSql, commentSql);
        }
       */

        public virtual async Task<int> InsertAsync(LastSeenRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO LastSeen (subject,timestamp) " +
                                           $"VALUES (@subject,@timestamp)"+
                                            "RETURNING -1,-1,rowId;";
                insertCommand.AddParameter("@subject", DbType.String, item.subject);
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
                insertCommand.CommandText = "INSERT INTO LastSeen (subject,timestamp) " +
                                            $"VALUES (@subject,@timestamp) " +
                                            "ON CONFLICT DO NOTHING "+
                                            "RETURNING -1,-1,rowId;";
                insertCommand.AddParameter("@subject", DbType.String, item.subject);
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
                upsertCommand.CommandText = "INSERT INTO LastSeen (subject,timestamp) " +
                                            $"VALUES (@subject,@timestamp)"+
                                            "ON CONFLICT (subject) DO UPDATE "+
                                            $"SET timestamp = @timestamp "+
                                            "RETURNING -1,-1,rowId;";
                upsertCommand.AddParameter("@subject", DbType.String, item.subject);
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
                                            "WHERE (subject = @subject) "+
                                            "RETURNING -1,-1,rowId;";
                updateCommand.AddParameter("@subject", DbType.String, item.subject);
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
            sl.Add("subject");
            sl.Add("timestamp");
            return sl;
        }

        // SELECT rowId,subject,timestamp
        public LastSeenRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<LastSeenRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new LastSeenRecord();
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.subject = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[1];
            item.timestamp = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[2]);
            return item;
       }

        public virtual async Task<int> DeleteAsync(string subject)
        {
            if (subject == null) throw new OdinDatabaseValidationException("Cannot be null subject");
            if (subject?.Length < 0) throw new OdinDatabaseValidationException($"Too short subject, was {subject.Length} (min 0)");
            if (subject?.Length > 65535) throw new OdinDatabaseValidationException($"Too long subject, was {subject.Length} (max 65535)");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM LastSeen " +
                                             "WHERE subject = @subject";

                delete0Command.AddParameter("@subject", DbType.String, subject);
                var count = await delete0Command.ExecuteNonQueryAsync();
                return count;
            }
        }

        public virtual async Task<LastSeenRecord> PopAsync(string subject)
        {
            if (subject == null) throw new OdinDatabaseValidationException("Cannot be null subject");
            if (subject?.Length < 0) throw new OdinDatabaseValidationException($"Too short subject, was {subject.Length} (min 0)");
            if (subject?.Length > 65535) throw new OdinDatabaseValidationException($"Too long subject, was {subject.Length} (max 65535)");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var deleteCommand = cn.CreateCommand();
            {
                deleteCommand.CommandText = "DELETE FROM LastSeen " +
                                             "WHERE subject = @subject " + 
                                             "RETURNING rowId,timestamp";

                deleteCommand.AddParameter("@subject", DbType.String, subject);
                using (var rdr = await deleteCommand.ExecuteReaderAsync(CommandBehavior.SingleRow))
                {
                    if (await rdr.ReadAsync())
                    {
                       return ReadRecordFromReader0(rdr,subject);
                    }
                    else
                    {
                       return null;
                    }
                }
            }
        }

        public LastSeenRecord ReadRecordFromReader0(DbDataReader rdr,string subject)
        {
            if (subject == null) throw new OdinDatabaseValidationException("Cannot be null subject");
            if (subject?.Length < 0) throw new OdinDatabaseValidationException($"Too short subject, was {subject.Length} (min 0)");
            if (subject?.Length > 65535) throw new OdinDatabaseValidationException($"Too long subject, was {subject.Length} (max 65535)");
            var result = new List<LastSeenRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new LastSeenRecord();
            item.subject = subject;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.timestamp = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[1]);
            return item;
       }

        public virtual async Task<LastSeenRecord> GetAsync(string subject)
        {
            if (subject == null) throw new OdinDatabaseValidationException("Cannot be null subject");
            if (subject?.Length < 0) throw new OdinDatabaseValidationException($"Too short subject, was {subject.Length} (min 0)");
            if (subject?.Length > 65535) throw new OdinDatabaseValidationException($"Too long subject, was {subject.Length} (max 65535)");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT rowId,timestamp FROM LastSeen " +
                                             "WHERE subject = @subject LIMIT 1 "+
                                             ";";

                get0Command.AddParameter("@subject", DbType.String, subject);
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr,subject);
                        return r;
                    } // using
                } //
            } // using
        }

    }
}
