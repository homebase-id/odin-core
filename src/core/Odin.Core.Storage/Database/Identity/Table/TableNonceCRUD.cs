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
using Odin.Core.Util;
using Odin.Core.Storage.Exceptions;
using Odin.Core.Storage.SQLite; //added for homebase social sync

// THIS FILE IS AUTO GENERATED - DO NOT EDIT

namespace Odin.Core.Storage.Database.Identity.Table
{
    public record NonceRecord
    {
        public Int64 rowId { get; set; }
        public Guid identityId { get; set; }
        public Guid id { get; set; }
        public UnixTimeUtc expiration { get; set; }
        public string data { get; set; }
        public UnixTimeUtc created { get; set; }
        public UnixTimeUtc modified { get; set; }
        public void Validate()
        {
            identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            id.AssertGuidNotEmpty("Guid parameter id cannot be set to Empty GUID.");
            if (data == null) throw new OdinDatabaseValidationException("Cannot be null data");
            if (data?.Length < 0) throw new OdinDatabaseValidationException($"Too short data, was {data.Length} (min 0)");
            if (data?.Length > 65000) throw new OdinDatabaseValidationException($"Too long data, was {data.Length} (max 65000)");
        }
    } // End of record NonceRecord

    public abstract class TableNonceCRUD
    {
        private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory;

        protected TableNonceCRUD(CacheHelper cache, ScopedIdentityConnectionFactory scopedConnectionFactory)
        {
            _scopedConnectionFactory = scopedConnectionFactory;
        }


        public virtual async Task EnsureTableExistsAsync(bool dropExisting = false)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            if (dropExisting)
                await MigrationBase.DeleteTableAsync(cn, "Nonce");
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowid BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE Nonce IS '{ \"Version\": 0 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS Nonce( -- { \"Version\": 0 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"id BYTEA NOT NULL UNIQUE, "
                   +"expiration BIGINT NOT NULL, "
                   +"data TEXT NOT NULL, "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +", UNIQUE(identityId,id)"
                   +$"){wori};"
                   ;
            await MigrationBase.CreateTableAsync(cn, createSql, commentSql);
        }

