using System;
using System.Data.Common;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Odin.Core.Time;
using Odin.Core.Identity;

// THIS FILE IS AUTO GENERATED - DO NOT EDIT

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class KeyUniqueThreeValueRecord
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
    } // End of class KeyUniqueThreeValueRecord

    public class TableKeyUniqueThreeValueCRUD
    {
        private readonly CacheHelper _cache;

        public TableKeyUniqueThreeValueCRUD(CacheHelper cache)
        {
            _cache = cache;
        }


        public async Task EnsureTableExistsAsync(DatabaseConnection conn, bool dropExisting = false)
        {
                using (var cmd = conn.db.CreateCommand())
                {
                    if (dropExisting)
                    {
                       cmd.CommandText = "DROP TABLE IF EXISTS keyUniqueThreeValue;";
                       await conn.ExecuteNonQueryAsync(cmd);
                    }
                    cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS keyUniqueThreeValue("
                     +"identityId BLOB NOT NULL, "
                     +"key1 BLOB NOT NULL UNIQUE, "
                     +"key2 BLOB NOT NULL, "
                     +"key3 BLOB NOT NULL, "
                     +"data BLOB  "
                     +", PRIMARY KEY (identityId,key1)"
                     +", UNIQUE(identityId,key2,key3)"
                     +");"
                     +"CREATE INDEX IF NOT EXISTS Idx0TableKeyUniqueThreeValueCRUD ON keyUniqueThreeValue(identityId,key2);"
                     +"CREATE INDEX IF NOT EXISTS Idx1TableKeyUniqueThreeValueCRUD ON keyUniqueThreeValue(key3);"
                     ;
                    await conn.ExecuteNonQueryAsync(cmd);
            }
        }

        internal virtual async Task<int> InsertAsync(DatabaseConnection conn, KeyUniqueThreeValueRecord item)
        {
            DatabaseBase.AssertGuidNotEmpty(item.identityId, "Guid parameter identityId cannot be set to Empty GUID.");
            using (var insertCommand = conn.db.CreateCommand())
            {
                insertCommand.CommandText = "INSERT INTO keyUniqueThreeValue (identityId,key1,key2,key3,data) " +
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
                var count = await conn.ExecuteNonQueryAsync(insertCommand);
                if (count > 0)
                {
                    _cache.AddOrUpdate("TableKeyUniqueThreeValueCRUD", item.identityId.ToString()+item.key1.ToBase64(), item);
                }
                return count;
            } // Using
        }

        internal virtual async Task<int> TryInsertAsync(DatabaseConnection conn, KeyUniqueThreeValueRecord item)
        {
            DatabaseBase.AssertGuidNotEmpty(item.identityId, "Guid parameter identityId cannot be set to Empty GUID.");
            using (var insertCommand = conn.db.CreateCommand())
            {
                insertCommand.CommandText = "INSERT OR IGNORE INTO keyUniqueThreeValue (identityId,key1,key2,key3,data) " +
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
                var count = await conn.ExecuteNonQueryAsync(insertCommand);
                if (count > 0)
                {
                   _cache.AddOrUpdate("TableKeyUniqueThreeValueCRUD", item.identityId.ToString()+item.key1.ToBase64(), item);
                }
                return count;
            } // Using
        }

        internal virtual async Task<int> UpsertAsync(DatabaseConnection conn, KeyUniqueThreeValueRecord item)
        {
            DatabaseBase.AssertGuidNotEmpty(item.identityId, "Guid parameter identityId cannot be set to Empty GUID.");
            using (var upsertCommand = conn.db.CreateCommand())
            {
                upsertCommand.CommandText = "INSERT INTO keyUniqueThreeValue (identityId,key1,key2,key3,data) " +
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
                var count = await conn.ExecuteNonQueryAsync(upsertCommand);
                if (count > 0)
                    _cache.AddOrUpdate("TableKeyUniqueThreeValueCRUD", item.identityId.ToString()+item.key1.ToBase64(), item);
                return count;
            } // Using
        }
        internal virtual async Task<int> UpdateAsync(DatabaseConnection conn, KeyUniqueThreeValueRecord item)
        {
            DatabaseBase.AssertGuidNotEmpty(item.identityId, "Guid parameter identityId cannot be set to Empty GUID.");
            using (var updateCommand = conn.db.CreateCommand())
            {
                updateCommand.CommandText = "UPDATE keyUniqueThreeValue " +
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
                var count = await conn.ExecuteNonQueryAsync(updateCommand);
                if (count > 0)
                {
                    _cache.AddOrUpdate("TableKeyUniqueThreeValueCRUD", item.identityId.ToString()+item.key1.ToBase64(), item);
                }
                return count;
            } // Using
        }

        internal virtual async Task<int> GetCountDirtyAsync(DatabaseConnection conn)
        {
            using (var getCountCommand = conn.db.CreateCommand())
            {
                 // TODO: this is SQLite specific
                getCountCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM keyUniqueThreeValue; PRAGMA read_uncommitted = 0;";
                var count = await conn.ExecuteScalarAsync(getCountCommand);
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
            sl.Add("key1");
            sl.Add("key2");
            sl.Add("key3");
            sl.Add("data");
            return sl;
        }

        // SELECT identityId,key1,key2,key3,data
        internal KeyUniqueThreeValueRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<KeyUniqueThreeValueRecord>();
            byte[] tmpbuf = new byte[1048576+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new KeyUniqueThreeValueRecord();

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(0, 0, guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in identityId...");
                item.identityId = new Guid(guid);
            }

            if (rdr.IsDBNull(1))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(1, 0, tmpbuf, 0, 48+1);
                if (bytesRead > 48)
                    throw new Exception("Too much data in key1...");
                if (bytesRead < 16)
                    throw new Exception("Too little data in key1...");
                item.key1 = new byte[bytesRead];
                Buffer.BlockCopy(tmpbuf, 0, item.key1, 0, (int) bytesRead);
            }

            if (rdr.IsDBNull(2))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(2, 0, tmpbuf, 0, 256+1);
                if (bytesRead > 256)
                    throw new Exception("Too much data in key2...");
                if (bytesRead < 0)
                    throw new Exception("Too little data in key2...");
                item.key2 = new byte[bytesRead];
                Buffer.BlockCopy(tmpbuf, 0, item.key2, 0, (int) bytesRead);
            }

            if (rdr.IsDBNull(3))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(3, 0, tmpbuf, 0, 256+1);
                if (bytesRead > 256)
                    throw new Exception("Too much data in key3...");
                if (bytesRead < 0)
                    throw new Exception("Too little data in key3...");
                item.key3 = new byte[bytesRead];
                Buffer.BlockCopy(tmpbuf, 0, item.key3, 0, (int) bytesRead);
            }

            if (rdr.IsDBNull(4))
                item.data = null;
            else
            {
                bytesRead = rdr.GetBytes(4, 0, tmpbuf, 0, 1048576+1);
                if (bytesRead > 1048576)
                    throw new Exception("Too much data in data...");
                if (bytesRead < 0)
                    throw new Exception("Too little data in data...");
                item.data = new byte[bytesRead];
                Buffer.BlockCopy(tmpbuf, 0, item.data, 0, (int) bytesRead);
            }
            return item;
       }

        internal async Task<int> DeleteAsync(DatabaseConnection conn, Guid identityId,byte[] key1)
        {
            if (key1 == null) throw new Exception("Cannot be null");
            if (key1?.Length < 16) throw new Exception("Too short");
            if (key1?.Length > 48) throw new Exception("Too long");
            using (var delete0Command = conn.db.CreateCommand())
            {
                delete0Command.CommandText = "DELETE FROM keyUniqueThreeValue " +
                                             "WHERE identityId = @identityId AND key1 = @key1";
                var delete0Param1 = delete0Command.CreateParameter();
                delete0Param1.ParameterName = "@identityId";
                delete0Command.Parameters.Add(delete0Param1);
                var delete0Param2 = delete0Command.CreateParameter();
                delete0Param2.ParameterName = "@key1";
                delete0Command.Parameters.Add(delete0Param2);

                delete0Param1.Value = identityId.ToByteArray();
                delete0Param2.Value = key1;
                var count = await conn.ExecuteNonQueryAsync(delete0Command);
                if (count > 0)
                    _cache.Remove("TableKeyUniqueThreeValueCRUD", identityId.ToString()+key1.ToBase64());
                return count;
            } // Using
        }

        internal async Task<List<byte[]>> GetByKeyTwoAsync(DatabaseConnection conn, Guid identityId,byte[] key2)
        {
            if (key2 == null) throw new Exception("Cannot be null");
            if (key2?.Length < 0) throw new Exception("Too short");
            if (key2?.Length > 256) throw new Exception("Too long");
            using (var get0Command = conn.db.CreateCommand())
            {
                get0Command.CommandText = "SELECT data FROM keyUniqueThreeValue " +
                                             "WHERE identityId = @identityId AND key2 = @key2;";
                var get0Param1 = get0Command.CreateParameter();
                get0Param1.ParameterName = "@identityId";
                get0Command.Parameters.Add(get0Param1);
                var get0Param2 = get0Command.CreateParameter();
                get0Param2.ParameterName = "@key2";
                get0Command.Parameters.Add(get0Param2);

                get0Param1.Value = identityId.ToByteArray();
                get0Param2.Value = key2;
                {
                    using (var rdr = await conn.ExecuteReaderAsync(get0Command, System.Data.CommandBehavior.Default))
                    {
                        byte[] result0tmp;
                        var thelistresult = new List<byte[]>();
                        if (!rdr.Read()) {
                            return thelistresult;
                        }
                    byte[] tmpbuf = new byte[1048576+1];
#pragma warning disable CS0168
                    long bytesRead;
#pragma warning restore CS0168
                    var guid = new byte[16];
                    while (true)
                    {

                        if (rdr.IsDBNull(0))
                            result0tmp = null;
                        else
                        {
                            bytesRead = rdr.GetBytes(0, 0, tmpbuf, 0, 1048576+1);
                            if (bytesRead > 1048576)
                                throw new Exception("Too much data in data...");
                            if (bytesRead < 0)
                                throw new Exception("Too little data in data...");
                            result0tmp = new byte[bytesRead];
                            Buffer.BlockCopy(tmpbuf, 0, result0tmp, 0, (int) bytesRead);
                        }
                        thelistresult.Add(result0tmp);
                        if (!rdr.Read())
                           break;
                    } // while
                    return thelistresult;
                    } // using
                } //
            } // using
        }

        internal async Task<List<byte[]>> GetByKeyThreeAsync(DatabaseConnection conn, Guid identityId,byte[] key3)
        {
            if (key3 == null) throw new Exception("Cannot be null");
            if (key3?.Length < 0) throw new Exception("Too short");
            if (key3?.Length > 256) throw new Exception("Too long");
            using (var get1Command = conn.db.CreateCommand())
            {
                get1Command.CommandText = "SELECT data FROM keyUniqueThreeValue " +
                                             "WHERE identityId = @identityId AND key3 = @key3;";
                var get1Param1 = get1Command.CreateParameter();
                get1Param1.ParameterName = "@identityId";
                get1Command.Parameters.Add(get1Param1);
                var get1Param2 = get1Command.CreateParameter();
                get1Param2.ParameterName = "@key3";
                get1Command.Parameters.Add(get1Param2);

                get1Param1.Value = identityId.ToByteArray();
                get1Param2.Value = key3;
                {
                    using (var rdr = await conn.ExecuteReaderAsync(get1Command, System.Data.CommandBehavior.Default))
                    {
                        byte[] result0tmp;
                        var thelistresult = new List<byte[]>();
                        if (!rdr.Read()) {
                            return thelistresult;
                        }
                    byte[] tmpbuf = new byte[1048576+1];
#pragma warning disable CS0168
                    long bytesRead;
#pragma warning restore CS0168
                    var guid = new byte[16];
                    while (true)
                    {

                        if (rdr.IsDBNull(0))
                            result0tmp = null;
                        else
                        {
                            bytesRead = rdr.GetBytes(0, 0, tmpbuf, 0, 1048576+1);
                            if (bytesRead > 1048576)
                                throw new Exception("Too much data in data...");
                            if (bytesRead < 0)
                                throw new Exception("Too little data in data...");
                            result0tmp = new byte[bytesRead];
                            Buffer.BlockCopy(tmpbuf, 0, result0tmp, 0, (int) bytesRead);
                        }
                        thelistresult.Add(result0tmp);
                        if (!rdr.Read())
                           break;
                    } // while
                    return thelistresult;
                    } // using
                } //
            } // using
        }

        internal KeyUniqueThreeValueRecord ReadRecordFromReader2(DbDataReader rdr, Guid identityId,byte[] key2,byte[] key3)
        {
            if (key2 == null) throw new Exception("Cannot be null");
            if (key2?.Length < 0) throw new Exception("Too short");
            if (key2?.Length > 256) throw new Exception("Too long");
            if (key3 == null) throw new Exception("Cannot be null");
            if (key3?.Length < 0) throw new Exception("Too short");
            if (key3?.Length > 256) throw new Exception("Too long");
            var result = new List<KeyUniqueThreeValueRecord>();
            byte[] tmpbuf = new byte[1048576+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new KeyUniqueThreeValueRecord();
            item.identityId = identityId;
            item.key2 = key2;
            item.key3 = key3;

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(0, 0, tmpbuf, 0, 48+1);
                if (bytesRead > 48)
                    throw new Exception("Too much data in key1...");
                if (bytesRead < 16)
                    throw new Exception("Too little data in key1...");
                item.key1 = new byte[bytesRead];
                Buffer.BlockCopy(tmpbuf, 0, item.key1, 0, (int) bytesRead);
            }

            if (rdr.IsDBNull(1))
                item.data = null;
            else
            {
                bytesRead = rdr.GetBytes(1, 0, tmpbuf, 0, 1048576+1);
                if (bytesRead > 1048576)
                    throw new Exception("Too much data in data...");
                if (bytesRead < 0)
                    throw new Exception("Too little data in data...");
                item.data = new byte[bytesRead];
                Buffer.BlockCopy(tmpbuf, 0, item.data, 0, (int) bytesRead);
            }
            return item;
       }

        internal async Task<List<KeyUniqueThreeValueRecord>> GetByKeyTwoThreeAsync(DatabaseConnection conn, Guid identityId,byte[] key2,byte[] key3)
        {
            if (key2 == null) throw new Exception("Cannot be null");
            if (key2?.Length < 0) throw new Exception("Too short");
            if (key2?.Length > 256) throw new Exception("Too long");
            if (key3 == null) throw new Exception("Cannot be null");
            if (key3?.Length < 0) throw new Exception("Too short");
            if (key3?.Length > 256) throw new Exception("Too long");
            using (var get2Command = conn.db.CreateCommand())
            {
                get2Command.CommandText = "SELECT key1,data FROM keyUniqueThreeValue " +
                                             "WHERE identityId = @identityId AND key2 = @key2 AND key3 = @key3;";
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
                    using (var rdr = await conn.ExecuteReaderAsync(get2Command, System.Data.CommandBehavior.Default))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            _cache.AddOrUpdate("TableKeyUniqueThreeValueCRUD", identityId.ToString()+key2.ToBase64()+key3.ToBase64(), null);
                            return new List<KeyUniqueThreeValueRecord>();
                        }
                        var result = new List<KeyUniqueThreeValueRecord>();
                        while (true)
                        {
                            result.Add(ReadRecordFromReader2(rdr, identityId,key2,key3));
                            if (!rdr.Read())
                                break;
                        }
                        return result;
                    } // using
                } //
            } // using
        }

        internal KeyUniqueThreeValueRecord ReadRecordFromReader3(DbDataReader rdr, Guid identityId,byte[] key1)
        {
            if (key1 == null) throw new Exception("Cannot be null");
            if (key1?.Length < 16) throw new Exception("Too short");
            if (key1?.Length > 48) throw new Exception("Too long");
            var result = new List<KeyUniqueThreeValueRecord>();
            byte[] tmpbuf = new byte[1048576+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new KeyUniqueThreeValueRecord();
            item.identityId = identityId;
            item.key1 = key1;

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(0, 0, tmpbuf, 0, 256+1);
                if (bytesRead > 256)
                    throw new Exception("Too much data in key2...");
                if (bytesRead < 0)
                    throw new Exception("Too little data in key2...");
                item.key2 = new byte[bytesRead];
                Buffer.BlockCopy(tmpbuf, 0, item.key2, 0, (int) bytesRead);
            }

            if (rdr.IsDBNull(1))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(1, 0, tmpbuf, 0, 256+1);
                if (bytesRead > 256)
                    throw new Exception("Too much data in key3...");
                if (bytesRead < 0)
                    throw new Exception("Too little data in key3...");
                item.key3 = new byte[bytesRead];
                Buffer.BlockCopy(tmpbuf, 0, item.key3, 0, (int) bytesRead);
            }

            if (rdr.IsDBNull(2))
                item.data = null;
            else
            {
                bytesRead = rdr.GetBytes(2, 0, tmpbuf, 0, 1048576+1);
                if (bytesRead > 1048576)
                    throw new Exception("Too much data in data...");
                if (bytesRead < 0)
                    throw new Exception("Too little data in data...");
                item.data = new byte[bytesRead];
                Buffer.BlockCopy(tmpbuf, 0, item.data, 0, (int) bytesRead);
            }
            return item;
       }

        internal async Task<KeyUniqueThreeValueRecord> GetAsync(DatabaseConnection conn, Guid identityId,byte[] key1)
        {
            if (key1 == null) throw new Exception("Cannot be null");
            if (key1?.Length < 16) throw new Exception("Too short");
            if (key1?.Length > 48) throw new Exception("Too long");
            var (hit, cacheObject) = _cache.Get("TableKeyUniqueThreeValueCRUD", identityId.ToString()+key1.ToBase64());
            if (hit)
                return (KeyUniqueThreeValueRecord)cacheObject;
            using (var get3Command = conn.db.CreateCommand())
            {
                get3Command.CommandText = "SELECT key2,key3,data FROM keyUniqueThreeValue " +
                                             "WHERE identityId = @identityId AND key1 = @key1 LIMIT 1;";
                var get3Param1 = get3Command.CreateParameter();
                get3Param1.ParameterName = "@identityId";
                get3Command.Parameters.Add(get3Param1);
                var get3Param2 = get3Command.CreateParameter();
                get3Param2.ParameterName = "@key1";
                get3Command.Parameters.Add(get3Param2);

                get3Param1.Value = identityId.ToByteArray();
                get3Param2.Value = key1;
                {
                    using (var rdr = await conn.ExecuteReaderAsync(get3Command, System.Data.CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            _cache.AddOrUpdate("TableKeyUniqueThreeValueCRUD", identityId.ToString()+key1.ToBase64(), null);
                            return null;
                        }
                        var r = ReadRecordFromReader3(rdr, identityId,key1);
                        _cache.AddOrUpdate("TableKeyUniqueThreeValueCRUD", identityId.ToString()+key1.ToBase64(), r);
                        return r;
                    } // using
                } //
            } // using
        }

    }
}
