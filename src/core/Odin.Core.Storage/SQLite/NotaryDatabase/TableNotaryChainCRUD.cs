using System;
using System.Data.Common;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Odin.Core.Time;
using Odin.Core.Identity;

// THIS FILE IS AUTO GENERATED - DO NOT EDIT

namespace Odin.Core.Storage.SQLite.NotaryDatabase
{
    public class NotaryChainRecord
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
        private byte[] _notarySignature;
        public byte[] notarySignature
        {
           get {
                   return _notarySignature;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 16) throw new Exception("Too short");
                    if (value?.Length > 200) throw new Exception("Too long");
                  _notarySignature = value;
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
    } // End of class NotaryChainRecord

    public class TableNotaryChainCRUD : TableBase
    {
        private bool _disposed = false;
        private readonly CacheHelper _cache;

        public TableNotaryChainCRUD(CacheHelper cache) : base("notaryChain")
        {
            _cache = cache;
        }

        ~TableNotaryChainCRUD()
        {
            if (_disposed == false) throw new Exception("TableNotaryChainCRUD Not disposed properly");
        }

        public override void Dispose()
        {
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        public sealed override async Task EnsureTableExistsAsync(DatabaseConnection conn, bool dropExisting = false)
        {
                using (var cmd = conn.db.CreateCommand())
                {
                    if (dropExisting)
                    {
                       cmd.CommandText = "DROP TABLE IF EXISTS notaryChain;";
                       await conn.ExecuteNonQueryAsync(cmd);
                    }
                    cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS notaryChain("
                     +"previousHash BLOB NOT NULL UNIQUE, "
                     +"identity STRING NOT NULL, "
                     +"timestamp INT NOT NULL, "
                     +"signedPreviousHash BLOB NOT NULL UNIQUE, "
                     +"algorithm STRING NOT NULL, "
                     +"publicKeyJwkBase64Url STRING NOT NULL, "
                     +"notarySignature BLOB NOT NULL UNIQUE, "
                     +"recordHash BLOB NOT NULL UNIQUE "
                     +", PRIMARY KEY (notarySignature)"
                     +");"
                     ;
                    await conn.ExecuteNonQueryAsync(cmd);
            }
        }

        public virtual async Task<int> InsertAsync(DatabaseConnection conn, NotaryChainRecord item)
        {
            using (var insertCommand = conn.db.CreateCommand())
            {
                insertCommand.CommandText = "INSERT INTO notaryChain (previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,notarySignature,recordHash) " +
                                             "VALUES (@previousHash,@identity,@timestamp,@signedPreviousHash,@algorithm,@publicKeyJwkBase64Url,@notarySignature,@recordHash)";
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
                insertParam7.ParameterName = "@notarySignature";
                insertCommand.Parameters.Add(insertParam7);
                var insertParam8 = insertCommand.CreateParameter();
                insertParam8.ParameterName = "@recordHash";
                insertCommand.Parameters.Add(insertParam8);
                insertParam1.Value = item.previousHash;
                insertParam2.Value = item.identity;
                insertParam3.Value = item.timestamp.uniqueTime;
                insertParam4.Value = item.signedPreviousHash;
                insertParam5.Value = item.algorithm;
                insertParam6.Value = item.publicKeyJwkBase64Url;
                insertParam7.Value = item.notarySignature;
                insertParam8.Value = item.recordHash;
                var count = await conn.ExecuteNonQueryAsync(insertCommand);
                if (count > 0)
                {
                    _cache.AddOrUpdate("TableNotaryChainCRUD", item.notarySignature.ToBase64(), item);
                }
                return count;
            } // Using
        }

        public virtual async Task<int> TryInsertAsync(DatabaseConnection conn, NotaryChainRecord item)
        {
            using (var insertCommand = conn.db.CreateCommand())
            {
                insertCommand.CommandText = "INSERT OR IGNORE INTO notaryChain (previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,notarySignature,recordHash) " +
                                             "VALUES (@previousHash,@identity,@timestamp,@signedPreviousHash,@algorithm,@publicKeyJwkBase64Url,@notarySignature,@recordHash)";
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
                insertParam7.ParameterName = "@notarySignature";
                insertCommand.Parameters.Add(insertParam7);
                var insertParam8 = insertCommand.CreateParameter();
                insertParam8.ParameterName = "@recordHash";
                insertCommand.Parameters.Add(insertParam8);
                insertParam1.Value = item.previousHash;
                insertParam2.Value = item.identity;
                insertParam3.Value = item.timestamp.uniqueTime;
                insertParam4.Value = item.signedPreviousHash;
                insertParam5.Value = item.algorithm;
                insertParam6.Value = item.publicKeyJwkBase64Url;
                insertParam7.Value = item.notarySignature;
                insertParam8.Value = item.recordHash;
                var count = await conn.ExecuteNonQueryAsync(insertCommand);
                if (count > 0)
                {
                   _cache.AddOrUpdate("TableNotaryChainCRUD", item.notarySignature.ToBase64(), item);
                }
                return count;
            } // Using
        }

        public virtual async Task<int> UpsertAsync(DatabaseConnection conn, NotaryChainRecord item)
        {
            using (var upsertCommand = conn.db.CreateCommand())
            {
                upsertCommand.CommandText = "INSERT INTO notaryChain (previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,notarySignature,recordHash) " +
                                             "VALUES (@previousHash,@identity,@timestamp,@signedPreviousHash,@algorithm,@publicKeyJwkBase64Url,@notarySignature,@recordHash)"+
                                             "ON CONFLICT (notarySignature) DO UPDATE "+
                                             "SET previousHash = @previousHash,identity = @identity,timestamp = @timestamp,signedPreviousHash = @signedPreviousHash,algorithm = @algorithm,publicKeyJwkBase64Url = @publicKeyJwkBase64Url,recordHash = @recordHash "+
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
                upsertParam7.ParameterName = "@notarySignature";
                upsertCommand.Parameters.Add(upsertParam7);
                var upsertParam8 = upsertCommand.CreateParameter();
                upsertParam8.ParameterName = "@recordHash";
                upsertCommand.Parameters.Add(upsertParam8);
                upsertParam1.Value = item.previousHash;
                upsertParam2.Value = item.identity;
                upsertParam3.Value = item.timestamp.uniqueTime;
                upsertParam4.Value = item.signedPreviousHash;
                upsertParam5.Value = item.algorithm;
                upsertParam6.Value = item.publicKeyJwkBase64Url;
                upsertParam7.Value = item.notarySignature;
                upsertParam8.Value = item.recordHash;
                var count = await conn.ExecuteNonQueryAsync(upsertCommand);
                if (count > 0)
                    _cache.AddOrUpdate("TableNotaryChainCRUD", item.notarySignature.ToBase64(), item);
                return count;
            } // Using
        }
        public virtual async Task<int> UpdateAsync(DatabaseConnection conn, NotaryChainRecord item)
        {
            using (var updateCommand = conn.db.CreateCommand())
            {
                updateCommand.CommandText = "UPDATE notaryChain " +
                                             "SET previousHash = @previousHash,identity = @identity,timestamp = @timestamp,signedPreviousHash = @signedPreviousHash,algorithm = @algorithm,publicKeyJwkBase64Url = @publicKeyJwkBase64Url,recordHash = @recordHash "+
                                             "WHERE (notarySignature = @notarySignature)";
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
                updateParam7.ParameterName = "@notarySignature";
                updateCommand.Parameters.Add(updateParam7);
                var updateParam8 = updateCommand.CreateParameter();
                updateParam8.ParameterName = "@recordHash";
                updateCommand.Parameters.Add(updateParam8);
                updateParam1.Value = item.previousHash;
                updateParam2.Value = item.identity;
                updateParam3.Value = item.timestamp.uniqueTime;
                updateParam4.Value = item.signedPreviousHash;
                updateParam5.Value = item.algorithm;
                updateParam6.Value = item.publicKeyJwkBase64Url;
                updateParam7.Value = item.notarySignature;
                updateParam8.Value = item.recordHash;
                var count = await conn.ExecuteNonQueryAsync(updateCommand);
                if (count > 0)
                {
                    _cache.AddOrUpdate("TableNotaryChainCRUD", item.notarySignature.ToBase64(), item);
                }
                return count;
            } // Using
        }

        public virtual async Task<int> GetCountDirtyAsync(DatabaseConnection conn)
        {
            using (var getCountCommand = conn.db.CreateCommand())
            {
                 // TODO: this is SQLite specific
                getCountCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM notaryChain; PRAGMA read_uncommitted = 0;";
                var count = await conn.ExecuteScalarAsync(getCountCommand);
                if (count == null || count == DBNull.Value || !(count is int || count is long))
                    return -1;
                else
                    return Convert.ToInt32(count);
            }
        }

        public override List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("previousHash");
            sl.Add("identity");
            sl.Add("timestamp");
            sl.Add("signedPreviousHash");
            sl.Add("algorithm");
            sl.Add("publicKeyJwkBase64Url");
            sl.Add("notarySignature");
            sl.Add("recordHash");
            return sl;
        }

        // SELECT previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,notarySignature,recordHash
        public NotaryChainRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<NotaryChainRecord>();
            byte[] tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new NotaryChainRecord();

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(0, 0, tmpbuf, 0, 64+1);
                if (bytesRead > 64)
                    throw new Exception("Too much data in previousHash...");
                if (bytesRead < 16)
                    throw new Exception("Too little data in previousHash...");
                item.previousHash = new byte[bytesRead];
                Buffer.BlockCopy(tmpbuf, 0, item.previousHash, 0, (int) bytesRead);
            }