        protected virtual async Task<int> InsertAsync(NonceRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr = insertCommand.SqlNow();
                insertCommand.CommandText = "INSERT INTO Nonce (identityId,id,expiration,data,created,modified) " +
                                           $"VALUES (@identityId,@id,@expiration,@data,{sqlNowStr},{sqlNowStr})"+
                                            "RETURNING created,modified,rowId;";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.DbType = DbType.Binary;
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.DbType = DbType.Binary;
                insertParam2.ParameterName = "@id";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.DbType = DbType.Int64;
                insertParam3.ParameterName = "@expiration";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.DbType = DbType.String;
                insertParam4.ParameterName = "@data";
                insertCommand.Parameters.Add(insertParam4);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.id.ToByteArray();
                insertParam3.Value = item.expiration.milliseconds;
                insertParam4.Value = item.data;
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

        protected virtual async Task<bool> TryInsertAsync(NonceRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr = insertCommand.SqlNow();
                insertCommand.CommandText = "INSERT INTO Nonce (identityId,id,expiration,data,created,modified) " +
                                            $"VALUES (@identityId,@id,@expiration,@data,{sqlNowStr},{sqlNowStr}) " +
                                            "ON CONFLICT DO NOTHING "+
                                            "RETURNING created,modified,rowId;";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.DbType = DbType.Binary;
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.DbType = DbType.Binary;
                insertParam2.ParameterName = "@id";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.DbType = DbType.Int64;
                insertParam3.ParameterName = "@expiration";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.DbType = DbType.String;
                insertParam4.ParameterName = "@data";
                insertCommand.Parameters.Add(insertParam4);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.id.ToByteArray();
                insertParam3.Value = item.expiration.milliseconds;
                insertParam4.Value = item.data;
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

        protected virtual async Task<int> UpsertAsync(NonceRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                string sqlNowStr = upsertCommand.SqlNow();
                upsertCommand.CommandText = "INSERT INTO Nonce (identityId,id,expiration,data,created,modified) " +
                                            $"VALUES (@identityId,@id,@expiration,@data,{sqlNowStr},{sqlNowStr})"+
                                            "ON CONFLICT (identityId,id) DO UPDATE "+
                                            $"SET expiration = @expiration,data = @data,modified = {upsertCommand.SqlMax()}(Nonce.modified+1,{sqlNowStr}) "+
                                            "RETURNING created,modified,rowId;";
                var upsertParam1 = upsertCommand.CreateParameter();
                upsertParam1.DbType = DbType.Binary;
                upsertParam1.ParameterName = "@identityId";
                upsertCommand.Parameters.Add(upsertParam1);
                var upsertParam2 = upsertCommand.CreateParameter();
                upsertParam2.DbType = DbType.Binary;
                upsertParam2.ParameterName = "@id";
                upsertCommand.Parameters.Add(upsertParam2);
                var upsertParam3 = upsertCommand.CreateParameter();
                upsertParam3.DbType = DbType.Int64;
                upsertParam3.ParameterName = "@expiration";
                upsertCommand.Parameters.Add(upsertParam3);
                var upsertParam4 = upsertCommand.CreateParameter();
                upsertParam4.DbType = DbType.String;
                upsertParam4.ParameterName = "@data";
                upsertCommand.Parameters.Add(upsertParam4);
                upsertParam1.Value = item.identityId.ToByteArray();
                upsertParam2.Value = item.id.ToByteArray();
                upsertParam3.Value = item.expiration.milliseconds;
                upsertParam4.Value = item.data;
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

        protected virtual async Task<int> UpdateAsync(NonceRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                string sqlNowStr = updateCommand.SqlNow();
                updateCommand.CommandText = "UPDATE Nonce " +
                                            $"SET expiration = @expiration,data = @data,modified = {updateCommand.SqlMax()}(Nonce.modified+1,{sqlNowStr}) "+
                                            "WHERE (identityId = @identityId AND id = @id) "+
                                            "RETURNING created,modified,rowId;";
                var updateParam1 = updateCommand.CreateParameter();
                updateParam1.DbType = DbType.Binary;
                updateParam1.ParameterName = "@identityId";
                updateCommand.Parameters.Add(updateParam1);
                var updateParam2 = updateCommand.CreateParameter();
                updateParam2.DbType = DbType.Binary;
                updateParam2.ParameterName = "@id";
                updateCommand.Parameters.Add(updateParam2);
                var updateParam3 = updateCommand.CreateParameter();
                updateParam3.DbType = DbType.Int64;
                updateParam3.ParameterName = "@expiration";
                updateCommand.Parameters.Add(updateParam3);
                var updateParam4 = updateCommand.CreateParameter();
                updateParam4.DbType = DbType.String;
                updateParam4.ParameterName = "@data";
                updateCommand.Parameters.Add(updateParam4);
                updateParam1.Value = item.identityId.ToByteArray();
                updateParam2.Value = item.id.ToByteArray();
                updateParam3.Value = item.expiration.milliseconds;
                updateParam4.Value = item.data;
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

        protected virtual async Task<int> GetCountAsync()
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getCountCommand = cn.CreateCommand();
            {
                 // TODO: this is SQLite specific
                getCountCommand.CommandText = "SELECT COUNT(*) FROM Nonce;";
                var count = await getCountCommand.ExecuteScalarAsync();
                if (count == null || count == DBNull.Value || !(count is int || count is long))
                    return -1;
                else
                    return Convert.ToInt32(count);
            }
        }

        public static List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("rowId");
            sl.Add("identityId");
            sl.Add("id");
            sl.Add("expiration");
            sl.Add("data");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        // SELECT rowId,identityId,id,expiration,data,created,modified
        protected NonceRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<NonceRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new NonceRecord();
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.identityId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.id = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[2]);
            item.expiration = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[3]);
            item.data = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[4];
            item.created = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[5]);
            item.modified = (rdr[6] == DBNull.Value) ? item.created : new UnixTimeUtc((long)rdr[6]); // HACK
            return item;
       }

        protected virtual async Task<int> DeleteAsync(Guid identityId,Guid id)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM Nonce " +
                                             "WHERE identityId = @identityId AND id = @id";
                var delete0Param1 = delete0Command.CreateParameter();
                delete0Param1.DbType = DbType.Binary;
                delete0Param1.ParameterName = "@identityId";
                delete0Command.Parameters.Add(delete0Param1);
                var delete0Param2 = delete0Command.CreateParameter();
                delete0Param2.DbType = DbType.Binary;
                delete0Param2.ParameterName = "@id";
                delete0Command.Parameters.Add(delete0Param2);

                delete0Param1.Value = identityId.ToByteArray();
                delete0Param2.Value = id.ToByteArray();
                var count = await delete0Command.ExecuteNonQueryAsync();
                return count;
            }
        }

        protected virtual async Task<NonceRecord> PopAsync(Guid identityId,Guid id)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var deleteCommand = cn.CreateCommand();
            {
                deleteCommand.CommandText = "DELETE FROM Nonce " +
                                             "WHERE identityId = @identityId AND id = @id " + 
                                             "RETURNING rowId,expiration,data,created,modified";
                var deleteParam1 = deleteCommand.CreateParameter();
                deleteParam1.DbType = DbType.Binary;
                deleteParam1.ParameterName = "@identityId";
                deleteCommand.Parameters.Add(deleteParam1);
                var deleteParam2 = deleteCommand.CreateParameter();
                deleteParam2.DbType = DbType.Binary;
                deleteParam2.ParameterName = "@id";
                deleteCommand.Parameters.Add(deleteParam2);

                deleteParam1.Value = identityId.ToByteArray();
                deleteParam2.Value = id.ToByteArray();
                using (var rdr = await deleteCommand.ExecuteReaderAsync(CommandBehavior.SingleRow))
                {
                    if (await rdr.ReadAsync())
                    {
                       return ReadRecordFromReader0(rdr,identityId,id);
                    }
                    else
                    {
                       return null;
                    }
                }
            }
        }

        protected NonceRecord ReadRecordFromReader0(DbDataReader rdr,Guid identityId,Guid id)
        {
            var result = new List<NonceRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new NonceRecord();
            item.identityId = identityId;
            item.id = id;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.expiration = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[1]);
            item.data = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[2];
            item.created = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[3]);
            item.modified = (rdr[4] == DBNull.Value) ? item.created : new UnixTimeUtc((long)rdr[4]); // HACK
            return item;
       }

        protected virtual async Task<NonceRecord> GetAsync(Guid identityId,Guid id)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT rowId,expiration,data,created,modified FROM Nonce " +
                                             "WHERE identityId = @identityId AND id = @id LIMIT 1;"+
                                             ";";
                var get0Param1 = get0Command.CreateParameter();
                get0Param1.DbType = DbType.Binary;
                get0Param1.ParameterName = "@identityId";
                get0Command.Parameters.Add(get0Param1);
                var get0Param2 = get0Command.CreateParameter();
                get0Param2.DbType = DbType.Binary;
                get0Param2.ParameterName = "@id";
                get0Command.Parameters.Add(get0Param2);

                get0Param1.Value = identityId.ToByteArray();
                get0Param2.Value = id.ToByteArray();
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr,identityId,id);
                        return r;
                    } // using
                } //
            } // using
        }

    }
}
