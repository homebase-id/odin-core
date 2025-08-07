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

namespace Odin.Core.Storage.Database.Identity.Table
{
    public record CatsRecord
    {
        public Int64 rowId { get; set; }
        public Guid identityId { get; set; }
        public Guid catId { get; set; }
        public OdinId issuedToId { get; set; }
        public UnixTimeUtc ttl { get; set; }
        public UnixTimeUtc expiresAt { get; set; }
        public Int32 catType { get; set; }
        public string value { get; set; }
        public UnixTimeUtc created { get; set; }
        public UnixTimeUtc modified { get; set; }
        public void Validate()
        {
            identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            catId.AssertGuidNotEmpty("Guid parameter catId cannot be set to Empty GUID.");
            if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short value, was {value.Length} (min 0)");
            if (value?.Length > 65535) throw new OdinDatabaseValidationException($"Too long value, was {value.Length} (max 65535)");
        }
    } // End of record CatsRecord

    public abstract class TableCatsCRUD : TableBase
    {
        private readonly CacheHelper _cache;
        private ScopedIdentityConnectionFactory _scopedConnectionFactory { get; init; }
        public override string TableName { get; } = "Cats";

        protected TableCatsCRUD(CacheHelper cache, ScopedIdentityConnectionFactory scopedConnectionFactory)
        {
            _cache = cache;
            _scopedConnectionFactory = scopedConnectionFactory;
        }


