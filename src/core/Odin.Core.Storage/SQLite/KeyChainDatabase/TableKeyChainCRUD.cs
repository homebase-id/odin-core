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

namespace Odin.Core.Storage.SQLite.KeyChainDatabase
{
    public class KeyChainRecord
    {
        private byte[] _previousHash;
        public byte[] previousHash
        {
           get {
                   return _previousHash;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 16) throw new Exception("Too short");
                    if (value?.Length > 64) throw new Exception("Too long");
                  _previousHash = value;
               }
        }
        private string _identity;
        public string identity
        {
           get {
                   return _identity;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 3) throw new Exception("Too short");
                    if (value?.Length > 256) throw new Exception("Too long");
                  _identity = value;
               }
        }
        private UnixTimeUtcUnique _timestamp;
        public UnixTimeUtcUnique timestamp
        {
           get {
                   return _timestamp;
               }
           set {
                  _timestamp = value;
               }
        }
        private byte[] _signedPreviousHash;
        public byte[] signedPreviousHash
        {
           get {
                   return _signedPreviousHash;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 16) throw new Exception("Too short");
                    if (value?.Length > 200) throw new Exception("Too long");
                  _signedPreviousHash = value;
               }
        }
        private string _algorithm;
        public string algorithm
        {
           get {
                   return _algorithm;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 1) throw new Exception("Too short");
                    if (value?.Length > 40) throw new Exception("Too long");
                  _algorithm = value;
               }
        }
        private string _publicKeyJwkBase64Url;
        public string publicKeyJwkBase64Url
        {
           get {
                   return _publicKeyJwkBase64Url;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 16) throw new Exception("Too short");
                    if (value?.Length > 600) throw new Exception("Too long");
                  _publicKeyJwkBase64Url = value;
               }
        }
        private byte[] _recordHash;
        public byte[] recordHash
        {
           get {
                   return _recordHash;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 16) throw new Exception("Too short");
                    if (value?.Length > 64) throw new Exception("Too long");
                  _recordHash = value;
               }
        }
    } // End of class KeyChainRecord

    public class TableKeyChainCRUD
    {
        private readonly CacheHelper _cache;

        public TableKeyChainCRUD(CacheHelper cache)
        {
            _cache = cache;
        }


        public virtual async Task EnsureTableExistsAsync(DatabaseConnection conn, bool dropExisting = false)
        {
            await using var cmd = conn.db.CreateCommand();
            if (dropExisting)
            {
                cmd.CommandText = "DROP TABLE IF EXISTS keyChain;";
                await conn.ExecuteNonQueryAsync(cmd);
            }
            var rowid = "";
            cmd.CommandText =
                "CREATE TABLE IF NOT EXISTS keyChain("
                   +"previousHash BYTEA NOT NULL UNIQUE, "
                   +"identity TEXT NOT NULL, "
                   +"timestamp BIGINT NOT NULL, "
                   +"signedPreviousHash BYTEA NOT NULL UNIQUE, "
                   +"algorithm TEXT NOT NULL, "
                   +"publicKeyJwkBase64Url TEXT NOT NULL UNIQUE, "
                   +"recordHash BYTEA NOT NULL UNIQUE "
                   + rowid
                   +", PRIMARY KEY (identity,publicKeyJwkBase64Url)"
                   +");"
                   ;
            await conn.ExecuteNonQueryAsync(cmd);
        }

        public virtual async Task<int> InsertAsync(DatabaseConnection conn, KeyChainRecord item)
        {
            using (var insertCommand = conn.db.CreateCommand())
            {
                insertCommand.CommandText = "INSERT INTO keyChain (previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,recordHash) " +
                                             "VALUES (@previousHash,@identity,@timestamp,@signedPreviousHash,@algorithm,@publicKeyJwkBase64Url,@recordHash)";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.ParameterName = "@previousHash";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.ParameterName = "@identity";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.ParameterName = "@timestamp";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.ParameterName = "@signedPreviousHash";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.ParameterName = "@algorithm";
                insertCommand.Parameters.Add(insertParam5);
                var insertParam6 = insertCommand.CreateParameter();
                insertParam6.ParameterName = "@publicKeyJwkBase64Url";
                insertCommand.Parameters.Add(insertParam6);
                var insertParam7 = insertCommand.CreateParameter();
                insertParam7.ParameterName = "@recordHash";
                insertCommand.Parameters.Add(insertParam7);
                insertParam1.Value = item.previousHash;
                insertParam2.Value = item.identity;
                insertParam3.Value = item.timestamp.uniqueTime;
                insertParam4.Value = item.signedPreviousHash;
                insertParam5.Value = item.algorithm;
                insertParam6.Value = item.publicKeyJwkBase64Url;
                insertParam7.Value = item.recordHash;
                var count = await conn.ExecuteNonQueryAsync(insertCommand);
                if (count > 0)
                {
                    _cache.AddOrUpdate("TableKeyChainCRUD", item.identity+item.publicKeyJwkBase64Url, item);
                }
                return count;
            }
        }

        public virtual async Task<bool> TryInsertAsync(DatabaseConnection conn, KeyChainRecord item)
        {
            using (var insertCommand = conn.db.CreateCommand())
            {
                insertCommand.CommandText = "INSERT INTO keyChain (previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,recordHash) " +
                                             "VALUES (@previousHash,@identity,@timestamp,@signedPreviousHash,@algorithm,@publicKeyJwkBase64Url,@recordHash) " +
                                             "ON CONFLICT DO NOTHING";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.ParameterName = "@previousHash";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.ParameterName = "@identity";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.ParameterName = "@timestamp";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.ParameterName = "@signedPreviousHash";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.ParameterName = "@algorithm";
                insertCommand.Parameters.Add(insertParam5);
                var insertParam6 = insertCommand.CreateParameter();
                insertParam6.ParameterName = "@publicKeyJwkBase64Url";
                insertCommand.Parameters.Add(insertParam6);
                var insertParam7 = insertCommand.CreateParameter();
                insertParam7.ParameterName = "@recordHash";
                insertCommand.Parameters.Add(insertParam7);
                insertParam1.Value = item.previousHash;
                insertParam2.Value = item.identity;
                insertParam3.Value = item.timestamp.uniqueTime;
                insertParam4.Value = item.signedPreviousHash;
                insertParam5.Value = item.algorithm;
                insertParam6.Value = item.publicKeyJwkBase64Url;
                insertParam7.Value = item.recordHash;
                var count = await conn.ExecuteNonQueryAsync(insertCommand);
                if (count > 0)
                {
                   _cache.AddOrUpdate("TableKeyChainCRUD", item.identity+item.publicKeyJwkBase64Url, item);
                }
                return count > 0;
            }
        }

        public virtual async Task<int> UpsertAsync(DatabaseConnection conn, KeyChainRecord item)
        {
            using (var upsertCommand = conn.db.CreateCommand())
            {
                upsertCommand.CommandText = "INSERT INTO keyChain (previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,recordHash) " +
                                             "VALUES (@previousHash,@identity,@timestamp,@signedPreviousHash,@algorithm,@publicKeyJwkBase64Url,@recordHash)"+
                                             "ON CONFLICT (identity,publicKeyJwkBase64Url) DO UPDATE "+
                                             "SET previousHash = @previousHash,timestamp = @timestamp,signedPreviousHash = @signedPreviousHash,algorithm = @algorithm,recordHash = @recordHash "+
                                             ";";
                var upsertParam1 = upsertCommand.CreateParameter();
                upsertParam1.ParameterName = "@previousHash";
                upsertCommand.Parameters.Add(upsertParam1);
                var upsertParam2 = upsertCommand.CreateParameter();
                upsertParam2.ParameterName = "@identity";
                upsertCommand.Parameters.Add(upsertParam2);
                var upsertParam3 = upsertCommand.CreateParameter();
                upsertParam3.ParameterName = "@timestamp";
                upsertCommand.Parameters.Add(upsertParam3);
                var upsertParam4 = upsertCommand.CreateParameter();
                upsertParam4.ParameterName = "@signedPreviousHash";
                upsertCommand.Parameters.Add(upsertParam4);
                var upsertParam5 = upsertCommand.CreateParameter();
                upsertParam5.ParameterName = "@algorithm";
                upsertCommand.Parameters.Add(upsertParam5);
                var upsertParam6 = upsertCommand.CreateParameter();
                upsertParam6.ParameterName = "@publicKeyJwkBase64Url";
                upsertCommand.Parameters.Add(upsertParam6);
                var upsertParam7 = upsertCommand.CreateParameter();
                upsertParam7.ParameterName = "@recordHash";
                upsertCommand.Parameters.Add(upsertParam7);
                upsertParam1.Value = item.previousHash;
                upsertParam2.Value = item.identity;
                upsertParam3.Value = item.timestamp.uniqueTime;
                upsertParam4.Value = item.signedPreviousHash;
                upsertParam5.Value = item.algorithm;
                upsertParam6.Value = item.publicKeyJwkBase64Url;
                upsertParam7.Value = item.recordHash;
                var count = await conn.ExecuteNonQueryAsync(upsertCommand);
                if (count > 0)
                    _cache.AddOrUpdate("TableKeyChainCRUD", item.identity+item.publicKeyJwkBase64Url, item);
                return count;
            }
        }
        public virtual async Task<int> UpdateAsync(DatabaseConnection conn, KeyChainRecord item)
        {
            using (var updateCommand = conn.db.CreateCommand())
            {
                updateCommand.CommandText = "UPDATE keyChain " +
                                             "SET previousHash = @previousHash,timestamp = @timestamp,signedPreviousHash = @signedPreviousHash,algorithm = @algorithm,recordHash = @recordHash "+
                                             "WHERE (identity = @identity AND publicKeyJwkBase64Url = @publicKeyJwkBase64Url)";
                var updateParam1 = updateCommand.CreateParameter();
                updateParam1.ParameterName = "@previousHash";
                updateCommand.Parameters.Add(updateParam1);
                var updateParam2 = updateCommand.CreateParameter();
                updateParam2.ParameterName = "@identity";
                updateCommand.Parameters.Add(updateParam2);
                var updateParam3 = updateCommand.CreateParameter();
                updateParam3.ParameterName = "@timestamp";
                updateCommand.Parameters.Add(updateParam3);
                var updateParam4 = updateCommand.CreateParameter();
                updateParam4.ParameterName = "@signedPreviousHash";
                updateCommand.Parameters.Add(updateParam4);
                var updateParam5 = updateCommand.CreateParameter();
                updateParam5.ParameterName = "@algorithm";
                updateCommand.Parameters.Add(updateParam5);
                var updateParam6 = updateCommand.CreateParameter();
                updateParam6.ParameterName = "@publicKeyJwkBase64Url";
                updateCommand.Parameters.Add(updateParam6);
                var updateParam7 = updateCommand.CreateParameter();
                updateParam7.ParameterName = "@recordHash";
                updateCommand.Parameters.Add(updateParam7);
                updateParam1.Value = item.previousHash;
                updateParam2.Value = item.identity;
                updateParam3.Value = item.timestamp.uniqueTime;
                updateParam4.Value = item.signedPreviousHash;
                updateParam5.Value = item.algorithm;
                updateParam6.Value = item.publicKeyJwkBase64Url;
                updateParam7.Value = item.recordHash;
                var count = await conn.ExecuteNonQueryAsync(updateCommand);
                if (count > 0)
                {
                    _cache.AddOrUpdate("TableKeyChainCRUD", item.identity+item.publicKeyJwkBase64Url, item);
                }
                return count;
            }
        }

        public virtual async Task<int> GetCountAsync(DatabaseConnection conn)
        {
            using (var getCountCommand = conn.db.CreateCommand())
            {
                 // TODO: this is SQLite specific
                getCountCommand.CommandText = "SELECT COUNT(*) FROM keyChain;";
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
            sl.Add("previousHash");
            sl.Add("identity");
            sl.Add("timestamp");
            sl.Add("signedPreviousHash");
            sl.Add("algorithm");
            sl.Add("publicKeyJwkBase64Url");
            sl.Add("recordHash");
            return sl;
        }

        // SELECT previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,recordHash
        public KeyChainRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<KeyChainRecord>();
            byte[] tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new KeyChainRecord();
            item.previousHash = rdr.IsDBNull(0) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : (byte[])(rdr[0]);
            if (item.previousHash?.Length > 64)
                throw new Exception("Too much data in previousHash...");
            if (item.previousHash?.Length < 16)
                throw new Exception("Too little data in previousHash...");
            item.identity = rdr.IsDBNull(1) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[1];
            item.timestamp = rdr.IsDBNull(2) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtcUnique((long)rdr[2]);
            item.signedPreviousHash = rdr.IsDBNull(3) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : (byte[])(rdr[3]);
            if (item.signedPreviousHash?.Length > 200)
                throw new Exception("Too much data in signedPreviousHash...");
            if (item.signedPreviousHash?.Length < 16)
                throw new Exception("Too little data in signedPreviousHash...");
            item.algorithm = rdr.IsDBNull(4) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[4];
            item.publicKeyJwkBase64Url = rdr.IsDBNull(5) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[5];
            item.recordHash = rdr.IsDBNull(6) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : (byte[])(rdr[6]);
            if (item.recordHash?.Length > 64)
                throw new Exception("Too much data in recordHash...");
            if (item.recordHash?.Length < 16)
                throw new Exception("Too little data in recordHash...");
            return item;
       }

        public virtual async Task<int> DeleteAsync(DatabaseConnection conn, string identity,string publicKeyJwkBase64Url)
        {
            if (identity == null) throw new Exception("Cannot be null");
            if (identity?.Length < 3) throw new Exception("Too short");
            if (identity?.Length > 256) throw new Exception("Too long");
            if (publicKeyJwkBase64Url == null) throw new Exception("Cannot be null");
            if (publicKeyJwkBase64Url?.Length < 16) throw new Exception("Too short");
            if (publicKeyJwkBase64Url?.Length > 600) throw new Exception("Too long");
            using (var delete0Command = conn.db.CreateCommand())
            {
                delete0Command.CommandText = "DELETE FROM keyChain " +
                                             "WHERE identity = @identity AND publicKeyJwkBase64Url = @publicKeyJwkBase64Url";
                var delete0Param1 = delete0Command.CreateParameter();
                delete0Param1.ParameterName = "@identity";
                delete0Command.Parameters.Add(delete0Param1);
                var delete0Param2 = delete0Command.CreateParameter();
                delete0Param2.ParameterName = "@publicKeyJwkBase64Url";
                delete0Command.Parameters.Add(delete0Param2);

                delete0Param1.Value = identity;
                delete0Param2.Value = publicKeyJwkBase64Url;
                var count = await conn.ExecuteNonQueryAsync(delete0Command);
                if (count > 0)
                    _cache.Remove("TableKeyChainCRUD", identity+publicKeyJwkBase64Url);
                return count;
            }
        }

        public KeyChainRecord ReadRecordFromReader0(DbDataReader rdr, string identity,string publicKeyJwkBase64Url)
        {
            if (identity == null) throw new Exception("Cannot be null");
            if (identity?.Length < 3) throw new Exception("Too short");
            if (identity?.Length > 256) throw new Exception("Too long");
            if (publicKeyJwkBase64Url == null) throw new Exception("Cannot be null");
            if (publicKeyJwkBase64Url?.Length < 16) throw new Exception("Too short");
            if (publicKeyJwkBase64Url?.Length > 600) throw new Exception("Too long");
            var result = new List<KeyChainRecord>();
            byte[] tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new KeyChainRecord();
            item.identity = identity;
            item.publicKeyJwkBase64Url = publicKeyJwkBase64Url;

            item.previousHash = rdr.IsDBNull(0) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : (byte[])(rdr[0]);
            if (item.previousHash?.Length > 64)
                throw new Exception("Too much data in previousHash...");
            if (item.previousHash?.Length < 16)
                throw new Exception("Too little data in previousHash...");

            item.timestamp = rdr.IsDBNull(1) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtcUnique((long)rdr[1]);

            item.signedPreviousHash = rdr.IsDBNull(2) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : (byte[])(rdr[2]);
            if (item.signedPreviousHash?.Length > 200)
                throw new Exception("Too much data in signedPreviousHash...");
            if (item.signedPreviousHash?.Length < 16)
                throw new Exception("Too little data in signedPreviousHash...");

            item.algorithm = rdr.IsDBNull(3) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[3];

            item.recordHash = rdr.IsDBNull(4) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : (byte[])(rdr[4]);
            if (item.recordHash?.Length > 64)
                throw new Exception("Too much data in recordHash...");
            if (item.recordHash?.Length < 16)
                throw new Exception("Too little data in recordHash...");
            return item;
       }

        public virtual async Task<KeyChainRecord> GetAsync(DatabaseConnection conn, string identity,string publicKeyJwkBase64Url)
        {
            if (identity == null) throw new Exception("Cannot be null");
            if (identity?.Length < 3) throw new Exception("Too short");
            if (identity?.Length > 256) throw new Exception("Too long");
            if (publicKeyJwkBase64Url == null) throw new Exception("Cannot be null");
            if (publicKeyJwkBase64Url?.Length < 16) throw new Exception("Too short");
            if (publicKeyJwkBase64Url?.Length > 600) throw new Exception("Too long");
            var (hit, cacheObject) = _cache.Get("TableKeyChainCRUD", identity+publicKeyJwkBase64Url);
            if (hit)
                return (KeyChainRecord)cacheObject;
            using (var get0Command = conn.db.CreateCommand())
            {
                get0Command.CommandText = "SELECT previousHash,timestamp,signedPreviousHash,algorithm,recordHash FROM keyChain " +
                                             "WHERE identity = @identity AND publicKeyJwkBase64Url = @publicKeyJwkBase64Url LIMIT 1;";
                var get0Param1 = get0Command.CreateParameter();
                get0Param1.ParameterName = "@identity";
                get0Command.Parameters.Add(get0Param1);
                var get0Param2 = get0Command.CreateParameter();
                get0Param2.ParameterName = "@publicKeyJwkBase64Url";
                get0Command.Parameters.Add(get0Param2);

                get0Param1.Value = identity;
                get0Param2.Value = publicKeyJwkBase64Url;
                {
                    using (var rdr = await conn.ExecuteReaderAsync(get0Command, System.Data.CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            _cache.AddOrUpdate("TableKeyChainCRUD", identity+publicKeyJwkBase64Url, null);
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr, identity,publicKeyJwkBase64Url);
                        _cache.AddOrUpdate("TableKeyChainCRUD", identity+publicKeyJwkBase64Url, r);
                        return r;
                    } // using
                } //
            } // using
        }

    }
}
