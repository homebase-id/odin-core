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
    public record KeyThreeValueRecord
    {
        public Int64 rowId { get; set; }
        public Guid identityId { get; set; }
        public byte[] key1 { get; set; }
        public byte[] key2 { get; set; }
        public byte[] key3 { get; set; }
        public byte[] data { get; set; }
        public void Validate()
        {
            identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            if (key1 == null) throw new OdinDatabaseValidationException("Cannot be null key1");
            if (key1?.Length < 16) throw new OdinDatabaseValidationException($"Too short key1, was {key1.Length} (min 16)");
            if (key1?.Length > 48) throw new OdinDatabaseValidationException($"Too long key1, was {key1.Length} (max 48)");
            if (key2?.Length < 0) throw new OdinDatabaseValidationException($"Too short key2, was {key2.Length} (min 0)");
            if (key2?.Length > 256) throw new OdinDatabaseValidationException($"Too long key2, was {key2.Length} (max 256)");
            if (key3?.Length < 0) throw new OdinDatabaseValidationException($"Too short key3, was {key3.Length} (min 0)");
            if (key3?.Length > 256) throw new OdinDatabaseValidationException($"Too long key3, was {key3.Length} (max 256)");
            if (data?.Length < 0) throw new OdinDatabaseValidationException($"Too short data, was {data.Length} (min 0)");
            if (data?.Length > 1048576) throw new OdinDatabaseValidationException($"Too long data, was {data.Length} (max 1048576)");
        }
    } // End of record KeyThreeValueRecord

    public abstract class TableKeyThreeValueCRUD
    {
        private readonly CacheHelper _cache;
        private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory;

        protected TableKeyThreeValueCRUD(CacheHelper cache, ScopedIdentityConnectionFactory scopedConnectionFactory)
        {
            _cache = cache;
            _scopedConnectionFactory = scopedConnectionFactory;
        }


        public virtual async Task EnsureTableExistsAsync(bool dropExisting = false)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            if (dropExisting)
                await SqlHelper.DeleteTableAsync(cn, "KeyThreeValue");
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowid BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE KeyThreeValue IS '{ \"Version\": 0 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS KeyThreeValue( -- { \"Version\": 0 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"key1 BYTEA NOT NULL, "
                   +"key2 BYTEA , "
                   +"key3 BYTEA , "
                   +"data BYTEA  "
                   +", UNIQUE(identityId,key1)"
                   +$"){wori};"
                   +"CREATE INDEX Idx0KeyThreeValue ON KeyThreeValue(identityId,key2);"
                   +"CREATE INDEX Idx1KeyThreeValue ON KeyThreeValue(key3);"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "KeyThreeValue", createSql, commentSql);
        }

        protected virtual async Task<int> InsertAsync(KeyThreeValueRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO KeyThreeValue (identityId,key1,key2,key3,data) " +
                                           $"VALUES (@identityId,@key1,@key2,@key3,@data)"+
                                            "RETURNING -1,-1,rowId;";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.DbType = DbType.Binary;
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.DbType = DbType.Binary;
                insertParam2.ParameterName = "@key1";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.DbType = DbType.Binary;
                insertParam3.ParameterName = "@key2";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.DbType = DbType.Binary;
                insertParam4.ParameterName = "@key3";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.DbType = DbType.Binary;
                insertParam5.ParameterName = "@data";
                insertCommand.Parameters.Add(insertParam5);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.key1;
                insertParam3.Value = item.key2 ?? (object)DBNull.Value;
                insertParam4.Value = item.key3 ?? (object)DBNull.Value;
                insertParam5.Value = item.data ?? (object)DBNull.Value;
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                    _cache.AddOrUpdate("TableKeyThreeValueCRUD", item.identityId.ToString()+item.key1.ToBase64(), item);
                    return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<bool> TryInsertAsync(KeyThreeValueRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO KeyThreeValue (identityId,key1,key2,key3,data) " +
                                            $"VALUES (@identityId,@key1,@key2,@key3,@data) " +
                                            "ON CONFLICT DO NOTHING "+
                                            "RETURNING -1,-1,rowId;";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.DbType = DbType.Binary;
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.DbType = DbType.Binary;
                insertParam2.ParameterName = "@key1";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.DbType = DbType.Binary;
                insertParam3.ParameterName = "@key2";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.DbType = DbType.Binary;
                insertParam4.ParameterName = "@key3";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.DbType = DbType.Binary;
                insertParam5.ParameterName = "@data";
                insertCommand.Parameters.Add(insertParam5);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.key1;
                insertParam3.Value = item.key2 ?? (object)DBNull.Value;
                insertParam4.Value = item.key3 ?? (object)DBNull.Value;
                insertParam5.Value = item.data ?? (object)DBNull.Value;
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                   _cache.AddOrUpdate("TableKeyThreeValueCRUD", item.identityId.ToString()+item.key1.ToBase64(), item);
                    return true;
                }
                return false;
            }
        }

        protected virtual async Task<int> UpsertAsync(KeyThreeValueRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                upsertCommand.CommandText = "INSERT INTO KeyThreeValue (identityId,key1,key2,key3,data) " +
                                            $"VALUES (@identityId,@key1,@key2,@key3,@data)"+
                                            "ON CONFLICT (identityId,key1) DO UPDATE "+
                                            $"SET key2 = @key2,key3 = @key3,data = @data "+
                                            "RETURNING -1,-1,rowId;";
                var upsertParam1 = upsertCommand.CreateParameter();
                upsertParam1.DbType = DbType.Binary;
                upsertParam1.ParameterName = "@identityId";
                upsertCommand.Parameters.Add(upsertParam1);
                var upsertParam2 = upsertCommand.CreateParameter();
                upsertParam2.DbType = DbType.Binary;
                upsertParam2.ParameterName = "@key1";
                upsertCommand.Parameters.Add(upsertParam2);
                var upsertParam3 = upsertCommand.CreateParameter();
                upsertParam3.DbType = DbType.Binary;
                upsertParam3.ParameterName = "@key2";
                upsertCommand.Parameters.Add(upsertParam3);
                var upsertParam4 = upsertCommand.CreateParameter();
                upsertParam4.DbType = DbType.Binary;
                upsertParam4.ParameterName = "@key3";
                upsertCommand.Parameters.Add(upsertParam4);
                var upsertParam5 = upsertCommand.CreateParameter();
                upsertParam5.DbType = DbType.Binary;
                upsertParam5.ParameterName = "@data";
                upsertCommand.Parameters.Add(upsertParam5);
                upsertParam1.Value = item.identityId.ToByteArray();
                upsertParam2.Value = item.key1;
                upsertParam3.Value = item.key2 ?? (object)DBNull.Value;
                upsertParam4.Value = item.key3 ?? (object)DBNull.Value;
                upsertParam5.Value = item.data ?? (object)DBNull.Value;
                await using var rdr = await upsertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                   _cache.AddOrUpdate("TableKeyThreeValueCRUD", item.identityId.ToString()+item.key1.ToBase64(), item);
                    return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<int> UpdateAsync(KeyThreeValueRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                updateCommand.CommandText = "UPDATE KeyThreeValue " +
                                            $"SET key2 = @key2,key3 = @key3,data = @data "+
                                            "WHERE (identityId = @identityId AND key1 = @key1) "+
                                            "RETURNING -1,-1,rowId;";
                var updateParam1 = updateCommand.CreateParameter();
                updateParam1.DbType = DbType.Binary;
                updateParam1.ParameterName = "@identityId";
                updateCommand.Parameters.Add(updateParam1);
                var updateParam2 = updateCommand.CreateParameter();
                updateParam2.DbType = DbType.Binary;
                updateParam2.ParameterName = "@key1";
                updateCommand.Parameters.Add(updateParam2);
                var updateParam3 = updateCommand.CreateParameter();
                updateParam3.DbType = DbType.Binary;
                updateParam3.ParameterName = "@key2";
                updateCommand.Parameters.Add(updateParam3);
                var updateParam4 = updateCommand.CreateParameter();
                updateParam4.DbType = DbType.Binary;
                updateParam4.ParameterName = "@key3";
                updateCommand.Parameters.Add(updateParam4);
                var updateParam5 = updateCommand.CreateParameter();
                updateParam5.DbType = DbType.Binary;
                updateParam5.ParameterName = "@data";
                updateCommand.Parameters.Add(updateParam5);
                updateParam1.Value = item.identityId.ToByteArray();
                updateParam2.Value = item.key1;
                updateParam3.Value = item.key2 ?? (object)DBNull.Value;
                updateParam4.Value = item.key3 ?? (object)DBNull.Value;
                updateParam5.Value = item.data ?? (object)DBNull.Value;
                await using var rdr = await updateCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                   _cache.AddOrUpdate("TableKeyThreeValueCRUD", item.identityId.ToString()+item.key1.ToBase64(), item);
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
                getCountCommand.CommandText = "SELECT COUNT(*) FROM KeyThreeValue;";
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
            sl.Add("key1");
            sl.Add("key2");
            sl.Add("key3");
            sl.Add("data");
            return sl;
        }

        // SELECT rowId,identityId,key1,key2,key3,data
        protected KeyThreeValueRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<KeyThreeValueRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new KeyThreeValueRecord();
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.identityId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.key1 = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (byte[])(rdr[2]);
            if (item.key1?.Length < 16)
                throw new Exception("Too little data in key1...");
            item.key2 = (rdr[3] == DBNull.Value) ? null : (byte[])(rdr[3]);
            if (item.key2?.Length < 0)
                throw new Exception("Too little data in key2...");
            item.key3 = (rdr[4] == DBNull.Value) ? null : (byte[])(rdr[4]);
            if (item.key3?.Length < 0)
                throw new Exception("Too little data in key3...");
            item.data = (rdr[5] == DBNull.Value) ? null : (byte[])(rdr[5]);
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            return item;
       }

        protected virtual async Task<int> DeleteAsync(Guid identityId,byte[] key1)
        {
            if (key1 == null) throw new OdinDatabaseValidationException("Cannot be null key1");
            if (key1?.Length < 16) throw new OdinDatabaseValidationException($"Too short key1, was {key1.Length} (min 16)");
            if (key1?.Length > 48) throw new OdinDatabaseValidationException($"Too long key1, was {key1.Length} (max 48)");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM KeyThreeValue " +
                                             "WHERE identityId = @identityId AND key1 = @key1";
                var delete0Param1 = delete0Command.CreateParameter();
                delete0Param1.DbType = DbType.Binary;
                delete0Param1.ParameterName = "@identityId";
                delete0Command.Parameters.Add(delete0Param1);
                var delete0Param2 = delete0Command.CreateParameter();
                delete0Param2.DbType = DbType.Binary;
                delete0Param2.ParameterName = "@key1";
                delete0Command.Parameters.Add(delete0Param2);

                delete0Param1.Value = identityId.ToByteArray();
                delete0Param2.Value = key1;
                var count = await delete0Command.ExecuteNonQueryAsync();
                if (count > 0)
                    _cache.Remove("TableKeyThreeValueCRUD", identityId.ToString()+key1.ToBase64());
                return count;
            }
        }

        protected virtual async Task<KeyThreeValueRecord> PopAsync(Guid identityId,byte[] key1)
        {
            if (key1 == null) throw new OdinDatabaseValidationException("Cannot be null key1");
            if (key1?.Length < 16) throw new OdinDatabaseValidationException($"Too short key1, was {key1.Length} (min 16)");
            if (key1?.Length > 48) throw new OdinDatabaseValidationException($"Too long key1, was {key1.Length} (max 48)");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var deleteCommand = cn.CreateCommand();
            {
                deleteCommand.CommandText = "DELETE FROM KeyThreeValue " +
                                             "WHERE identityId = @identityId AND key1 = @key1 " + 
                                             "RETURNING rowId,key2,key3,data";
                var deleteParam1 = deleteCommand.CreateParameter();
                deleteParam1.DbType = DbType.Binary;
                deleteParam1.ParameterName = "@identityId";
                deleteCommand.Parameters.Add(deleteParam1);
                var deleteParam2 = deleteCommand.CreateParameter();
                deleteParam2.DbType = DbType.Binary;
                deleteParam2.ParameterName = "@key1";
                deleteCommand.Parameters.Add(deleteParam2);

                deleteParam1.Value = identityId.ToByteArray();
                deleteParam2.Value = key1;
                using (var rdr = await deleteCommand.ExecuteReaderAsync(CommandBehavior.SingleRow))
                {
                    if (await rdr.ReadAsync())
                    {
                       return ReadRecordFromReader0(rdr,identityId,key1);
                    }
                    else
                    {
                       return null;
                    }
                }
            }
        }

        protected KeyThreeValueRecord ReadRecordFromReader0(DbDataReader rdr,Guid identityId,byte[] key1)
        {
            if (key1 == null) throw new OdinDatabaseValidationException("Cannot be null key1");
            if (key1?.Length < 16) throw new OdinDatabaseValidationException($"Too short key1, was {key1.Length} (min 16)");
            if (key1?.Length > 48) throw new OdinDatabaseValidationException($"Too long key1, was {key1.Length} (max 48)");
            var result = new List<KeyThreeValueRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new KeyThreeValueRecord();
            item.identityId = identityId;
            item.key1 = key1;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.key2 = (rdr[1] == DBNull.Value) ? null : (byte[])(rdr[1]);
            if (item.key2?.Length < 0)
                throw new Exception("Too little data in key2...");
            item.key3 = (rdr[2] == DBNull.Value) ? null : (byte[])(rdr[2]);
            if (item.key3?.Length < 0)
                throw new Exception("Too little data in key3...");
            item.data = (rdr[3] == DBNull.Value) ? null : (byte[])(rdr[3]);
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            return item;
       }

        protected virtual async Task<KeyThreeValueRecord> GetAsync(Guid identityId,byte[] key1)
        {
            if (key1 == null) throw new OdinDatabaseValidationException("Cannot be null key1");
            if (key1?.Length < 16) throw new OdinDatabaseValidationException($"Too short key1, was {key1.Length} (min 16)");
            if (key1?.Length > 48) throw new OdinDatabaseValidationException($"Too long key1, was {key1.Length} (max 48)");
            var (hit, cacheObject) = _cache.Get("TableKeyThreeValueCRUD", identityId.ToString()+key1.ToBase64());
            if (hit)
                return (KeyThreeValueRecord)cacheObject;
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT rowId,key2,key3,data FROM KeyThreeValue " +
                                             "WHERE identityId = @identityId AND key1 = @key1 LIMIT 1;"+
                                             ";";
                var get0Param1 = get0Command.CreateParameter();
                get0Param1.DbType = DbType.Binary;
                get0Param1.ParameterName = "@identityId";
                get0Command.Parameters.Add(get0Param1);
                var get0Param2 = get0Command.CreateParameter();
                get0Param2.DbType = DbType.Binary;
                get0Param2.ParameterName = "@key1";
                get0Command.Parameters.Add(get0Param2);

                get0Param1.Value = identityId.ToByteArray();
                get0Param2.Value = key1;
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            _cache.AddOrUpdate("TableKeyThreeValueCRUD", identityId.ToString()+key1.ToBase64(), null);
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr,identityId,key1);
                        _cache.AddOrUpdate("TableKeyThreeValueCRUD", identityId.ToString()+key1.ToBase64(), r);
                        return r;
                    } // using
                } //
            } // using
        }

        protected virtual async Task<List<byte[]>> GetByKeyTwoAsync(Guid identityId,byte[] key2)
        {
            if (key2?.Length < 0) throw new OdinDatabaseValidationException($"Too short key2, was {key2.Length} (min 0)");
            if (key2?.Length > 256) throw new OdinDatabaseValidationException($"Too long key2, was {key2.Length} (max 256)");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get1Command = cn.CreateCommand();
            {
                get1Command.CommandText = "SELECT data FROM KeyThreeValue " +
                                             "WHERE identityId = @identityId AND key2 = @key2;"+
                                             ";";
                var get1Param1 = get1Command.CreateParameter();
                get1Param1.DbType = DbType.Binary;
                get1Param1.ParameterName = "@identityId";
                get1Command.Parameters.Add(get1Param1);
                var get1Param2 = get1Command.CreateParameter();
                get1Param2.DbType = DbType.Binary;
                get1Param2.ParameterName = "@key2";
                get1Command.Parameters.Add(get1Param2);

                get1Param1.Value = identityId.ToByteArray();
                get1Param2.Value = key2 ?? (object)DBNull.Value;
                {
                    using (var rdr = await get1Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        byte[] result0tmp;
                        var thelistresult = new List<byte[]>();
                        if (!await rdr.ReadAsync()) {
                            return thelistresult;
                        }
                    byte[] tmpbuf = new byte[1048576+1];
                    var guid = new byte[16];
                    while (true)
                    {

                        if (rdr[0] == DBNull.Value)
                            result0tmp = null;
                        else
                        {
                            tmpbuf = (byte[]) rdr[0];
                            if (tmpbuf.Length < 0)
                                throw new Exception("Too little data in data...");
                            result0tmp = new byte[tmpbuf.Length];
                            Buffer.BlockCopy(tmpbuf, 0, result0tmp, 0, (int) tmpbuf.Length);
                        }
                        thelistresult.Add(result0tmp);
                        if (!await rdr.ReadAsync())
                           break;
                    } // while
                    return thelistresult;
                    } // using
                } //
            } // using
        }

        protected virtual async Task<List<byte[]>> GetByKeyThreeAsync(Guid identityId,byte[] key3)
        {
            if (key3?.Length < 0) throw new OdinDatabaseValidationException($"Too short key3, was {key3.Length} (min 0)");
            if (key3?.Length > 256) throw new OdinDatabaseValidationException($"Too long key3, was {key3.Length} (max 256)");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get2Command = cn.CreateCommand();
            {
                get2Command.CommandText = "SELECT data FROM KeyThreeValue " +
                                             "WHERE identityId = @identityId AND key3 = @key3;"+
                                             ";";
                var get2Param1 = get2Command.CreateParameter();
                get2Param1.DbType = DbType.Binary;
                get2Param1.ParameterName = "@identityId";
                get2Command.Parameters.Add(get2Param1);
                var get2Param2 = get2Command.CreateParameter();
                get2Param2.DbType = DbType.Binary;
                get2Param2.ParameterName = "@key3";
                get2Command.Parameters.Add(get2Param2);

                get2Param1.Value = identityId.ToByteArray();
                get2Param2.Value = key3 ?? (object)DBNull.Value;
                {
                    using (var rdr = await get2Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        byte[] result0tmp;
                        var thelistresult = new List<byte[]>();
                        if (!await rdr.ReadAsync()) {
                            return thelistresult;
                        }
                    byte[] tmpbuf = new byte[1048576+1];
                    var guid = new byte[16];
                    while (true)
                    {

                        if (rdr[0] == DBNull.Value)
                            result0tmp = null;
                        else
                        {
                            tmpbuf = (byte[]) rdr[0];
                            if (tmpbuf.Length < 0)
                                throw new Exception("Too little data in data...");
                            result0tmp = new byte[tmpbuf.Length];
                            Buffer.BlockCopy(tmpbuf, 0, result0tmp, 0, (int) tmpbuf.Length);
                        }
                        thelistresult.Add(result0tmp);
                        if (!await rdr.ReadAsync())
                           break;
                    } // while
                    return thelistresult;
                    } // using
                } //
            } // using
        }

        protected KeyThreeValueRecord ReadRecordFromReader3(DbDataReader rdr,Guid identityId,byte[] key2,byte[] key3)
        {
            if (key2?.Length < 0) throw new OdinDatabaseValidationException($"Too short key2, was {key2.Length} (min 0)");
            if (key2?.Length > 256) throw new OdinDatabaseValidationException($"Too long key2, was {key2.Length} (max 256)");
            if (key3?.Length < 0) throw new OdinDatabaseValidationException($"Too short key3, was {key3.Length} (min 0)");
            if (key3?.Length > 256) throw new OdinDatabaseValidationException($"Too long key3, was {key3.Length} (max 256)");
            var result = new List<KeyThreeValueRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new KeyThreeValueRecord();
            item.identityId = identityId;
            item.key2 = key2;
            item.key3 = key3;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.key1 = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (byte[])(rdr[1]);
            if (item.key1?.Length < 16)
                throw new Exception("Too little data in key1...");
            item.data = (rdr[2] == DBNull.Value) ? null : (byte[])(rdr[2]);
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            return item;
       }

        protected virtual async Task<List<KeyThreeValueRecord>> GetByKeyTwoThreeAsync(Guid identityId,byte[] key2,byte[] key3)
        {
            if (key2?.Length < 0) throw new OdinDatabaseValidationException($"Too short key2, was {key2.Length} (min 0)");
            if (key2?.Length > 256) throw new OdinDatabaseValidationException($"Too long key2, was {key2.Length} (max 256)");
            if (key3?.Length < 0) throw new OdinDatabaseValidationException($"Too short key3, was {key3.Length} (min 0)");
            if (key3?.Length > 256) throw new OdinDatabaseValidationException($"Too long key3, was {key3.Length} (max 256)");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get3Command = cn.CreateCommand();
            {
                get3Command.CommandText = "SELECT rowId,key1,data FROM KeyThreeValue " +
                                             "WHERE identityId = @identityId AND key2 = @key2 AND key3 = @key3;"+
                                             ";";
                var get3Param1 = get3Command.CreateParameter();
                get3Param1.DbType = DbType.Binary;
                get3Param1.ParameterName = "@identityId";
                get3Command.Parameters.Add(get3Param1);
                var get3Param2 = get3Command.CreateParameter();
                get3Param2.DbType = DbType.Binary;
                get3Param2.ParameterName = "@key2";
                get3Command.Parameters.Add(get3Param2);
                var get3Param3 = get3Command.CreateParameter();
                get3Param3.DbType = DbType.Binary;
                get3Param3.ParameterName = "@key3";
                get3Command.Parameters.Add(get3Param3);

                get3Param1.Value = identityId.ToByteArray();
                get3Param2.Value = key2 ?? (object)DBNull.Value;
                get3Param3.Value = key3 ?? (object)DBNull.Value;
                {
                    using (var rdr = await get3Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            _cache.AddOrUpdate("TableKeyThreeValueCRUD", identityId.ToString()+key2.ToBase64()+key3.ToBase64(), null);
                            return new List<KeyThreeValueRecord>();
                        }
                        var result = new List<KeyThreeValueRecord>();
                        while (true)
                        {
                            result.Add(ReadRecordFromReader3(rdr,identityId,key2,key3));
                            if (!await rdr.ReadAsync())
                                break;
                        }
                        return result;
                    } // using
                } //
            } // using
        }

        protected virtual async Task<(List<KeyThreeValueRecord>, Int64? nextCursor)> PagingByRowIdAsync(int count, Int64? inCursor)
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
                getPaging0Command.CommandText = "SELECT rowId,identityId,key1,key2,key3,data FROM KeyThreeValue " +
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
                        var result = new List<KeyThreeValueRecord>();
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
