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

namespace Odin.Core.Storage.SQLite.NotaryDatabase
{
    public record NotaryChainRecord
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
        private byte[] _previousHash;
        public byte[] previousHash
        {
           get {
                   return _previousHash;
               }
           set {
                    if (value == null) throw new OdinDatabaseValidationException("Cannot be null previousHash");
                    if (value?.Length < 16) throw new OdinDatabaseValidationException($"Too short previousHash, was {value.Length} (min 16)");
                    if (value?.Length > 64) throw new OdinDatabaseValidationException($"Too long previousHash, was {value.Length} (max 64)");
                  _previousHash = value;
               }
        }
        internal byte[] previousHashNoLengthCheck
        {
           get {
                   return _previousHash;
               }
           set {
                    if (value == null) throw new OdinDatabaseValidationException("Cannot be null previousHash");
                    if (value?.Length < 16) throw new OdinDatabaseValidationException($"Too short previousHash, was {value.Length} (min 16)");
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
                    if (value == null) throw new OdinDatabaseValidationException("Cannot be null identity");
                    if (value?.Length < 3) throw new OdinDatabaseValidationException($"Too short identity, was {value.Length} (min 3)");
                    if (value?.Length > 256) throw new OdinDatabaseValidationException($"Too long identity, was {value.Length} (max 256)");
                  _identity = value;
               }
        }
        internal string identityNoLengthCheck
        {
           get {
                   return _identity;
               }
           set {
                    if (value == null) throw new OdinDatabaseValidationException("Cannot be null identity");
                    if (value?.Length < 3) throw new OdinDatabaseValidationException($"Too short identity, was {value.Length} (min 3)");
                  _identity = value;
               }
        }
        private UnixTimeUtc _timestamp;
        public UnixTimeUtc timestamp
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
                    if (value == null) throw new OdinDatabaseValidationException("Cannot be null signedPreviousHash");
                    if (value?.Length < 16) throw new OdinDatabaseValidationException($"Too short signedPreviousHash, was {value.Length} (min 16)");
                    if (value?.Length > 200) throw new OdinDatabaseValidationException($"Too long signedPreviousHash, was {value.Length} (max 200)");
                  _signedPreviousHash = value;
               }
        }
        internal byte[] signedPreviousHashNoLengthCheck
        {
           get {
                   return _signedPreviousHash;
               }
           set {
                    if (value == null) throw new OdinDatabaseValidationException("Cannot be null signedPreviousHash");
                    if (value?.Length < 16) throw new OdinDatabaseValidationException($"Too short signedPreviousHash, was {value.Length} (min 16)");
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
                    if (value == null) throw new OdinDatabaseValidationException("Cannot be null algorithm");
                    if (value?.Length < 1) throw new OdinDatabaseValidationException($"Too short algorithm, was {value.Length} (min 1)");
                    if (value?.Length > 40) throw new OdinDatabaseValidationException($"Too long algorithm, was {value.Length} (max 40)");
                  _algorithm = value;
               }
        }
        internal string algorithmNoLengthCheck
        {
           get {
                   return _algorithm;
               }
           set {
                    if (value == null) throw new OdinDatabaseValidationException("Cannot be null algorithm");
                    if (value?.Length < 1) throw new OdinDatabaseValidationException($"Too short algorithm, was {value.Length} (min 1)");
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
                    if (value == null) throw new OdinDatabaseValidationException("Cannot be null publicKeyJwkBase64Url");
                    if (value?.Length < 16) throw new OdinDatabaseValidationException($"Too short publicKeyJwkBase64Url, was {value.Length} (min 16)");
                    if (value?.Length > 600) throw new OdinDatabaseValidationException($"Too long publicKeyJwkBase64Url, was {value.Length} (max 600)");
                  _publicKeyJwkBase64Url = value;
               }
        }
        internal string publicKeyJwkBase64UrlNoLengthCheck
        {
           get {
                   return _publicKeyJwkBase64Url;
               }
           set {
                    if (value == null) throw new OdinDatabaseValidationException("Cannot be null publicKeyJwkBase64Url");
                    if (value?.Length < 16) throw new OdinDatabaseValidationException($"Too short publicKeyJwkBase64Url, was {value.Length} (min 16)");
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
                    if (value == null) throw new OdinDatabaseValidationException("Cannot be null notarySignature");
                    if (value?.Length < 16) throw new OdinDatabaseValidationException($"Too short notarySignature, was {value.Length} (min 16)");
                    if (value?.Length > 200) throw new OdinDatabaseValidationException($"Too long notarySignature, was {value.Length} (max 200)");
                  _notarySignature = value;
               }
        }
        internal byte[] notarySignatureNoLengthCheck
        {
           get {
                   return _notarySignature;
               }
           set {
                    if (value == null) throw new OdinDatabaseValidationException("Cannot be null notarySignature");
                    if (value?.Length < 16) throw new OdinDatabaseValidationException($"Too short notarySignature, was {value.Length} (min 16)");
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
                    if (value == null) throw new OdinDatabaseValidationException("Cannot be null recordHash");
                    if (value?.Length < 16) throw new OdinDatabaseValidationException($"Too short recordHash, was {value.Length} (min 16)");
                    if (value?.Length > 64) throw new OdinDatabaseValidationException($"Too long recordHash, was {value.Length} (max 64)");
                  _recordHash = value;
               }
        }
        internal byte[] recordHashNoLengthCheck
        {
           get {
                   return _recordHash;
               }
           set {
                    if (value == null) throw new OdinDatabaseValidationException("Cannot be null recordHash");
                    if (value?.Length < 16) throw new OdinDatabaseValidationException($"Too short recordHash, was {value.Length} (min 16)");
                  _recordHash = value;
               }
        }
    } // End of record NotaryChainRecord

    public class TableNotaryChainCRUD
    {
        private readonly CacheHelper _cache;

        public TableNotaryChainCRUD(CacheHelper cache)
        {
            _cache = cache;
        }


        public virtual async Task EnsureTableExistsAsync(DatabaseConnection conn, bool dropExisting = false)
        {
            await using var cmd = conn.db.CreateCommand();
            if (dropExisting)
            {
                cmd.CommandText = "DROP TABLE IF EXISTS NotaryChain;";
                await conn.ExecuteNonQueryAsync(cmd);
            }
            var rowid = "";
            rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            cmd.CommandText =
                "CREATE TABLE IF NOT EXISTS NotaryChain("
                   +rowid
                   +"previousHash BYTEA NOT NULL UNIQUE, "
                   +"identity TEXT NOT NULL, "
                   +"timestamp BIGINT NOT NULL, "
                   +"signedPreviousHash BYTEA NOT NULL UNIQUE, "
                   +"algorithm TEXT NOT NULL, "
                   +"publicKeyJwkBase64Url TEXT NOT NULL, "
                   +"notarySignature BYTEA NOT NULL UNIQUE, "
                   +"recordHash BYTEA NOT NULL UNIQUE "
                   +$"){wori};"
                   ;
            await conn.ExecuteNonQueryAsync(cmd);
        }

        public virtual async Task<int> InsertAsync(DatabaseConnection conn, NotaryChainRecord item)
        {
            using (var insertCommand = conn.db.CreateCommand())
            {
                insertCommand.CommandText = "INSERT INTO NotaryChain (previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,notarySignature,recordHash) " +
                                           $"VALUES (@previousHash,@identity,@timestamp,@signedPreviousHash,@algorithm,@publicKeyJwkBase64Url,@notarySignature,@recordHash)"+
                                            "RETURNING -1,-1,rowId;";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.DbType = DbType.Binary;
                insertParam1.ParameterName = "@previousHash";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.DbType = DbType.String;
                insertParam2.ParameterName = "@identity";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.DbType = DbType.Int64;
                insertParam3.ParameterName = "@timestamp";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.DbType = DbType.Binary;
                insertParam4.ParameterName = "@signedPreviousHash";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.DbType = DbType.String;
                insertParam5.ParameterName = "@algorithm";
                insertCommand.Parameters.Add(insertParam5);
                var insertParam6 = insertCommand.CreateParameter();
                insertParam6.DbType = DbType.String;
                insertParam6.ParameterName = "@publicKeyJwkBase64Url";
                insertCommand.Parameters.Add(insertParam6);
                var insertParam7 = insertCommand.CreateParameter();
                insertParam7.DbType = DbType.Binary;
                insertParam7.ParameterName = "@notarySignature";
                insertCommand.Parameters.Add(insertParam7);
                var insertParam8 = insertCommand.CreateParameter();
                insertParam8.DbType = DbType.Binary;
                insertParam8.ParameterName = "@recordHash";
                insertCommand.Parameters.Add(insertParam8);
                insertParam1.Value = item.previousHash;
                insertParam2.Value = item.identity;
                insertParam3.Value = item.timestamp.milliseconds;
                insertParam4.Value = item.signedPreviousHash;
                insertParam5.Value = item.algorithm;
                insertParam6.Value = item.publicKeyJwkBase64Url;
                insertParam7.Value = item.notarySignature;
                insertParam8.Value = item.recordHash;
                await using var rdr = await conn.ExecuteReaderAsync(insertCommand, CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                    _cache.AddOrUpdate("TableNotaryChainCRUD", item.notarySignature.ToBase64(), item);
                    return 1;
                }
                return 0;
            }
        }

        public virtual async Task<bool> TryInsertAsync(DatabaseConnection conn, NotaryChainRecord item)
        {
            using (var insertCommand = conn.db.CreateCommand())
            {
                insertCommand.CommandText = "INSERT INTO NotaryChain (previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,notarySignature,recordHash) " +
                                            $"VALUES (@previousHash,@identity,@timestamp,@signedPreviousHash,@algorithm,@publicKeyJwkBase64Url,@notarySignature,@recordHash) " +
                                            "ON CONFLICT DO NOTHING "+
                                            "RETURNING -1,-1,rowId;";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.DbType = DbType.Binary;
                insertParam1.ParameterName = "@previousHash";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.DbType = DbType.String;
                insertParam2.ParameterName = "@identity";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.DbType = DbType.Int64;
                insertParam3.ParameterName = "@timestamp";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.DbType = DbType.Binary;
                insertParam4.ParameterName = "@signedPreviousHash";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.DbType = DbType.String;
                insertParam5.ParameterName = "@algorithm";
                insertCommand.Parameters.Add(insertParam5);
                var insertParam6 = insertCommand.CreateParameter();
                insertParam6.DbType = DbType.String;
                insertParam6.ParameterName = "@publicKeyJwkBase64Url";
                insertCommand.Parameters.Add(insertParam6);
                var insertParam7 = insertCommand.CreateParameter();
                insertParam7.DbType = DbType.Binary;
                insertParam7.ParameterName = "@notarySignature";
                insertCommand.Parameters.Add(insertParam7);
                var insertParam8 = insertCommand.CreateParameter();
                insertParam8.DbType = DbType.Binary;
                insertParam8.ParameterName = "@recordHash";
                insertCommand.Parameters.Add(insertParam8);
                insertParam1.Value = item.previousHash;
                insertParam2.Value = item.identity;
                insertParam3.Value = item.timestamp.milliseconds;
                insertParam4.Value = item.signedPreviousHash;
                insertParam5.Value = item.algorithm;
                insertParam6.Value = item.publicKeyJwkBase64Url;
                insertParam7.Value = item.notarySignature;
                insertParam8.Value = item.recordHash;
                await using var rdr = await conn.ExecuteReaderAsync(insertCommand, CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                   _cache.AddOrUpdate("TableNotaryChainCRUD", item.notarySignature.ToBase64(), item);
                    return true;
                }
                return false;
            }
        }

        public virtual async Task<int> UpsertAsync(DatabaseConnection conn, NotaryChainRecord item)
        {
            using (var upsertCommand = conn.db.CreateCommand())
            {
                upsertCommand.CommandText = "INSERT INTO NotaryChain (previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,notarySignature,recordHash) " +
                                            $"VALUES (@previousHash,@identity,@timestamp,@signedPreviousHash,@algorithm,@publicKeyJwkBase64Url,@notarySignature,@recordHash)"+
                                            "ON CONFLICT (notarySignature) DO UPDATE "+
                                            $"SET previousHash = @previousHash,identity = @identity,timestamp = @timestamp,signedPreviousHash = @signedPreviousHash,algorithm = @algorithm,publicKeyJwkBase64Url = @publicKeyJwkBase64Url,recordHash = @recordHash "+
                                            "RETURNING -1,-1,rowId;";
                var upsertParam1 = upsertCommand.CreateParameter();
                upsertParam1.DbType = DbType.Binary;
                upsertParam1.ParameterName = "@previousHash";
                upsertCommand.Parameters.Add(upsertParam1);
                var upsertParam2 = upsertCommand.CreateParameter();
                upsertParam2.DbType = DbType.String;
                upsertParam2.ParameterName = "@identity";
                upsertCommand.Parameters.Add(upsertParam2);
                var upsertParam3 = upsertCommand.CreateParameter();
                upsertParam3.DbType = DbType.Int64;
                upsertParam3.ParameterName = "@timestamp";
                upsertCommand.Parameters.Add(upsertParam3);
                var upsertParam4 = upsertCommand.CreateParameter();
                upsertParam4.DbType = DbType.Binary;
                upsertParam4.ParameterName = "@signedPreviousHash";
                upsertCommand.Parameters.Add(upsertParam4);
                var upsertParam5 = upsertCommand.CreateParameter();
                upsertParam5.DbType = DbType.String;
                upsertParam5.ParameterName = "@algorithm";
                upsertCommand.Parameters.Add(upsertParam5);
                var upsertParam6 = upsertCommand.CreateParameter();
                upsertParam6.DbType = DbType.String;
                upsertParam6.ParameterName = "@publicKeyJwkBase64Url";
                upsertCommand.Parameters.Add(upsertParam6);
                var upsertParam7 = upsertCommand.CreateParameter();
                upsertParam7.DbType = DbType.Binary;
                upsertParam7.ParameterName = "@notarySignature";
                upsertCommand.Parameters.Add(upsertParam7);
                var upsertParam8 = upsertCommand.CreateParameter();
                upsertParam8.DbType = DbType.Binary;
                upsertParam8.ParameterName = "@recordHash";
                upsertCommand.Parameters.Add(upsertParam8);
                upsertParam1.Value = item.previousHash;
                upsertParam2.Value = item.identity;
                upsertParam3.Value = item.timestamp.milliseconds;
                upsertParam4.Value = item.signedPreviousHash;
                upsertParam5.Value = item.algorithm;
                upsertParam6.Value = item.publicKeyJwkBase64Url;
                upsertParam7.Value = item.notarySignature;
                upsertParam8.Value = item.recordHash;
                await using var rdr = await conn.ExecuteReaderAsync(upsertCommand, System.Data.CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                   _cache.AddOrUpdate("TableNotaryChainCRUD", item.notarySignature.ToBase64(), item);
                    return 1;
                }
                return 0;
            }
        }

        public virtual async Task<int> UpdateAsync(DatabaseConnection conn, NotaryChainRecord item)
        {
            using (var updateCommand = conn.db.CreateCommand())
            {
                updateCommand.CommandText = "UPDATE NotaryChain " +
                                            $"SET previousHash = @previousHash,identity = @identity,timestamp = @timestamp,signedPreviousHash = @signedPreviousHash,algorithm = @algorithm,publicKeyJwkBase64Url = @publicKeyJwkBase64Url,recordHash = @recordHash "+
                                            "WHERE (notarySignature = @notarySignature) "+
                                            "RETURNING -1,-1,rowId;";
                var updateParam1 = updateCommand.CreateParameter();
                updateParam1.DbType = DbType.Binary;
                updateParam1.ParameterName = "@previousHash";
                updateCommand.Parameters.Add(updateParam1);
                var updateParam2 = updateCommand.CreateParameter();
                updateParam2.DbType = DbType.String;
                updateParam2.ParameterName = "@identity";
                updateCommand.Parameters.Add(updateParam2);
                var updateParam3 = updateCommand.CreateParameter();
                updateParam3.DbType = DbType.Int64;
                updateParam3.ParameterName = "@timestamp";
                updateCommand.Parameters.Add(updateParam3);
                var updateParam4 = updateCommand.CreateParameter();
                updateParam4.DbType = DbType.Binary;
                updateParam4.ParameterName = "@signedPreviousHash";
                updateCommand.Parameters.Add(updateParam4);
                var updateParam5 = updateCommand.CreateParameter();
                updateParam5.DbType = DbType.String;
                updateParam5.ParameterName = "@algorithm";
                updateCommand.Parameters.Add(updateParam5);
                var updateParam6 = updateCommand.CreateParameter();
                updateParam6.DbType = DbType.String;
                updateParam6.ParameterName = "@publicKeyJwkBase64Url";
                updateCommand.Parameters.Add(updateParam6);
                var updateParam7 = updateCommand.CreateParameter();
                updateParam7.DbType = DbType.Binary;
                updateParam7.ParameterName = "@notarySignature";
                updateCommand.Parameters.Add(updateParam7);
                var updateParam8 = updateCommand.CreateParameter();
                updateParam8.DbType = DbType.Binary;
                updateParam8.ParameterName = "@recordHash";
                updateCommand.Parameters.Add(updateParam8);
                updateParam1.Value = item.previousHash;
                updateParam2.Value = item.identity;
                updateParam3.Value = item.timestamp.milliseconds;
                updateParam4.Value = item.signedPreviousHash;
                updateParam5.Value = item.algorithm;
                updateParam6.Value = item.publicKeyJwkBase64Url;
                updateParam7.Value = item.notarySignature;
                updateParam8.Value = item.recordHash;
                await using var rdr = await conn.ExecuteReaderAsync(updateCommand, System.Data.CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                   _cache.AddOrUpdate("TableNotaryChainCRUD", item.notarySignature.ToBase64(), item);
                    return 1;
                }
                return 0;
            }
        }

        public virtual async Task<int> GetCountAsync(DatabaseConnection conn)
        {
            using (var getCountCommand = conn.db.CreateCommand())
            {
                 // TODO: this is SQLite specific
                getCountCommand.CommandText = "SELECT COUNT(*) FROM NotaryChain;";
                var count = await conn.ExecuteScalarAsync(getCountCommand);
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

        // SELECT rowId,previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,notarySignature,recordHash
        public NotaryChainRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<NotaryChainRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new NotaryChainRecord();
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.previousHashNoLengthCheck = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (byte[])(rdr[1]);
            if (item.previousHash?.Length < 16)
                throw new Exception("Too little data in previousHash...");
            item.identityNoLengthCheck = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[2];
            item.timestamp = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[3]);
            item.signedPreviousHashNoLengthCheck = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (byte[])(rdr[4]);
            if (item.signedPreviousHash?.Length < 16)
                throw new Exception("Too little data in signedPreviousHash...");
            item.algorithmNoLengthCheck = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[5];
            item.publicKeyJwkBase64UrlNoLengthCheck = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[6];
            item.notarySignatureNoLengthCheck = (rdr[7] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (byte[])(rdr[7]);
            if (item.notarySignature?.Length < 16)
                throw new Exception("Too little data in notarySignature...");
            item.recordHashNoLengthCheck = (rdr[8] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (byte[])(rdr[8]);
            if (item.recordHash?.Length < 16)
                throw new Exception("Too little data in recordHash...");
            return item;
       }

        public virtual async Task<int> DeleteAsync(DatabaseConnection conn, byte[] notarySignature)
        {
            if (notarySignature == null) throw new OdinDatabaseValidationException("Cannot be null notarySignature");
            if (notarySignature?.Length < 16) throw new OdinDatabaseValidationException($"Too short notarySignature, was {notarySignature.Length} (min 16)");
            if (notarySignature?.Length > 200) throw new OdinDatabaseValidationException($"Too long notarySignature, was {notarySignature.Length} (max 200)");
            using (var delete0Command = conn.db.CreateCommand())
            {
                delete0Command.CommandText = "DELETE FROM NotaryChain " +
                                             "WHERE notarySignature = @notarySignature";
                var delete0Param1 = delete0Command.CreateParameter();
                delete0Param1.DbType = DbType.Binary;
                delete0Param1.ParameterName = "@notarySignature";
                delete0Command.Parameters.Add(delete0Param1);

                delete0Param1.Value = notarySignature;
                var count = await conn.ExecuteNonQueryAsync(delete0Command);
                if (count > 0)
                    _cache.Remove("TableNotaryChainCRUD", notarySignature.ToBase64());
                return count;
            }
        }

        public NotaryChainRecord ReadRecordFromReader0(DbDataReader rdr,byte[] notarySignature)
        {
            if (notarySignature == null) throw new OdinDatabaseValidationException("Cannot be null notarySignature");
            if (notarySignature?.Length < 16) throw new OdinDatabaseValidationException($"Too short notarySignature, was {notarySignature.Length} (min 16)");
            if (notarySignature?.Length > 200) throw new OdinDatabaseValidationException($"Too long notarySignature, was {notarySignature.Length} (max 200)");
            var result = new List<NotaryChainRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new NotaryChainRecord();
            item.notarySignature = notarySignature;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.previousHashNoLengthCheck = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (byte[])(rdr[1]);
            if (item.previousHash?.Length < 16)
                throw new Exception("Too little data in previousHash...");
            item.identityNoLengthCheck = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[2];
            item.timestamp = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[3]);
            item.signedPreviousHashNoLengthCheck = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (byte[])(rdr[4]);
            if (item.signedPreviousHash?.Length < 16)
                throw new Exception("Too little data in signedPreviousHash...");
            item.algorithmNoLengthCheck = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[5];
            item.publicKeyJwkBase64UrlNoLengthCheck = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[6];
            item.recordHashNoLengthCheck = (rdr[7] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (byte[])(rdr[7]);
            if (item.recordHash?.Length < 16)
                throw new Exception("Too little data in recordHash...");
            return item;
       }

        public virtual async Task<NotaryChainRecord> GetAsync(DatabaseConnection conn,byte[] notarySignature)
        {
            if (notarySignature == null) throw new OdinDatabaseValidationException("Cannot be null notarySignature");
            if (notarySignature?.Length < 16) throw new OdinDatabaseValidationException($"Too short notarySignature, was {notarySignature.Length} (min 16)");
            if (notarySignature?.Length > 200) throw new OdinDatabaseValidationException($"Too long notarySignature, was {notarySignature.Length} (max 200)");
            var (hit, cacheObject) = _cache.Get("TableNotaryChainCRUD", notarySignature.ToBase64());
            if (hit)
                return (NotaryChainRecord)cacheObject;
            using (var get0Command = conn.db.CreateCommand())
            {
                get0Command.CommandText = "SELECT rowId,previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,recordHash FROM NotaryChain " +
                                             "WHERE notarySignature = @notarySignature LIMIT 1;"+
                                             ";";
                var get0Param1 = get0Command.CreateParameter();
                get0Param1.DbType = DbType.Binary;
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
                        var r = ReadRecordFromReader0(rdr,notarySignature);
                        _cache.AddOrUpdate("TableNotaryChainCRUD", notarySignature.ToBase64(), r);
                        return r;
                    } // using
                } //
            } // using
        }

    }
}
