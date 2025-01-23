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

namespace Odin.Core.Storage.Database.Identity.Table
{
    public class KeyTwoValueRecord
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
        private byte[] _key1;
        public byte[] key1
        {
           get {
                   return _key1;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 16) throw new Exception("Too short");
                    if (value?.Length > 48) throw new Exception("Too long");
                  _key1 = value;
               }
        }
        internal byte[] key1NoLengthCheck
        {
           get {
                   return _key1;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 16) throw new Exception("Too short");
                  _key1 = value;
               }
        }
        private byte[] _key2;
        public byte[] key2
        {
           get {
                   return _key2;
               }
           set {
                    if (value?.Length < 0) throw new Exception("Too short");
                    if (value?.Length > 128) throw new Exception("Too long");
                  _key2 = value;
               }
        }
        internal byte[] key2NoLengthCheck
        {
           get {
                   return _key2;
               }
           set {
                    if (value?.Length < 0) throw new Exception("Too short");
                  _key2 = value;
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
        internal byte[] dataNoLengthCheck
        {
           get {
                   return _data;
               }
           set {
                    if (value?.Length < 0) throw new Exception("Too short");
                  _data = value;
               }
        }
    } // End of class KeyTwoValueRecord

    public abstract class TableKeyTwoValueCRUD
    {
        private readonly CacheHelper _cache;
        private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory;

        protected TableKeyTwoValueCRUD(CacheHelper cache, ScopedIdentityConnectionFactory scopedConnectionFactory)
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
                cmd.CommandText = "DROP TABLE IF EXISTS keyTwoValue;";
                await cmd.ExecuteNonQueryAsync();
            }
            var rowid = "";
            if (_scopedConnectionFactory.DatabaseType == DatabaseType.Postgres)
            {
                   rowid = ", rowid BIGSERIAL NOT NULL UNIQUE ";
            }
            cmd.CommandText =
                "CREATE TABLE IF NOT EXISTS keyTwoValue("
                   +"identityId BYTEA NOT NULL, "
                   +"key1 BYTEA NOT NULL, "
                   +"key2 BYTEA , "
                   +"data BYTEA  "
                   + rowid
                   +", PRIMARY KEY (identityId,key1)"
                   +");"
                   +"CREATE INDEX IF NOT EXISTS Idx0TableKeyTwoValueCRUD ON keyTwoValue(identityId,key2);"
                   ;
            await cmd.ExecuteNonQueryAsync();
        }

        protected virtual async Task<int> InsertAsync(KeyTwoValueRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO keyTwoValue (identityId,key1,key2,data) " +
                                             "VALUES (@identityId,@key1,@key2,@data)";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.ParameterName = "@key1";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.ParameterName = "@key2";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.ParameterName = "@data";
                insertCommand.Parameters.Add(insertParam4);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.key1;
                insertParam3.Value = item.key2 ?? (object)DBNull.Value;
                insertParam4.Value = item.data ?? (object)DBNull.Value;
                var count = await insertCommand.ExecuteNonQueryAsync();
                if (count > 0)
                {
                    _cache.AddOrUpdate("TableKeyTwoValueCRUD", item.identityId.ToString()+item.key1.ToBase64(), item);
                }
                return count;
            }
        }

        protected virtual async Task<bool> TryInsertAsync(KeyTwoValueRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO keyTwoValue (identityId,key1,key2,data) " +
                                             "VALUES (@identityId,@key1,@key2,@data) " +
                                             "ON CONFLICT DO NOTHING";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.ParameterName = "@key1";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.ParameterName = "@key2";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.ParameterName = "@data";
                insertCommand.Parameters.Add(insertParam4);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.key1;
                insertParam3.Value = item.key2 ?? (object)DBNull.Value;
                insertParam4.Value = item.data ?? (object)DBNull.Value;
                var count = await insertCommand.ExecuteNonQueryAsync();
                if (count > 0)
                {
                   _cache.AddOrUpdate("TableKeyTwoValueCRUD", item.identityId.ToString()+item.key1.ToBase64(), item);
                }
                return count > 0;
            }
        }

        protected virtual async Task<int> UpsertAsync(KeyTwoValueRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                upsertCommand.CommandText = "INSERT INTO keyTwoValue (identityId,key1,key2,data) " +
                                             "VALUES (@identityId,@key1,@key2,@data)"+
                                             "ON CONFLICT (identityId,key1) DO UPDATE "+
                                             "SET key2 = @key2,data = @data "+
                                             ";";
                var upsertParam1 = upsertCommand.CreateParameter();
                upsertParam1.ParameterName = "@identityId";
                upsertCommand.Parameters.Add(upsertParam1);
                var upsertParam2 = upsertCommand.CreateParameter();
                upsertParam2.ParameterName = "@key1";
                upsertCommand.Parameters.Add(upsertParam2);
                var upsertParam3 = upsertCommand.CreateParameter();
                upsertParam3.ParameterName = "@key2";
                upsertCommand.Parameters.Add(upsertParam3);
                var upsertParam4 = upsertCommand.CreateParameter();
                upsertParam4.ParameterName = "@data";
                upsertCommand.Parameters.Add(upsertParam4);
                upsertParam1.Value = item.identityId.ToByteArray();
                upsertParam2.Value = item.key1;
                upsertParam3.Value = item.key2 ?? (object)DBNull.Value;
                upsertParam4.Value = item.data ?? (object)DBNull.Value;
                var count = await upsertCommand.ExecuteNonQueryAsync();
                if (count > 0)
                    _cache.AddOrUpdate("TableKeyTwoValueCRUD", item.identityId.ToString()+item.key1.ToBase64(), item);
                return count;
            }
        }
        protected virtual async Task<int> UpdateAsync(KeyTwoValueRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                updateCommand.CommandText = "UPDATE keyTwoValue " +
                                             "SET key2 = @key2,data = @data "+
                                             "WHERE (identityId = @identityId AND key1 = @key1)";
                var updateParam1 = updateCommand.CreateParameter();
                updateParam1.ParameterName = "@identityId";
                updateCommand.Parameters.Add(updateParam1);
                var updateParam2 = updateCommand.CreateParameter();
                updateParam2.ParameterName = "@key1";
                updateCommand.Parameters.Add(updateParam2);
                var updateParam3 = updateCommand.CreateParameter();
                updateParam3.ParameterName = "@key2";
                updateCommand.Parameters.Add(updateParam3);
                var updateParam4 = updateCommand.CreateParameter();
                updateParam4.ParameterName = "@data";
                updateCommand.Parameters.Add(updateParam4);
                updateParam1.Value = item.identityId.ToByteArray();
                updateParam2.Value = item.key1;
                updateParam3.Value = item.key2 ?? (object)DBNull.Value;
                updateParam4.Value = item.data ?? (object)DBNull.Value;
                var count = await updateCommand.ExecuteNonQueryAsync();
                if (count > 0)
                {
                    _cache.AddOrUpdate("TableKeyTwoValueCRUD", item.identityId.ToString()+item.key1.ToBase64(), item);
                }
                return count;
            }
        }

        protected virtual async Task<int> GetCountAsync()
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getCountCommand = cn.CreateCommand();
            {
                 // TODO: this is SQLite specific
                getCountCommand.CommandText = "SELECT COUNT(*) FROM keyTwoValue;";
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
            sl.Add("identityId");
            sl.Add("key1");
            sl.Add("key2");
            sl.Add("data");
            return sl;
        }

        // SELECT identityId,key1,key2,data
        protected KeyTwoValueRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<KeyTwoValueRecord>();
            byte[] tmpbuf = new byte[1048576+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new KeyTwoValueRecord();
            item.identityId = rdr.IsDBNull(0) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[0]);
            item.key1NoLengthCheck = rdr.IsDBNull(1) ? throw new Exception("item is NULL, but set as NOT NULL") : (byte[])(rdr[1]);
            if (item.key1?.Length > 48)
                throw new Exception("Too much data in key1...");
            if (item.key1?.Length < 16)
                throw new Exception("Too little data in key1...");
            item.key2NoLengthCheck = rdr.IsDBNull(2) ? null : (byte[])(rdr[2]);
            if (item.key2?.Length > 128)
                throw new Exception("Too much data in key2...");
            if (item.key2?.Length < 0)
                throw new Exception("Too little data in key2...");
            item.dataNoLengthCheck = rdr.IsDBNull(3) ? null : (byte[])(rdr[3]);
            if (item.data?.Length > 1048576)
                throw new Exception("Too much data in data...");
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            return item;
       }

        protected virtual async Task<int> DeleteAsync(Guid identityId,byte[] key1)
        {
            if (key1 == null) throw new Exception("Cannot be null");
            if (key1?.Length < 16) throw new Exception("Too short");
            if (key1?.Length > 48) throw new Exception("Too long");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM keyTwoValue " +
                                             "WHERE identityId = @identityId AND key1 = @key1";
                var delete0Param1 = delete0Command.CreateParameter();
                delete0Param1.ParameterName = "@identityId";
                delete0Command.Parameters.Add(delete0Param1);
                var delete0Param2 = delete0Command.CreateParameter();
                delete0Param2.ParameterName = "@key1";
                delete0Command.Parameters.Add(delete0Param2);

                delete0Param1.Value = identityId.ToByteArray();
                delete0Param2.Value = key1;
                var count = await delete0Command.ExecuteNonQueryAsync();
                if (count > 0)
                    _cache.Remove("TableKeyTwoValueCRUD", identityId.ToString()+key1.ToBase64());
                return count;
            }
        }

        protected KeyTwoValueRecord ReadRecordFromReader0(DbDataReader rdr, Guid identityId,byte[] key2)
        {
            if (key2?.Length < 0) throw new Exception("Too short");
            if (key2?.Length > 128) throw new Exception("Too long");
            var result = new List<KeyTwoValueRecord>();
            byte[] tmpbuf = new byte[1048576+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new KeyTwoValueRecord();
            item.identityId = identityId;
            item.key2 = key2;
            item.key1NoLengthCheck = rdr.IsDBNull(0) ? throw new Exception("item is NULL, but set as NOT NULL") : (byte[])(rdr[0]);
            if (item.key1?.Length > 48)
                throw new Exception("Too much data in key1...");
            if (item.key1?.Length < 16)
                throw new Exception("Too little data in key1...");
            item.dataNoLengthCheck = rdr.IsDBNull(1) ? null : (byte[])(rdr[1]);
            if (item.data?.Length > 1048576)
                throw new Exception("Too much data in data...");
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            return item;
       }

        protected virtual async Task<List<KeyTwoValueRecord>> GetByKeyTwoAsync(Guid identityId,byte[] key2)
        {
            if (key2?.Length < 0) throw new Exception("Too short");
            if (key2?.Length > 128) throw new Exception("Too long");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT key1,data FROM keyTwoValue " +
                                             "WHERE identityId = @identityId AND key2 = @key2;";
                var get0Param1 = get0Command.CreateParameter();
                get0Param1.ParameterName = "@identityId";
                get0Command.Parameters.Add(get0Param1);
                var get0Param2 = get0Command.CreateParameter();
                get0Param2.ParameterName = "@key2";
                get0Command.Parameters.Add(get0Param2);

                get0Param1.Value = identityId.ToByteArray();
                get0Param2.Value = key2 ?? (object)DBNull.Value;
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            _cache.AddOrUpdate("TableKeyTwoValueCRUD", identityId.ToString()+key2.ToBase64(), null);
                            return new List<KeyTwoValueRecord>();
                        }
                        var result = new List<KeyTwoValueRecord>();
                        while (true)
                        {
                            result.Add(ReadRecordFromReader0(rdr, identityId,key2));
                            if (!await rdr.ReadAsync())
                                break;
                        }
                        return result;
                    } // using
                } //
            } // using
        }

        protected KeyTwoValueRecord ReadRecordFromReader1(DbDataReader rdr, Guid identityId,byte[] key1)
        {
            if (key1 == null) throw new Exception("Cannot be null");
            if (key1?.Length < 16) throw new Exception("Too short");
            if (key1?.Length > 48) throw new Exception("Too long");
            var result = new List<KeyTwoValueRecord>();
            byte[] tmpbuf = new byte[1048576+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new KeyTwoValueRecord();
            item.identityId = identityId;
            item.key1 = key1;
            item.key2NoLengthCheck = rdr.IsDBNull(0) ? null : (byte[])(rdr[0]);
            if (item.key2?.Length > 128)
                throw new Exception("Too much data in key2...");
            if (item.key2?.Length < 0)
                throw new Exception("Too little data in key2...");
            item.dataNoLengthCheck = rdr.IsDBNull(1) ? null : (byte[])(rdr[1]);
            if (item.data?.Length > 1048576)
                throw new Exception("Too much data in data...");
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            return item;
       }

        protected virtual async Task<KeyTwoValueRecord> GetAsync(Guid identityId,byte[] key1)
        {
            if (key1 == null) throw new Exception("Cannot be null");
            if (key1?.Length < 16) throw new Exception("Too short");
            if (key1?.Length > 48) throw new Exception("Too long");
            var (hit, cacheObject) = _cache.Get("TableKeyTwoValueCRUD", identityId.ToString()+key1.ToBase64());
            if (hit)
                return (KeyTwoValueRecord)cacheObject;
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get1Command = cn.CreateCommand();
            {
                get1Command.CommandText = "SELECT key2,data FROM keyTwoValue " +
                                             "WHERE identityId = @identityId AND key1 = @key1 LIMIT 1;";
                var get1Param1 = get1Command.CreateParameter();
                get1Param1.ParameterName = "@identityId";
                get1Command.Parameters.Add(get1Param1);
                var get1Param2 = get1Command.CreateParameter();
                get1Param2.ParameterName = "@key1";
                get1Command.Parameters.Add(get1Param2);

                get1Param1.Value = identityId.ToByteArray();
                get1Param2.Value = key1;
                {
                    using (var rdr = await get1Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            _cache.AddOrUpdate("TableKeyTwoValueCRUD", identityId.ToString()+key1.ToBase64(), null);
                            return null;
                        }
                        var r = ReadRecordFromReader1(rdr, identityId,key1);
                        _cache.AddOrUpdate("TableKeyTwoValueCRUD", identityId.ToString()+key1.ToBase64(), r);
                        return r;
                    } // using
                } //
            } // using
        }

    }
}
