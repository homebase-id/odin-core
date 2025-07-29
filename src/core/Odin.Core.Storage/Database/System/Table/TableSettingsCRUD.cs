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
    public record SettingsRecord
    {
        public Int64 rowId { get; set; }
        public string key { get; set; }
        public string value { get; set; }
        public UnixTimeUtc created { get; set; }
        public UnixTimeUtc modified { get; set; }
        public void Validate()
        {
            if (key == null) throw new OdinDatabaseValidationException("Cannot be null key");
            if (key?.Length < 0) throw new OdinDatabaseValidationException($"Too short key, was {key.Length} (min 0)");
            if (key?.Length > 65535) throw new OdinDatabaseValidationException($"Too long key, was {key.Length} (max 65535)");
            if (value == null) throw new OdinDatabaseValidationException("Cannot be null value");
            if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short value, was {value.Length} (min 0)");
            if (value?.Length > 65535) throw new OdinDatabaseValidationException($"Too long value, was {value.Length} (max 65535)");
        }
    } // End of record SettingsRecord

    public abstract class TableSettingsCRUD : TableBase
    {
        private ScopedSystemConnectionFactory _scopedConnectionFactory { get; init; }

        public TableSettingsCRUD(CacheHelper cache, ScopedSystemConnectionFactory scopedConnectionFactory)
        {
            _scopedConnectionFactory = scopedConnectionFactory;
        }


        public override async Task EnsureTableExistsAsync(bool dropExisting = false)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            if (dropExisting)
                await SqlHelper.DeleteTableAsync(cn, "Settings");
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowid BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE Settings IS '{ \"Version\": 0 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS Settings( -- { \"Version\": 0 }\n"
                   +rowid
                   +"key TEXT NOT NULL UNIQUE, "
                   +"value TEXT NOT NULL, "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +$"){wori};"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "Settings", createSql, commentSql);
        }

        public virtual async Task<int> InsertAsync(SettingsRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr = insertCommand.SqlNow();
                insertCommand.CommandText = "INSERT INTO Settings (key,value,created,modified) " +
                                           $"VALUES (@key,@value,{sqlNowStr},{sqlNowStr})"+
                                            "RETURNING created,modified,rowId;";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.DbType = DbType.String;
                insertParam1.ParameterName = "@key";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.DbType = DbType.String;
                insertParam2.ParameterName = "@value";
                insertCommand.Parameters.Add(insertParam2);
                insertParam1.Value = item.key;
                insertParam2.Value = item.value;
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    item.created = new UnixTimeUtc(created);
                    long modified = (long) rdr[1];
                    item.modified = new UnixTimeUtc((long)modified);
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        public virtual async Task<bool> TryInsertAsync(SettingsRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr = insertCommand.SqlNow();
                insertCommand.CommandText = "INSERT INTO Settings (key,value,created,modified) " +
                                            $"VALUES (@key,@value,{sqlNowStr},{sqlNowStr}) " +
                                            "ON CONFLICT DO NOTHING "+
                                            "RETURNING created,modified,rowId;";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.DbType = DbType.String;
                insertParam1.ParameterName = "@key";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.DbType = DbType.String;
                insertParam2.ParameterName = "@value";
                insertCommand.Parameters.Add(insertParam2);
                insertParam1.Value = item.key;
                insertParam2.Value = item.value;
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    item.created = new UnixTimeUtc(created);
                    long modified = (long) rdr[1];
                    item.modified = new UnixTimeUtc((long)modified);
                    item.rowId = (long) rdr[2];
                    return true;
                }
                return false;
            }
        }

        public virtual async Task<int> UpsertAsync(SettingsRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                string sqlNowStr = upsertCommand.SqlNow();
                upsertCommand.CommandText = "INSERT INTO Settings (key,value,created,modified) " +
                                            $"VALUES (@key,@value,{sqlNowStr},{sqlNowStr})"+
                                            "ON CONFLICT (key) DO UPDATE "+
                                            $"SET value = @value,modified = {upsertCommand.SqlMax()}(Settings.modified+1,{sqlNowStr}) "+
                                            "RETURNING created,modified,rowId;";
                var upsertParam1 = upsertCommand.CreateParameter();
                upsertParam1.DbType = DbType.String;
                upsertParam1.ParameterName = "@key";
                upsertCommand.Parameters.Add(upsertParam1);
                var upsertParam2 = upsertCommand.CreateParameter();
                upsertParam2.DbType = DbType.String;
                upsertParam2.ParameterName = "@value";
                upsertCommand.Parameters.Add(upsertParam2);
                upsertParam1.Value = item.key;
                upsertParam2.Value = item.value;
                await using var rdr = await upsertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    item.created = new UnixTimeUtc(created);
                    long modified = (long) rdr[1];
                    item.modified = new UnixTimeUtc((long)modified);
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        public virtual async Task<int> UpdateAsync(SettingsRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                string sqlNowStr = updateCommand.SqlNow();
                updateCommand.CommandText = "UPDATE Settings " +
                                            $"SET value = @value,modified = {updateCommand.SqlMax()}(Settings.modified+1,{sqlNowStr}) "+
                                            "WHERE (key = @key) "+
                                            "RETURNING created,modified,rowId;";
                var updateParam1 = updateCommand.CreateParameter();
                updateParam1.DbType = DbType.String;
                updateParam1.ParameterName = "@key";
                updateCommand.Parameters.Add(updateParam1);
                var updateParam2 = updateCommand.CreateParameter();
                updateParam2.DbType = DbType.String;
                updateParam2.ParameterName = "@value";
                updateCommand.Parameters.Add(updateParam2);
                updateParam1.Value = item.key;
                updateParam2.Value = item.value;
                await using var rdr = await updateCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    item.created = new UnixTimeUtc(created);
                    long modified = (long) rdr[1];
                    item.modified = new UnixTimeUtc((long)modified);
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
                getCountCommand.CommandText = "SELECT COUNT(*) FROM Settings;";
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
            sl.Add("key");
            sl.Add("value");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        // SELECT rowId,key,value,created,modified
        public SettingsRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<SettingsRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new SettingsRecord();
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.key = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[1];
            item.value = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[2];
            item.created = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[3]);
            item.modified = (rdr[4] == DBNull.Value) ? item.created : new UnixTimeUtc((long)rdr[4]); // HACK
            return item;
       }

        public virtual async Task<int> DeleteAsync(string key)
        {
            if (key == null) throw new OdinDatabaseValidationException("Cannot be null key");
            if (key?.Length < 0) throw new OdinDatabaseValidationException($"Too short key, was {key.Length} (min 0)");
            if (key?.Length > 65535) throw new OdinDatabaseValidationException($"Too long key, was {key.Length} (max 65535)");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM Settings " +
                                             "WHERE key = @key";
                var delete0Param1 = delete0Command.CreateParameter();
                delete0Param1.DbType = DbType.String;
                delete0Param1.ParameterName = "@key";
                delete0Command.Parameters.Add(delete0Param1);

                delete0Param1.Value = key;
                var count = await delete0Command.ExecuteNonQueryAsync();
                return count;
            }
        }

        public virtual async Task<SettingsRecord> PopAsync(string key)
        {
            if (key == null) throw new OdinDatabaseValidationException("Cannot be null key");
            if (key?.Length < 0) throw new OdinDatabaseValidationException($"Too short key, was {key.Length} (min 0)");
            if (key?.Length > 65535) throw new OdinDatabaseValidationException($"Too long key, was {key.Length} (max 65535)");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var deleteCommand = cn.CreateCommand();
            {
                deleteCommand.CommandText = "DELETE FROM Settings " +
                                             "WHERE key = @key " + 
                                             "RETURNING rowId,value,created,modified";
                var deleteParam1 = deleteCommand.CreateParameter();
                deleteParam1.DbType = DbType.String;
                deleteParam1.ParameterName = "@key";
                deleteCommand.Parameters.Add(deleteParam1);

                deleteParam1.Value = key;
                using (var rdr = await deleteCommand.ExecuteReaderAsync(CommandBehavior.SingleRow))
                {
                    if (await rdr.ReadAsync())
                    {
                       return ReadRecordFromReader0(rdr,key);
                    }
                    else
                    {
                       return null;
                    }
                }
            }
        }

        public SettingsRecord ReadRecordFromReader0(DbDataReader rdr,string key)
        {
            if (key == null) throw new OdinDatabaseValidationException("Cannot be null key");
            if (key?.Length < 0) throw new OdinDatabaseValidationException($"Too short key, was {key.Length} (min 0)");
            if (key?.Length > 65535) throw new OdinDatabaseValidationException($"Too long key, was {key.Length} (max 65535)");
            var result = new List<SettingsRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new SettingsRecord();
            item.key = key;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.value = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[1];
            item.created = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[2]);
            item.modified = (rdr[3] == DBNull.Value) ? item.created : new UnixTimeUtc((long)rdr[3]); // HACK
            return item;
       }

        public virtual async Task<SettingsRecord> GetAsync(string key)
        {
            if (key == null) throw new OdinDatabaseValidationException("Cannot be null key");
            if (key?.Length < 0) throw new OdinDatabaseValidationException($"Too short key, was {key.Length} (min 0)");
            if (key?.Length > 65535) throw new OdinDatabaseValidationException($"Too long key, was {key.Length} (max 65535)");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT rowId,value,created,modified FROM Settings " +
                                             "WHERE key = @key LIMIT 1;"+
                                             ";";
                var get0Param1 = get0Command.CreateParameter();
                get0Param1.DbType = DbType.String;
                get0Param1.ParameterName = "@key";
                get0Command.Parameters.Add(get0Param1);

                get0Param1.Value = key;
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr,key);
                        return r;
                    } // using
                } //
            } // using
        }

    }
}
