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
using Odin.Core.Storage.Exceptions;
using Odin.Core.Storage.SQLite;

// THIS FILE IS AUTO GENERATED - DO NOT EDIT

[assembly: InternalsVisibleTo("DatabaseCommitTest")]
[assembly: InternalsVisibleTo("DatabaseConnectionTests")]

namespace Odin.Core.Storage.Database.Identity.Table
{
    public record KeyValueRecord
    {
        private Int64 _rowId;
        public Int64 rowId
        {
           get {
                   return _rowId;
               }
           set {
                  _rowId = value;
               }
        }
        private Guid _identityId;
        public Guid identityId
        {
           get {
                   return _identityId;
               }
           set {
                  _identityId = value;
               }
        }
        private byte[] _key;
        public byte[] key
        {
           get {
                   return _key;
               }
           set {
                    if (value == null) throw new OdinDatabaseValidationException("Cannot be null key");
                    if (value?.Length < 16) throw new OdinDatabaseValidationException($"Too short key, was {value.Length} (min 16)");
                    if (value?.Length > 48) throw new OdinDatabaseValidationException($"Too long key, was {value.Length} (max 48)");
                  _key = value;
               }
        }
        internal byte[] keyNoLengthCheck
        {
           get {
                   return _key;
               }
           set {
                    if (value == null) throw new OdinDatabaseValidationException("Cannot be null key");
                    if (value?.Length < 16) throw new OdinDatabaseValidationException($"Too short key, was {value.Length} (min 16)");
                  _key = value;
               }
        }
        private byte[] _data;
        public byte[] data
        {
           get {
                   return _data;
               }
           set {
                    if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short data, was {value.Length} (min 0)");
                    if (value?.Length > 1048576) throw new OdinDatabaseValidationException($"Too long data, was {value.Length} (max 1048576)");
                  _data = value;
               }
        }
        internal byte[] dataNoLengthCheck
        {
           get {
                   return _data;
               }
           set {
                    if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short data, was {value.Length} (min 0)");
                  _data = value;
               }
        }
    } // End of record KeyValueRecord

    public abstract class TableKeyValueCRUD
    {
        private readonly CacheHelper _cache;
        private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory;

        protected TableKeyValueCRUD(CacheHelper cache, ScopedIdentityConnectionFactory scopedConnectionFactory)
        {
            _cache = cache;
            _scopedConnectionFactory = scopedConnectionFactory;
        }


        public virtual async Task EnsureTableExistsAsync(bool dropExisting = false)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var cmd = cn.CreateCommand();
            if (dropExisting)
            {
                cmd.CommandText = "DROP TABLE IF EXISTS KeyValue;";
                await cmd.ExecuteNonQueryAsync();
            }
            var rowid = "";
            if (_scopedConnectionFactory.DatabaseType == DatabaseType.Postgres)
               rowid = "rowid BIGSERIAL PRIMARY KEY,";
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            cmd.CommandText =
                "CREATE TABLE IF NOT EXISTS KeyValue("
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"key BYTEA NOT NULL, "
                   +"data BYTEA  "
                   +", UNIQUE(identityId,key)"
                   +$"){wori};"
                   ;
            await cmd.ExecuteNonQueryAsync();
        }

