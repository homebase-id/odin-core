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
using Odin.Core.Util;

// THIS FILE IS AUTO GENERATED - DO NOT EDIT

[assembly: InternalsVisibleTo("DatabaseCommitTest")]
[assembly: InternalsVisibleTo("DatabaseConnectionTests")]

namespace Odin.Core.Storage.Database.Identity.Table
{
    public class KeyValueRecord
    {
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
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 16) throw new Exception("Too short");
                    if (value?.Length > 48) throw new Exception("Too long");
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
                    if (value?.Length < 0) throw new Exception("Too short");
                    if (value?.Length > 1048576) throw new Exception("Too long");
                  _data = value;
               }
        }
    } // End of class KeyValueRecord

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
            {
                if (dropExisting)
                {
                   cmd.CommandText = "DROP TABLE IF EXISTS keyValue;";
                   await cmd.ExecuteNonQueryAsync();
                }
                cmd.CommandText =
                "CREATE TABLE IF NOT EXISTS keyValue("
                 +"identityId BLOB NOT NULL, "
                 +"key BLOB NOT NULL UNIQUE, "
                 +"data BLOB  "
                 +", PRIMARY KEY (identityId,key)"
                 +");"
                 ;
                 await cmd.ExecuteNonQueryAsync();
            }
        }

        public virtual async Task<int> InsertAsync(KeyValueRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO keyValue (identityId,key,data) " +
                                             "VALUES (@identityId,@key,@data)";
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
                var count = await insertCommand.ExecuteNonQueryAsync();
                if (count > 0)
                {
                    _cache.AddOrUpdate("TableKeyValueCRUD", item.identityId.ToString()+item.key.ToBase64(), item);
                }
                return count;
            }
        }

        public virtual async Task<int> TryInsertAsync(KeyValueRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT OR IGNORE INTO keyValue (identityId,key,data) " +
                                             "VALUES (@identityId,@key,@data)";
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
                var count = await insertCommand.ExecuteNonQueryAsync();
                if (count > 0)
                {
                   _cache.AddOrUpdate("TableKeyValueCRUD", item.identityId.ToString()+item.key.ToBase64(), item);
                }
                return count;
            }
        }

        public virtual async Task<int> UpsertAsync(KeyValueRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                upsertCommand.CommandText = "INSERT INTO keyValue (identityId,key,data) " +
                                             "VALUES (@identityId,@key,@data)"+
                                             "ON CONFLICT (identityId,key) DO UPDATE "+
                                             "SET data = @data "+
                                             ";";
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
                var count = await upsertCommand.ExecuteNonQueryAsync();
                if (count > 0)
                    _cache.AddOrUpdate("TableKeyValueCRUD", item.identityId.ToString()+item.key.ToBase64(), item);
                return count;
            }
        }
        public virtual async Task<int> UpdateAsync(KeyValueRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                updateCommand.CommandText = "UPDATE keyValue " +
                                             "SET data = @data "+
                                             "WHERE (identityId = @identityId AND key = @key)";
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
                var count = await updateCommand.ExecuteNonQueryAsync();
                if (count > 0)
                {
                    _cache.AddOrUpdate("TableKeyValueCRUD", item.identityId.ToString()+item.key.ToBase64(), item);
                }
                return count;
            }
        }

        public virtual async Task<int> GetCountDirtyAsync()
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getCountCommand = cn.CreateCommand();
            {
                 // TODO: this is SQLite specific
                getCountCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM keyValue; PRAGMA read_uncommitted = 0;";
                var count = await getCountCommand.ExecuteScalarAsync();
                if (count == null || count == DBNull.Value || !(count is int || count is long))
                    return -1;
                else
                    return Convert.ToInt32(count);
            }
        }

        public List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("identityId");
            sl.Add("key");
            sl.Add("data");
            return sl;
        }

        // SELECT identityId,key,data
        public KeyValueRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<KeyValueRecord>();
            byte[] tmpbuf = new byte[1048576+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new KeyValueRecord();
            item.identityId = rdr.IsDBNull(0) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[0]);
            item.key = rdr.IsDBNull(1) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : (byte[])(rdr[1]);
            if (item.key?.Length > 48)
                throw new Exception("Too much data in key...");
            if (item.key?.Length < 16)
                throw new Exception("Too little data in key...");
            item.data = rdr.IsDBNull(2) ? 
                null : (byte[])(rdr[2]);
            if (item.data?.Length > 1048576)
                throw new Exception("Too much data in data...");
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            return item;
       }

        public virtual async Task<int> DeleteAsync(Guid identityId,byte[] key)
        {
            if (key == null) throw new Exception("Cannot be null");
            if (key?.Length < 16) throw new Exception("Too short");
            if (key?.Length > 48) throw new Exception("Too long");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM keyValue " +
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

        public KeyValueRecord ReadRecordFromReader0(DbDataReader rdr, Guid identityId,byte[] key)
        {
            if (key == null) throw new Exception("Cannot be null");
            if (key?.Length < 16) throw new Exception("Too short");
            if (key?.Length > 48) throw new Exception("Too long");
            var result = new List<KeyValueRecord>();
            byte[] tmpbuf = new byte[1048576+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new KeyValueRecord();
            item.identityId = identityId;
            item.key = key;

            item.data = rdr.IsDBNull(0) ? 
                null : (byte[])(rdr[0]);
            if (item.data?.Length > 1048576)
                throw new Exception("Too much data in data...");
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            return item;
       }

        public virtual async Task<KeyValueRecord> GetAsync(Guid identityId,byte[] key)
        {
            if (key == null) throw new Exception("Cannot be null");
            if (key?.Length < 16) throw new Exception("Too short");
            if (key?.Length > 48) throw new Exception("Too long");
            var (hit, cacheObject) = _cache.Get("TableKeyValueCRUD", identityId.ToString()+key.ToBase64());
            if (hit)
                return (KeyValueRecord)cacheObject;
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT data FROM keyValue " +
                                             "WHERE identityId = @identityId AND key = @key LIMIT 1;";
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
                        var r = ReadRecordFromReader0(rdr, identityId,key);
                        _cache.AddOrUpdate("TableKeyValueCRUD", identityId.ToString()+key.ToBase64(), r);
                        return r;
                    } // using
                } //
            } // using
        }

    }
}
