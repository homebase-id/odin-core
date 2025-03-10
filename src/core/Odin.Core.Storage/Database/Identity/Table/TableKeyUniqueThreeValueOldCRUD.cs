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
    public class KeyUniqueThreeValueOldRecord
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
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 0) throw new Exception("Too short");
                    if (value?.Length > 256) throw new Exception("Too long");
                  _key2 = value;
               }
        }
        internal byte[] key2NoLengthCheck
        {
           get {
                   return _key2;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 0) throw new Exception("Too short");
                  _key2 = value;
               }
        }
        private byte[] _key3;
        public byte[] key3
        {
           get {
                   return _key3;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 0) throw new Exception("Too short");
                    if (value?.Length > 256) throw new Exception("Too long");
                  _key3 = value;
               }
        }
        internal byte[] key3NoLengthCheck
        {
           get {
                   return _key3;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 0) throw new Exception("Too short");
                  _key3 = value;
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
    } // End of class KeyUniqueThreeValueOldRecord

    public class TableKeyUniqueThreeValueOldCRUD
    {
        private readonly CacheHelper _cache;
        private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory;

        public TableKeyUniqueThreeValueOldCRUD(CacheHelper cache, ScopedIdentityConnectionFactory scopedConnectionFactory)
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
                cmd.CommandText = "DROP TABLE IF EXISTS KeyUniqueThreeValueOld;";
                await cmd.ExecuteNonQueryAsync();
            }
            var rowid = "";
            if (_scopedConnectionFactory.DatabaseType == DatabaseType.Postgres)
            {
                   rowid = ", rowid BIGSERIAL NOT NULL UNIQUE ";
            }
            cmd.CommandText =
                "CREATE TABLE IF NOT EXISTS KeyUniqueThreeValueOld("
                   +"identityId BYTEA NOT NULL, "
                   +"key1 BYTEA NOT NULL, "
                   +"key2 BYTEA NOT NULL, "
                   +"key3 BYTEA NOT NULL, "
                   +"data BYTEA  "
                   + rowid
                   +", PRIMARY KEY (identityId,key1)"
                   +", UNIQUE(identityId,key2,key3)"
                   +");"
                   +"CREATE INDEX IF NOT EXISTS Idx0KeyUniqueThreeValueOld ON KeyUniqueThreeValueOld(identityId,key2);"
                   +"CREATE INDEX IF NOT EXISTS Idx1KeyUniqueThreeValueOld ON KeyUniqueThreeValueOld(key3);"
                   ;
            await cmd.ExecuteNonQueryAsync();
        }

        public virtual async Task<int> InsertAsync(KeyUniqueThreeValueOldRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO KeyUniqueThreeValueOld (identityId,key1,key2,key3,data) " +
                                             "VALUES (@identityId,@key1,@key2,@key3,@data)";
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
                insertParam4.ParameterName = "@key3";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.ParameterName = "@data";
                insertCommand.Parameters.Add(insertParam5);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.key1;
                insertParam3.Value = item.key2;
                insertParam4.Value = item.key3;
                insertParam5.Value = item.data ?? (object)DBNull.Value;
                var count = await insertCommand.ExecuteNonQueryAsync();
                if (count > 0)
                {
                    _cache.AddOrUpdate("TableKeyUniqueThreeValueOldCRUD", item.identityId.ToString()+item.key1.ToBase64(), item);
                }
                return count;
            }
        }

        public virtual async Task<bool> TryInsertAsync(KeyUniqueThreeValueOldRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO KeyUniqueThreeValueOld (identityId,key1,key2,key3,data) " +
                                             "VALUES (@identityId,@key1,@key2,@key3,@data) " +
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
                insertParam4.ParameterName = "@key3";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.ParameterName = "@data";
                insertCommand.Parameters.Add(insertParam5);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.key1;
                insertParam3.Value = item.key2;
                insertParam4.Value = item.key3;
                insertParam5.Value = item.data ?? (object)DBNull.Value;
                var count = await insertCommand.ExecuteNonQueryAsync();
                if (count > 0)
                {
                   _cache.AddOrUpdate("TableKeyUniqueThreeValueOldCRUD", item.identityId.ToString()+item.key1.ToBase64(), item);
                }
                return count > 0;
            }
        }

        public virtual async Task<int> UpsertAsync(KeyUniqueThreeValueOldRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                upsertCommand.CommandText = "INSERT INTO KeyUniqueThreeValueOld (identityId,key1,key2,key3,data) " +
                                             "VALUES (@identityId,@key1,@key2,@key3,@data)"+
                                             "ON CONFLICT (identityId,key1) DO UPDATE "+
                                             "SET key2 = @key2,key3 = @key3,data = @data "+
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
                upsertParam4.ParameterName = "@key3";
                upsertCommand.Parameters.Add(upsertParam4);
                var upsertParam5 = upsertCommand.CreateParameter();
                upsertParam5.ParameterName = "@data";
                upsertCommand.Parameters.Add(upsertParam5);
                upsertParam1.Value = item.identityId.ToByteArray();
                upsertParam2.Value = item.key1;
                upsertParam3.Value = item.key2;
                upsertParam4.Value = item.key3;
                upsertParam5.Value = item.data ?? (object)DBNull.Value;
                var count = await upsertCommand.ExecuteNonQueryAsync();
                if (count > 0)
                    _cache.AddOrUpdate("TableKeyUniqueThreeValueOldCRUD", item.identityId.ToString()+item.key1.ToBase64(), item);
                return count;
            }
        }
        public virtual async Task<int> UpdateAsync(KeyUniqueThreeValueOldRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                updateCommand.CommandText = "UPDATE KeyUniqueThreeValueOld " +
                                             "SET key2 = @key2,key3 = @key3,data = @data "+
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
                updateParam4.ParameterName = "@key3";
                updateCommand.Parameters.Add(updateParam4);
                var updateParam5 = updateCommand.CreateParameter();
                updateParam5.ParameterName = "@data";
                updateCommand.Parameters.Add(updateParam5);
                updateParam1.Value = item.identityId.ToByteArray();
                updateParam2.Value = item.key1;
                updateParam3.Value = item.key2;
                updateParam4.Value = item.key3;
                updateParam5.Value = item.data ?? (object)DBNull.Value;
                var count = await updateCommand.ExecuteNonQueryAsync();
                if (count > 0)
                {
                    _cache.AddOrUpdate("TableKeyUniqueThreeValueOldCRUD", item.identityId.ToString()+item.key1.ToBase64(), item);
                }
                return count;
            }
        }

        public virtual async Task<int> RenameAsync()
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getCountCommand = cn.CreateCommand();
            {
                getCountCommand.CommandText = "ALTER TABLE keyUniqueThreeValue RENAME TO KeyUniqueThreeValueOld;";
                var count = await getCountCommand.ExecuteScalarAsync();
                if (count == null || count == DBNull.Value || !(count is int || count is long))
                    return -1;
                else
                    return Convert.ToInt32(count);
            }
        }

        public virtual async Task<int> GetCountAsync()
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getCountCommand = cn.CreateCommand();
            {
                 // TODO: this is SQLite specific
                getCountCommand.CommandText = "SELECT COUNT(*) FROM KeyUniqueThreeValueOld;";
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
            sl.Add("key3");
            sl.Add("data");
            return sl;
        }

        // SELECT identityId,key1,key2,key3,data
        public KeyUniqueThreeValueOldRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<KeyUniqueThreeValueOldRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new KeyUniqueThreeValueOldRecord();
            item.identityId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[0]);
            item.key1NoLengthCheck = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (byte[])(rdr[1]);
            if (item.key1?.Length < 16)
                throw new Exception("Too little data in key1...");
            item.key2NoLengthCheck = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (byte[])(rdr[2]);
            if (item.key2?.Length < 0)
                throw new Exception("Too little data in key2...");
            item.key3NoLengthCheck = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (byte[])(rdr[3]);
            if (item.key3?.Length < 0)
                throw new Exception("Too little data in key3...");
            item.dataNoLengthCheck = (rdr[4] == DBNull.Value) ? null : (byte[])(rdr[4]);
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            return item;
       }

        public virtual async Task<int> DeleteAsync(Guid identityId,byte[] key1)
        {
            if (key1 == null) throw new Exception("Cannot be null");
            if (key1?.Length < 16) throw new Exception("Too short");
            if (key1?.Length > 48) throw new Exception("Too long");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM KeyUniqueThreeValueOld " +
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
                    _cache.Remove("TableKeyUniqueThreeValueOldCRUD", identityId.ToString()+key1.ToBase64());
                return count;
            }
        }

        public virtual async Task<List<byte[]>> GetByKeyTwoAsync(Guid identityId,byte[] key2)
        {
            if (key2 == null) throw new Exception("Cannot be null");
            if (key2?.Length < 0) throw new Exception("Too short");
            if (key2?.Length > 256) throw new Exception("Too long");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT data FROM KeyUniqueThreeValueOld " +
                                             "WHERE identityId = @identityId AND key2 = @key2;"+
                                             ";";
                var get0Param1 = get0Command.CreateParameter();
                get0Param1.ParameterName = "@identityId";
                get0Command.Parameters.Add(get0Param1);
                var get0Param2 = get0Command.CreateParameter();
                get0Param2.ParameterName = "@key2";
                get0Command.Parameters.Add(get0Param2);

                get0Param1.Value = identityId.ToByteArray();
                get0Param2.Value = key2;
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.Default))
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

        public virtual async Task<List<byte[]>> GetByKeyThreeAsync(Guid identityId,byte[] key3)
        {
            if (key3 == null) throw new Exception("Cannot be null");
            if (key3?.Length < 0) throw new Exception("Too short");
            if (key3?.Length > 256) throw new Exception("Too long");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get1Command = cn.CreateCommand();
            {
                get1Command.CommandText = "SELECT data FROM KeyUniqueThreeValueOld " +
                                             "WHERE identityId = @identityId AND key3 = @key3;"+
                                             ";";
                var get1Param1 = get1Command.CreateParameter();
                get1Param1.ParameterName = "@identityId";
                get1Command.Parameters.Add(get1Param1);
                var get1Param2 = get1Command.CreateParameter();
                get1Param2.ParameterName = "@key3";
                get1Command.Parameters.Add(get1Param2);

                get1Param1.Value = identityId.ToByteArray();
                get1Param2.Value = key3;
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

        public KeyUniqueThreeValueOldRecord ReadRecordFromReader2(DbDataReader rdr,Guid identityId,byte[] key2,byte[] key3)
        {
            if (key2 == null) throw new Exception("Cannot be null");
            if (key2?.Length < 0) throw new Exception("Too short");
            if (key2?.Length > 256) throw new Exception("Too long");
            if (key3 == null) throw new Exception("Cannot be null");
            if (key3?.Length < 0) throw new Exception("Too short");
            if (key3?.Length > 256) throw new Exception("Too long");
            var result = new List<KeyUniqueThreeValueOldRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new KeyUniqueThreeValueOldRecord();
            item.identityId = identityId;
            item.key2 = key2;
            item.key3 = key3;
            item.key1NoLengthCheck = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (byte[])(rdr[0]);
            if (item.key1?.Length < 16)
                throw new Exception("Too little data in key1...");
            item.dataNoLengthCheck = (rdr[1] == DBNull.Value) ? null : (byte[])(rdr[1]);
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            return item;
       }

        public virtual async Task<List<KeyUniqueThreeValueOldRecord>> GetByKeyTwoThreeAsync(Guid identityId,byte[] key2,byte[] key3)
        {
            if (key2 == null) throw new Exception("Cannot be null");
            if (key2?.Length < 0) throw new Exception("Too short");
            if (key2?.Length > 256) throw new Exception("Too long");
            if (key3 == null) throw new Exception("Cannot be null");
            if (key3?.Length < 0) throw new Exception("Too short");
            if (key3?.Length > 256) throw new Exception("Too long");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get2Command = cn.CreateCommand();
            {
                get2Command.CommandText = "SELECT key1,data FROM KeyUniqueThreeValueOld " +
                                             "WHERE identityId = @identityId AND key2 = @key2 AND key3 = @key3;"+
                                             ";";
                var get2Param1 = get2Command.CreateParameter();
                get2Param1.ParameterName = "@identityId";
                get2Command.Parameters.Add(get2Param1);
                var get2Param2 = get2Command.CreateParameter();
                get2Param2.ParameterName = "@key2";
                get2Command.Parameters.Add(get2Param2);
                var get2Param3 = get2Command.CreateParameter();
                get2Param3.ParameterName = "@key3";
                get2Command.Parameters.Add(get2Param3);

                get2Param1.Value = identityId.ToByteArray();
                get2Param2.Value = key2;
                get2Param3.Value = key3;
                {
                    using (var rdr = await get2Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            _cache.AddOrUpdate("TableKeyUniqueThreeValueOldCRUD", identityId.ToString()+key2.ToBase64()+key3.ToBase64(), null);
                            return new List<KeyUniqueThreeValueOldRecord>();
                        }
                        var result = new List<KeyUniqueThreeValueOldRecord>();
                        while (true)
                        {
                            result.Add(ReadRecordFromReader2(rdr,identityId,key2,key3));
                            if (!await rdr.ReadAsync())
                                break;
                        }
                        return result;
                    } // using
                } //
            } // using
        }

        public KeyUniqueThreeValueOldRecord ReadRecordFromReader3(DbDataReader rdr)
        {
            var result = new List<KeyUniqueThreeValueOldRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new KeyUniqueThreeValueOldRecord();
            item.identityId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[0]);
            item.key1NoLengthCheck = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (byte[])(rdr[1]);
            if (item.key1?.Length < 16)
                throw new Exception("Too little data in key1...");
            item.key2NoLengthCheck = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (byte[])(rdr[2]);
            if (item.key2?.Length < 0)
                throw new Exception("Too little data in key2...");
            item.key3NoLengthCheck = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (byte[])(rdr[3]);
            if (item.key3?.Length < 0)
                throw new Exception("Too little data in key3...");
            item.dataNoLengthCheck = (rdr[4] == DBNull.Value) ? null : (byte[])(rdr[4]);
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            return item;
       }

        public virtual async Task<List<KeyUniqueThreeValueOldRecord>> GetAllAsync()
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get3Command = cn.CreateCommand();
            {
                get3Command.CommandText = "SELECT identityId,key1,key2,key3,data FROM KeyUniqueThreeValueOld " +
                                             ";";

                {
                    using (var rdr = await get3Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return new List<KeyUniqueThreeValueOldRecord>();
                        }
                        var result = new List<KeyUniqueThreeValueOldRecord>();
                        while (true)
                        {
                            result.Add(ReadRecordFromReader3(rdr));
                            if (!await rdr.ReadAsync())
                                break;
                        }
                        return result;
                    } // using
                } //
            } // using
        }

        public KeyUniqueThreeValueOldRecord ReadRecordFromReader4(DbDataReader rdr,Guid identityId,byte[] key1)
        {
            if (key1 == null) throw new Exception("Cannot be null");
            if (key1?.Length < 16) throw new Exception("Too short");
            if (key1?.Length > 48) throw new Exception("Too long");
            var result = new List<KeyUniqueThreeValueOldRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new KeyUniqueThreeValueOldRecord();
            item.identityId = identityId;
            item.key1 = key1;
            item.key2NoLengthCheck = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (byte[])(rdr[0]);
            if (item.key2?.Length < 0)
                throw new Exception("Too little data in key2...");
            item.key3NoLengthCheck = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (byte[])(rdr[1]);
            if (item.key3?.Length < 0)
                throw new Exception("Too little data in key3...");
            item.dataNoLengthCheck = (rdr[2] == DBNull.Value) ? null : (byte[])(rdr[2]);
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            return item;
       }

        public virtual async Task<KeyUniqueThreeValueOldRecord> GetAsync(Guid identityId,byte[] key1)
        {
            if (key1 == null) throw new Exception("Cannot be null");
            if (key1?.Length < 16) throw new Exception("Too short");
            if (key1?.Length > 48) throw new Exception("Too long");
            var (hit, cacheObject) = _cache.Get("TableKeyUniqueThreeValueOldCRUD", identityId.ToString()+key1.ToBase64());
            if (hit)
                return (KeyUniqueThreeValueOldRecord)cacheObject;
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get4Command = cn.CreateCommand();
            {
                get4Command.CommandText = "SELECT key2,key3,data FROM KeyUniqueThreeValueOld " +
                                             "WHERE identityId = @identityId AND key1 = @key1 LIMIT 1;"+
                                             ";";
                var get4Param1 = get4Command.CreateParameter();
                get4Param1.ParameterName = "@identityId";
                get4Command.Parameters.Add(get4Param1);
                var get4Param2 = get4Command.CreateParameter();
                get4Param2.ParameterName = "@key1";
                get4Command.Parameters.Add(get4Param2);

                get4Param1.Value = identityId.ToByteArray();
                get4Param2.Value = key1;
                {
                    using (var rdr = await get4Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            _cache.AddOrUpdate("TableKeyUniqueThreeValueOldCRUD", identityId.ToString()+key1.ToBase64(), null);
                            return null;
                        }
                        var r = ReadRecordFromReader4(rdr,identityId,key1);
                        _cache.AddOrUpdate("TableKeyUniqueThreeValueOldCRUD", identityId.ToString()+key1.ToBase64(), r);
                        return r;
                    } // using
                } //
            } // using
        }

    }
}