        protected virtual async Task<int> InsertAsync(KeyValueRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO KeyValue (identityId,key,data) " +
                                           $"VALUES (@identityId,@key,@data)"+
                                            "RETURNING -1,-1,rowId;";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.DbType = DbType.Binary;
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.DbType = DbType.Binary;
                insertParam2.ParameterName = "@key";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.DbType = DbType.Binary;
                insertParam3.ParameterName = "@data";
                insertCommand.Parameters.Add(insertParam3);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.key;
                insertParam3.Value = item.data ?? (object)DBNull.Value;
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                    _cache.AddOrUpdate("TableKeyValueCRUD", item.identityId.ToString()+item.key.ToBase64(), item);
                    return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<bool> TryInsertAsync(KeyValueRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO KeyValue (identityId,key,data) " +
                                            $"VALUES (@identityId,@key,@data) " +
                                            "ON CONFLICT DO NOTHING "+
                                            "RETURNING -1,-1,rowId;";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.ParameterName = "@key";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.ParameterName = "@data";
                insertCommand.Parameters.Add(insertParam3);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.key;
                insertParam3.Value = item.data ?? (object)DBNull.Value;
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                   _cache.AddOrUpdate("TableKeyValueCRUD", item.identityId.ToString()+item.key.ToBase64(), item);
                    return true;
                }
                return false;
            }
        }

        protected virtual async Task<int> UpsertAsync(KeyValueRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                upsertCommand.CommandText = "INSERT INTO KeyValue (identityId,key,data) " +
                                            $"VALUES (@identityId,@key,@data)"+
                                            "ON CONFLICT (identityId,key) DO UPDATE "+
                                            $"SET data = @data "+
                                            "RETURNING -1,-1,rowId;";
                var upsertParam1 = upsertCommand.CreateParameter();
                upsertParam1.ParameterName = "@identityId";
                upsertCommand.Parameters.Add(upsertParam1);
                var upsertParam2 = upsertCommand.CreateParameter();
                upsertParam2.ParameterName = "@key";
                upsertCommand.Parameters.Add(upsertParam2);
                var upsertParam3 = upsertCommand.CreateParameter();
                upsertParam3.ParameterName = "@data";
                upsertCommand.Parameters.Add(upsertParam3);
                upsertParam1.Value = item.identityId.ToByteArray();
                upsertParam2.Value = item.key;
                upsertParam3.Value = item.data ?? (object)DBNull.Value;
                await using var rdr = await upsertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                   _cache.AddOrUpdate("TableKeyValueCRUD", item.identityId.ToString()+item.key.ToBase64(), item);
                    return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<int> UpdateAsync(KeyValueRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                updateCommand.CommandText = "UPDATE KeyValue " +
                                            $"SET data = @data "+
                                            "WHERE (identityId = @identityId AND key = @key) "+
                                            "RETURNING -1,-1,rowId;";
                var updateParam1 = updateCommand.CreateParameter();
                updateParam1.ParameterName = "@identityId";
                updateCommand.Parameters.Add(updateParam1);
                var updateParam2 = updateCommand.CreateParameter();
                updateParam2.ParameterName = "@key";
                updateCommand.Parameters.Add(updateParam2);
                var updateParam3 = updateCommand.CreateParameter();
                updateParam3.ParameterName = "@data";
                updateCommand.Parameters.Add(updateParam3);
                updateParam1.Value = item.identityId.ToByteArray();
                updateParam2.Value = item.key;
                updateParam3.Value = item.data ?? (object)DBNull.Value;
                await using var rdr = await updateCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                   _cache.AddOrUpdate("TableKeyValueCRUD", item.identityId.ToString()+item.key.ToBase64(), item);
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
                getCountCommand.CommandText = "SELECT COUNT(*) FROM KeyValue;";
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
            sl.Add("key");
            sl.Add("data");
            return sl;
        }

        // SELECT rowId,identityId,key,data
        protected KeyValueRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<KeyValueRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new KeyValueRecord();
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.identityId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.keyNoLengthCheck = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (byte[])(rdr[2]);
            if (item.key?.Length < 16)
                throw new Exception("Too little data in key...");
            item.dataNoLengthCheck = (rdr[3] == DBNull.Value) ? null : (byte[])(rdr[3]);
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            return item;
       }

        protected virtual async Task<int> DeleteAsync(Guid identityId,byte[] key)
        {
            if (key == null) throw new OdinDatabaseValidationException("Cannot be null key");
            if (key?.Length < 16) throw new OdinDatabaseValidationException($"Too short key, was {key.Length} (min 16)");
            if (key?.Length > 48) throw new OdinDatabaseValidationException($"Too long key, was {key.Length} (max 48)");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM KeyValue " +
                                             "WHERE identityId = @identityId AND key = @key";
                var delete0Param1 = delete0Command.CreateParameter();
                delete0Param1.ParameterName = "@identityId";
                delete0Command.Parameters.Add(delete0Param1);
                var delete0Param2 = delete0Command.CreateParameter();
                delete0Param2.ParameterName = "@key";
                delete0Command.Parameters.Add(delete0Param2);

                delete0Param1.Value = identityId.ToByteArray();
                delete0Param2.Value = key;
                var count = await delete0Command.ExecuteNonQueryAsync();
                if (count > 0)
                    _cache.Remove("TableKeyValueCRUD", identityId.ToString()+key.ToBase64());
                return count;
            }
        }

        protected KeyValueRecord ReadRecordFromReader0(DbDataReader rdr,Guid identityId,byte[] key)
        {
            if (key == null) throw new OdinDatabaseValidationException("Cannot be null key");
            if (key?.Length < 16) throw new OdinDatabaseValidationException($"Too short key, was {key.Length} (min 16)");
            if (key?.Length > 48) throw new OdinDatabaseValidationException($"Too long key, was {key.Length} (max 48)");
            var result = new List<KeyValueRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new KeyValueRecord();
            item.identityId = identityId;
            item.key = key;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.dataNoLengthCheck = (rdr[1] == DBNull.Value) ? null : (byte[])(rdr[1]);
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            return item;
       }

        protected virtual async Task<KeyValueRecord> GetAsync(Guid identityId,byte[] key)
        {
            if (key == null) throw new OdinDatabaseValidationException("Cannot be null key");
            if (key?.Length < 16) throw new OdinDatabaseValidationException($"Too short key, was {key.Length} (min 16)");
            if (key?.Length > 48) throw new OdinDatabaseValidationException($"Too long key, was {key.Length} (max 48)");
            var (hit, cacheObject) = _cache.Get("TableKeyValueCRUD", identityId.ToString()+key.ToBase64());
            if (hit)
                return (KeyValueRecord)cacheObject;
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT rowId,data FROM KeyValue " +
                                             "WHERE identityId = @identityId AND key = @key LIMIT 1;"+
                                             ";";
                var get0Param1 = get0Command.CreateParameter();
                get0Param1.ParameterName = "@identityId";
                get0Command.Parameters.Add(get0Param1);
                var get0Param2 = get0Command.CreateParameter();
                get0Param2.ParameterName = "@key";
                get0Command.Parameters.Add(get0Param2);

                get0Param1.Value = identityId.ToByteArray();
                get0Param2.Value = key;
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            _cache.AddOrUpdate("TableKeyValueCRUD", identityId.ToString()+key.ToBase64(), null);
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr,identityId,key);
                        _cache.AddOrUpdate("TableKeyValueCRUD", identityId.ToString()+key.ToBase64(), r);
                        return r;
                    } // using
                } //
            } // using
        }

        protected virtual async Task<(List<KeyValueRecord>, Int64? nextCursor)> PagingByRowIdAsync(int count, Int64? inCursor)
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
                getPaging0Command.CommandText = "SELECT rowId,identityId,key,data FROM KeyValue " +
                                            "WHERE rowId > @rowId  ORDER BY rowId ASC  LIMIT @count;";
                var getPaging0Param1 = getPaging0Command.CreateParameter();
                getPaging0Param1.ParameterName = "@rowId";
                getPaging0Command.Parameters.Add(getPaging0Param1);
                var getPaging0Param2 = getPaging0Command.CreateParameter();
                getPaging0Param2.ParameterName = "@count";
                getPaging0Command.Parameters.Add(getPaging0Param2);

                getPaging0Param1.Value = inCursor;
                getPaging0Param2.Value = count+1;

                {
                    await using (var rdr = await getPaging0Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        var result = new List<KeyValueRecord>();
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