        public override async Task EnsureTableExistsAsync(bool dropExisting = false)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            if (dropExisting)
                await SqlHelper.DeleteTableAsync(cn, "Cats");
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowid BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE Cats IS '{ \"Version\": 0 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS Cats( -- { \"Version\": 0 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"catId BYTEA NOT NULL UNIQUE, "
                   +"issuedToId TEXT NOT NULL, "
                   +"ttl BIGINT NOT NULL, "
                   +"expiresAt BIGINT NOT NULL, "
                   +"catType BIGINT NOT NULL, "
                   +"value TEXT , "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +", UNIQUE(identityId,catId)"
                   +$"){wori};"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "Cats", createSql, commentSql);
        }

        protected virtual async Task<int> InsertAsync(CatsRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr = insertCommand.SqlNow();
                insertCommand.CommandText = "INSERT INTO Cats (identityId,catId,issuedToId,ttl,expiresAt,catType,value,created,modified) " +
                                           $"VALUES (@identityId,@catId,@issuedToId,@ttl,@expiresAt,@catType,@value,{sqlNowStr},{sqlNowStr})"+
                                            "RETURNING created,modified,rowId;";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.DbType = DbType.Binary;
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.DbType = DbType.Binary;
                insertParam2.ParameterName = "@catId";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.DbType = DbType.String;
                insertParam3.ParameterName = "@issuedToId";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.DbType = DbType.Int64;
                insertParam4.ParameterName = "@ttl";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.DbType = DbType.Int64;
                insertParam5.ParameterName = "@expiresAt";
                insertCommand.Parameters.Add(insertParam5);
                var insertParam6 = insertCommand.CreateParameter();
                insertParam6.DbType = DbType.Int32;
                insertParam6.ParameterName = "@catType";
                insertCommand.Parameters.Add(insertParam6);
                var insertParam7 = insertCommand.CreateParameter();
                insertParam7.DbType = DbType.String;
                insertParam7.ParameterName = "@value";
                insertCommand.Parameters.Add(insertParam7);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.catId.ToByteArray();
                insertParam3.Value = item.issuedToId.DomainName;
                insertParam4.Value = item.ttl.milliseconds;
                insertParam5.Value = item.expiresAt.milliseconds;
                insertParam6.Value = item.catType;
                insertParam7.Value = item.value ?? (object)DBNull.Value;
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    item.created = new UnixTimeUtc(created);
                    long modified = (long) rdr[1];
                    item.modified = new UnixTimeUtc((long)modified);
                    item.rowId = (long) rdr[2];
                    _cache.AddOrUpdate("TableCatsCRUD", item.identityId.ToString()+item.catId.ToString(), item);
                    return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<bool> TryInsertAsync(CatsRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr = insertCommand.SqlNow();
                insertCommand.CommandText = "INSERT INTO Cats (identityId,catId,issuedToId,ttl,expiresAt,catType,value,created,modified) " +
                                            $"VALUES (@identityId,@catId,@issuedToId,@ttl,@expiresAt,@catType,@value,{sqlNowStr},{sqlNowStr}) " +
                                            "ON CONFLICT DO NOTHING "+
                                            "RETURNING created,modified,rowId;";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.DbType = DbType.Binary;
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.DbType = DbType.Binary;
                insertParam2.ParameterName = "@catId";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.DbType = DbType.String;
                insertParam3.ParameterName = "@issuedToId";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.DbType = DbType.Int64;
                insertParam4.ParameterName = "@ttl";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.DbType = DbType.Int64;
                insertParam5.ParameterName = "@expiresAt";
                insertCommand.Parameters.Add(insertParam5);
                var insertParam6 = insertCommand.CreateParameter();
                insertParam6.DbType = DbType.Int32;
                insertParam6.ParameterName = "@catType";
                insertCommand.Parameters.Add(insertParam6);
                var insertParam7 = insertCommand.CreateParameter();
                insertParam7.DbType = DbType.String;
                insertParam7.ParameterName = "@value";
                insertCommand.Parameters.Add(insertParam7);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.catId.ToByteArray();
                insertParam3.Value = item.issuedToId.DomainName;
                insertParam4.Value = item.ttl.milliseconds;
                insertParam5.Value = item.expiresAt.milliseconds;
                insertParam6.Value = item.catType;
                insertParam7.Value = item.value ?? (object)DBNull.Value;
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    item.created = new UnixTimeUtc(created);
                    long modified = (long) rdr[1];
                    item.modified = new UnixTimeUtc((long)modified);
                    item.rowId = (long) rdr[2];
                   _cache.AddOrUpdate("TableCatsCRUD", item.identityId.ToString()+item.catId.ToString(), item);
                    return true;
                }
                return false;
            }
        }

        protected virtual async Task<int> UpsertAsync(CatsRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                string sqlNowStr = upsertCommand.SqlNow();
                upsertCommand.CommandText = "INSERT INTO Cats (identityId,catId,issuedToId,ttl,expiresAt,catType,value,created,modified) " +
                                            $"VALUES (@identityId,@catId,@issuedToId,@ttl,@expiresAt,@catType,@value,{sqlNowStr},{sqlNowStr})"+
                                            "ON CONFLICT (identityId,catId) DO UPDATE "+
                                            $"SET issuedToId = @issuedToId,ttl = @ttl,expiresAt = @expiresAt,catType = @catType,value = @value,modified = {upsertCommand.SqlMax()}(Cats.modified+1,{sqlNowStr}) "+
                                            "RETURNING created,modified,rowId;";
                var upsertParam1 = upsertCommand.CreateParameter();
                upsertParam1.DbType = DbType.Binary;
                upsertParam1.ParameterName = "@identityId";
                upsertCommand.Parameters.Add(upsertParam1);
                var upsertParam2 = upsertCommand.CreateParameter();
                upsertParam2.DbType = DbType.Binary;
                upsertParam2.ParameterName = "@catId";
                upsertCommand.Parameters.Add(upsertParam2);
                var upsertParam3 = upsertCommand.CreateParameter();
                upsertParam3.DbType = DbType.String;
                upsertParam3.ParameterName = "@issuedToId";
                upsertCommand.Parameters.Add(upsertParam3);
                var upsertParam4 = upsertCommand.CreateParameter();
                upsertParam4.DbType = DbType.Int64;
                upsertParam4.ParameterName = "@ttl";
                upsertCommand.Parameters.Add(upsertParam4);
                var upsertParam5 = upsertCommand.CreateParameter();
                upsertParam5.DbType = DbType.Int64;
                upsertParam5.ParameterName = "@expiresAt";
                upsertCommand.Parameters.Add(upsertParam5);
                var upsertParam6 = upsertCommand.CreateParameter();
                upsertParam6.DbType = DbType.Int32;
                upsertParam6.ParameterName = "@catType";
                upsertCommand.Parameters.Add(upsertParam6);
                var upsertParam7 = upsertCommand.CreateParameter();
                upsertParam7.DbType = DbType.String;
                upsertParam7.ParameterName = "@value";
                upsertCommand.Parameters.Add(upsertParam7);
                upsertParam1.Value = item.identityId.ToByteArray();
                upsertParam2.Value = item.catId.ToByteArray();
                upsertParam3.Value = item.issuedToId.DomainName;
                upsertParam4.Value = item.ttl.milliseconds;
                upsertParam5.Value = item.expiresAt.milliseconds;
                upsertParam6.Value = item.catType;
                upsertParam7.Value = item.value ?? (object)DBNull.Value;
                await using var rdr = await upsertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    item.created = new UnixTimeUtc(created);
                    long modified = (long) rdr[1];
                    item.modified = new UnixTimeUtc((long)modified);
                    item.rowId = (long) rdr[2];
                   _cache.AddOrUpdate("TableCatsCRUD", item.identityId.ToString()+item.catId.ToString(), item);
                    return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<int> UpdateAsync(CatsRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                string sqlNowStr = updateCommand.SqlNow();
                updateCommand.CommandText = "UPDATE Cats " +
                                            $"SET issuedToId = @issuedToId,ttl = @ttl,expiresAt = @expiresAt,catType = @catType,value = @value,modified = {updateCommand.SqlMax()}(Cats.modified+1,{sqlNowStr}) "+
                                            "WHERE (identityId = @identityId AND catId = @catId) "+
                                            "RETURNING created,modified,rowId;";
                var updateParam1 = updateCommand.CreateParameter();
                updateParam1.DbType = DbType.Binary;
                updateParam1.ParameterName = "@identityId";
                updateCommand.Parameters.Add(updateParam1);
                var updateParam2 = updateCommand.CreateParameter();
                updateParam2.DbType = DbType.Binary;
                updateParam2.ParameterName = "@catId";
                updateCommand.Parameters.Add(updateParam2);
                var updateParam3 = updateCommand.CreateParameter();
                updateParam3.DbType = DbType.String;
                updateParam3.ParameterName = "@issuedToId";
                updateCommand.Parameters.Add(updateParam3);
                var updateParam4 = updateCommand.CreateParameter();
                updateParam4.DbType = DbType.Int64;
                updateParam4.ParameterName = "@ttl";
                updateCommand.Parameters.Add(updateParam4);
                var updateParam5 = updateCommand.CreateParameter();
                updateParam5.DbType = DbType.Int64;
                updateParam5.ParameterName = "@expiresAt";
                updateCommand.Parameters.Add(updateParam5);
                var updateParam6 = updateCommand.CreateParameter();
                updateParam6.DbType = DbType.Int32;
                updateParam6.ParameterName = "@catType";
                updateCommand.Parameters.Add(updateParam6);
                var updateParam7 = updateCommand.CreateParameter();
                updateParam7.DbType = DbType.String;
                updateParam7.ParameterName = "@value";
                updateCommand.Parameters.Add(updateParam7);
                updateParam1.Value = item.identityId.ToByteArray();
                updateParam2.Value = item.catId.ToByteArray();
                updateParam3.Value = item.issuedToId.DomainName;
                updateParam4.Value = item.ttl.milliseconds;
                updateParam5.Value = item.expiresAt.milliseconds;
                updateParam6.Value = item.catType;
                updateParam7.Value = item.value ?? (object)DBNull.Value;
                await using var rdr = await updateCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    item.created = new UnixTimeUtc(created);
                    long modified = (long) rdr[1];
                    item.modified = new UnixTimeUtc((long)modified);
                    item.rowId = (long) rdr[2];
                   _cache.AddOrUpdate("TableCatsCRUD", item.identityId.ToString()+item.catId.ToString(), item);
                    return 1;
                }
                return 0;
            }
        }

        protected new async Task<int> GetCountAsync()
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getCountCommand = cn.CreateCommand();
            {
                 // TODO: this is SQLite specific
                getCountCommand.CommandText = "SELECT COUNT(*) FROM Cats;";
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
            sl.Add("identityId");
            sl.Add("catId");
            sl.Add("issuedToId");
            sl.Add("ttl");
            sl.Add("expiresAt");
            sl.Add("catType");
            sl.Add("value");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        // SELECT rowId,identityId,catId,issuedToId,ttl,expiresAt,catType,value,created,modified
        protected CatsRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<CatsRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new CatsRecord();
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.identityId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.catId = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[2]);
            item.issuedToId = (rdr[3] == DBNull.Value) ?                 throw new Exception("item is NULL, but set as NOT NULL") : new OdinId((string)rdr[3]);
            item.ttl = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[4]);
            item.expiresAt = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[5]);
            item.catType = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[6];
            item.value = (rdr[7] == DBNull.Value) ? null : (string)rdr[7];
            item.created = (rdr[8] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[8]);
            item.modified = (rdr[9] == DBNull.Value) ? item.created : new UnixTimeUtc((long)rdr[9]); // HACK
            return item;
       }

        protected virtual async Task<int> DeleteAsync(Guid identityId,Guid catId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM Cats " +
                                             "WHERE identityId = @identityId AND catId = @catId";
                var delete0Param1 = delete0Command.CreateParameter();
                delete0Param1.DbType = DbType.Binary;
                delete0Param1.ParameterName = "@identityId";
                delete0Command.Parameters.Add(delete0Param1);
                var delete0Param2 = delete0Command.CreateParameter();
                delete0Param2.DbType = DbType.Binary;
                delete0Param2.ParameterName = "@catId";
                delete0Command.Parameters.Add(delete0Param2);

                delete0Param1.Value = identityId.ToByteArray();
                delete0Param2.Value = catId.ToByteArray();
                var count = await delete0Command.ExecuteNonQueryAsync();
                if (count > 0)
                    _cache.Remove("TableCatsCRUD", identityId.ToString()+catId.ToString());
                return count;
            }
        }

        protected virtual async Task<CatsRecord> PopAsync(Guid identityId,Guid catId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var deleteCommand = cn.CreateCommand();
            {
                deleteCommand.CommandText = "DELETE FROM Cats " +
                                             "WHERE identityId = @identityId AND catId = @catId " + 
                                             "RETURNING rowId,issuedToId,ttl,expiresAt,catType,value,created,modified";
                var deleteParam1 = deleteCommand.CreateParameter();
                deleteParam1.DbType = DbType.Binary;
                deleteParam1.ParameterName = "@identityId";
                deleteCommand.Parameters.Add(deleteParam1);
                var deleteParam2 = deleteCommand.CreateParameter();
                deleteParam2.DbType = DbType.Binary;
                deleteParam2.ParameterName = "@catId";
                deleteCommand.Parameters.Add(deleteParam2);

                deleteParam1.Value = identityId.ToByteArray();
                deleteParam2.Value = catId.ToByteArray();
                using (var rdr = await deleteCommand.ExecuteReaderAsync(CommandBehavior.SingleRow))
                {
                    if (await rdr.ReadAsync())
                    {
                       return ReadRecordFromReader0(rdr,identityId,catId);
                    }
                    else
                    {
                       return null;
                    }
                }
            }
        }

        protected CatsRecord ReadRecordFromReader0(DbDataReader rdr,Guid identityId,Guid catId)
        {
            var result = new List<CatsRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new CatsRecord();
            item.identityId = identityId;
            item.catId = catId;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.issuedToId = (rdr[1] == DBNull.Value) ?                 throw new Exception("item is NULL, but set as NOT NULL") : new OdinId((string)rdr[1]);
            item.ttl = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[2]);
            item.expiresAt = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[3]);
            item.catType = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[4];
            item.value = (rdr[5] == DBNull.Value) ? null : (string)rdr[5];
            item.created = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[6]);
            item.modified = (rdr[7] == DBNull.Value) ? item.created : new UnixTimeUtc((long)rdr[7]); // HACK
            return item;
       }

        protected virtual async Task<CatsRecord> GetAsync(Guid identityId,Guid catId)
        {
            var (hit, cacheObject) = _cache.Get("TableCatsCRUD", identityId.ToString()+catId.ToString());
            if (hit)
                return (CatsRecord)cacheObject;
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT rowId,issuedToId,ttl,expiresAt,catType,value,created,modified FROM Cats " +
                                             "WHERE identityId = @identityId AND catId = @catId LIMIT 1;"+
                                             ";";
                var get0Param1 = get0Command.CreateParameter();
                get0Param1.DbType = DbType.Binary;
                get0Param1.ParameterName = "@identityId";
                get0Command.Parameters.Add(get0Param1);
                var get0Param2 = get0Command.CreateParameter();
                get0Param2.DbType = DbType.Binary;
                get0Param2.ParameterName = "@catId";
                get0Command.Parameters.Add(get0Param2);

                get0Param1.Value = identityId.ToByteArray();
                get0Param2.Value = catId.ToByteArray();
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            _cache.AddOrUpdate("TableCatsCRUD", identityId.ToString()+catId.ToString(), null);
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr,identityId,catId);
                        _cache.AddOrUpdate("TableCatsCRUD", identityId.ToString()+catId.ToString(), r);
                        return r;
                    } // using
                } //
            } // using
        }

        protected CatsRecord ReadRecordFromReader1(DbDataReader rdr,Guid identityId)
        {
            var result = new List<CatsRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new CatsRecord();
            item.identityId = identityId;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.catId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.issuedToId = (rdr[2] == DBNull.Value) ?                 throw new Exception("item is NULL, but set as NOT NULL") : new OdinId((string)rdr[2]);
            item.ttl = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[3]);
            item.expiresAt = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[4]);
            item.catType = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[5];
            item.value = (rdr[6] == DBNull.Value) ? null : (string)rdr[6];
            item.created = (rdr[7] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[7]);
            item.modified = (rdr[8] == DBNull.Value) ? item.created : new UnixTimeUtc((long)rdr[8]); // HACK
            return item;
       }

        protected virtual async Task<List<CatsRecord>> GetAllCatsAsync(Guid identityId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get1Command = cn.CreateCommand();
            {
                get1Command.CommandText = "SELECT rowId,catId,issuedToId,ttl,expiresAt,catType,value,created,modified FROM Cats " +
                                             "WHERE identityId = @identityId;"+
                                             ";";
                var get1Param1 = get1Command.CreateParameter();
                get1Param1.DbType = DbType.Binary;
                get1Param1.ParameterName = "@identityId";
                get1Command.Parameters.Add(get1Param1);

                get1Param1.Value = identityId.ToByteArray();
                {
                    using (var rdr = await get1Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            _cache.AddOrUpdate("TableCatsCRUD", identityId.ToString(), null);
                            return new List<CatsRecord>();
                        }
                        var result = new List<CatsRecord>();
                        while (true)
                        {
                            result.Add(ReadRecordFromReader1(rdr,identityId));
                            if (!await rdr.ReadAsync())
                                break;
                        }
                        return result;
                    } // using
                } //
            } // using
        }

        protected virtual async Task<(List<CatsRecord>, Guid? nextCursor)> PagingByIdentityIdAsync(int count, Guid identityId, Guid? inCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");
            if (count == int.MaxValue)
                count--; // avoid overflow when doing +1 on the param below
            if (inCursor == null)
                inCursor = Guid.Empty;

            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getPaging1Command = cn.CreateCommand();
            {
                getPaging1Command.CommandText = "SELECT rowId,identityId,catId,issuedToId,ttl,expiresAt,catType,value,created,modified FROM Cats " +
                                            "WHERE (identityId = @identityId) AND identityId > @identityId  ORDER BY identityId ASC  LIMIT @count;";
                var getPaging1Param1 = getPaging1Command.CreateParameter();
                getPaging1Param1.DbType = DbType.Binary;
                getPaging1Param1.ParameterName = "@identityId";
                getPaging1Command.Parameters.Add(getPaging1Param1);
                var getPaging1Param2 = getPaging1Command.CreateParameter();
                getPaging1Param2.DbType = DbType.Int64;
                getPaging1Param2.ParameterName = "@count";
                getPaging1Command.Parameters.Add(getPaging1Param2);
                var getPaging1Param3 = getPaging1Command.CreateParameter();
                getPaging1Param3.DbType = DbType.Binary;
                getPaging1Param3.ParameterName = "@identityId";
                getPaging1Command.Parameters.Add(getPaging1Param3);

                getPaging1Param1.Value = inCursor?.ToByteArray();
                getPaging1Param2.Value = count+1;
                getPaging1Param3.Value = identityId.ToByteArray();

                {
                    await using (var rdr = await getPaging1Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        var result = new List<CatsRecord>();
                        Guid? nextCursor;
                        int n = 0;
                        while ((n < count) && await rdr.ReadAsync())
                        {
                            n++;
                            result.Add(ReadRecordFromReaderAll(rdr));
                        } // while
                        if ((n > 0) && await rdr.ReadAsync())
                        {
                                nextCursor = result[n - 1].identityId;
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

        protected virtual async Task<(List<CatsRecord>, Int64? nextCursor)> PagingByRowIdAsync(int count, Int64? inCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");
            if (count == int.MaxValue)
                count--; // avoid overflow when doing +1 on the param below
            if (inCursor == null)
                inCursor = 0;

            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getPaging0Command = cn.CreateCommand();
            {
                getPaging0Command.CommandText = "SELECT rowId,identityId,catId,issuedToId,ttl,expiresAt,catType,value,created,modified FROM Cats " +
                                            "WHERE rowId > @rowId  ORDER BY rowId ASC  LIMIT @count;";
                var getPaging0Param1 = getPaging0Command.CreateParameter();
                getPaging0Param1.DbType = DbType.Int64;
                getPaging0Param1.ParameterName = "@rowId";
                getPaging0Command.Parameters.Add(getPaging0Param1);
                var getPaging0Param2 = getPaging0Command.CreateParameter();
                getPaging0Param2.DbType = DbType.Int64;
                getPaging0Param2.ParameterName = "@count";
                getPaging0Command.Parameters.Add(getPaging0Param2);

                getPaging0Param1.Value = inCursor;
                getPaging0Param2.Value = count+1;

                {
                    await using (var rdr = await getPaging0Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        var result = new List<CatsRecord>();
                        Int64? nextCursor;
                        int n = 0;
                        while ((n < count) && await rdr.ReadAsync())
                        {
                            n++;
                            result.Add(ReadRecordFromReaderAll(rdr));
                        } // while
                        if ((n > 0) && await rdr.ReadAsync())
                        {
                                nextCursor = result[n - 1].rowId;
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