            if (rdr.IsDBNull(1))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.identity = rdr.GetString(1);
            }

            if (rdr.IsDBNull(2))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.timestamp = new UnixTimeUtcUnique(rdr.GetInt64(2));
            }

            if (rdr.IsDBNull(3))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(3, 0, tmpbuf, 0, 200+1);
                if (bytesRead > 200)
                    throw new Exception("Too much data in signedPreviousHash...");
                if (bytesRead < 16)
                    throw new Exception("Too little data in signedPreviousHash...");
                item.signedPreviousHash = new byte[bytesRead];
                Buffer.BlockCopy(tmpbuf, 0, item.signedPreviousHash, 0, (int) bytesRead);
            }

            if (rdr.IsDBNull(4))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.algorithm = rdr.GetString(4);
            }

            if (rdr.IsDBNull(5))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.publicKeyJwkBase64Url = rdr.GetString(5);
            }

            if (rdr.IsDBNull(6))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(6, 0, tmpbuf, 0, 200+1);
                if (bytesRead > 200)
                    throw new Exception("Too much data in notarySignature...");
                if (bytesRead < 16)
                    throw new Exception("Too little data in notarySignature...");
                item.notarySignature = new byte[bytesRead];
                Buffer.BlockCopy(tmpbuf, 0, item.notarySignature, 0, (int) bytesRead);
            }

            if (rdr.IsDBNull(7))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(7, 0, tmpbuf, 0, 64+1);
                if (bytesRead > 64)
                    throw new Exception("Too much data in recordHash...");
                if (bytesRead < 16)
                    throw new Exception("Too little data in recordHash...");
                item.recordHash = new byte[bytesRead];
                Buffer.BlockCopy(tmpbuf, 0, item.recordHash, 0, (int) bytesRead);
            }
            return item;
       }

        public async Task<int> DeleteAsync(DatabaseConnection conn, byte[] notarySignature)
        {
            if (notarySignature == null) throw new Exception("Cannot be null");
            if (notarySignature?.Length < 16) throw new Exception("Too short");
            if (notarySignature?.Length > 200) throw new Exception("Too long");
            using (var delete0Command = conn.db.CreateCommand())
            {
                delete0Command.CommandText = "DELETE FROM notaryChain " +
                                             "WHERE notarySignature = @notarySignature";
                var delete0Param1 = delete0Command.CreateParameter();
                delete0Param1.ParameterName = "@notarySignature";
                delete0Command.Parameters.Add(delete0Param1);

                delete0Param1.Value = notarySignature;
                var count = await conn.ExecuteNonQueryAsync(delete0Command);
                if (count > 0)
                    _cache.Remove("TableNotaryChainCRUD", notarySignature.ToBase64());
                return count;
            } // Using
        }

        public NotaryChainRecord ReadRecordFromReader0(DbDataReader rdr, byte[] notarySignature)
        {
            if (notarySignature == null) throw new Exception("Cannot be null");
            if (notarySignature?.Length < 16) throw new Exception("Too short");
            if (notarySignature?.Length > 200) throw new Exception("Too long");
            var result = new List<NotaryChainRecord>();
            byte[] tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new NotaryChainRecord();
            item.notarySignature = notarySignature;

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(0, 0, tmpbuf, 0, 64+1);
                if (bytesRead > 64)
                    throw new Exception("Too much data in previousHash...");
                if (bytesRead < 16)
                    throw new Exception("Too little data in previousHash...");
                item.previousHash = new byte[bytesRead];
                Buffer.BlockCopy(tmpbuf, 0, item.previousHash, 0, (int) bytesRead);
            }

            if (rdr.IsDBNull(1))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.identity = rdr.GetString(1);
            }

            if (rdr.IsDBNull(2))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.timestamp = new UnixTimeUtcUnique(rdr.GetInt64(2));
            }

            if (rdr.IsDBNull(3))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(3, 0, tmpbuf, 0, 200+1);
                if (bytesRead > 200)
                    throw new Exception("Too much data in signedPreviousHash...");
                if (bytesRead < 16)
                    throw new Exception("Too little data in signedPreviousHash...");
                item.signedPreviousHash = new byte[bytesRead];
                Buffer.BlockCopy(tmpbuf, 0, item.signedPreviousHash, 0, (int) bytesRead);
            }

            if (rdr.IsDBNull(4))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.algorithm = rdr.GetString(4);
            }

            if (rdr.IsDBNull(5))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.publicKeyJwkBase64Url = rdr.GetString(5);
            }

            if (rdr.IsDBNull(6))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(6, 0, tmpbuf, 0, 64+1);
                if (bytesRead > 64)
                    throw new Exception("Too much data in recordHash...");
                if (bytesRead < 16)
                    throw new Exception("Too little data in recordHash...");
                item.recordHash = new byte[bytesRead];
                Buffer.BlockCopy(tmpbuf, 0, item.recordHash, 0, (int) bytesRead);
            }
            return item;
       }

        public async Task<NotaryChainRecord> GetAsync(DatabaseConnection conn, byte[] notarySignature)
        {
            if (notarySignature == null) throw new Exception("Cannot be null");
            if (notarySignature?.Length < 16) throw new Exception("Too short");
            if (notarySignature?.Length > 200) throw new Exception("Too long");
            var (hit, cacheObject) = _cache.Get("TableNotaryChainCRUD", notarySignature.ToBase64());
            if (hit)
                return (NotaryChainRecord)cacheObject;
            using (var get0Command = conn.db.CreateCommand())
            {
                get0Command.CommandText = "SELECT previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,recordHash FROM notaryChain " +
                                             "WHERE notarySignature = @notarySignature LIMIT 1;";
                var get0Param1 = get0Command.CreateParameter();
                get0Param1.ParameterName = "@notarySignature";
                get0Command.Parameters.Add(get0Param1);

                get0Param1.Value = notarySignature;
                {
                    using (var rdr = await conn.ExecuteReaderAsync(get0Command, System.Data.CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            _cache.AddOrUpdate("TableNotaryChainCRUD", notarySignature.ToBase64(), null);
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr, notarySignature);
                        _cache.AddOrUpdate("TableNotaryChainCRUD", notarySignature.ToBase64(), r);
                        return r;
                    } // using
                } //
            } // using
        }

    }
}
