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

namespace Odin.Core.Storage.Database.KeyChain.Table
{
    public record KeyChainRecord
    {
        public Int64 rowId { get; set; }
        public byte[] previousHash { get; set; }
        public string identity { get; set; }
        public UnixTimeUtc timestamp { get; set; }
        public byte[] signedPreviousHash { get; set; }
        public string algorithm { get; set; }
        public string publicKeyJwkBase64Url { get; set; }
        public byte[] recordHash { get; set; }
        public void Validate()
        {
            if (previousHash == null) throw new OdinDatabaseValidationException("Cannot be null previousHash");
            if (previousHash?.Length < 16) throw new OdinDatabaseValidationException($"Too short previousHash, was {previousHash.Length} (min 16)");
            if (previousHash?.Length > 64) throw new OdinDatabaseValidationException($"Too long previousHash, was {previousHash.Length} (max 64)");
            if (identity == null) throw new OdinDatabaseValidationException("Cannot be null identity");
            if (identity?.Length < 3) throw new OdinDatabaseValidationException($"Too short identity, was {identity.Length} (min 3)");
            if (identity?.Length > 256) throw new OdinDatabaseValidationException($"Too long identity, was {identity.Length} (max 256)");
            if (signedPreviousHash == null) throw new OdinDatabaseValidationException("Cannot be null signedPreviousHash");
            if (signedPreviousHash?.Length < 16) throw new OdinDatabaseValidationException($"Too short signedPreviousHash, was {signedPreviousHash.Length} (min 16)");
            if (signedPreviousHash?.Length > 200) throw new OdinDatabaseValidationException($"Too long signedPreviousHash, was {signedPreviousHash.Length} (max 200)");
            if (algorithm == null) throw new OdinDatabaseValidationException("Cannot be null algorithm");
            if (algorithm?.Length < 1) throw new OdinDatabaseValidationException($"Too short algorithm, was {algorithm.Length} (min 1)");
            if (algorithm?.Length > 40) throw new OdinDatabaseValidationException($"Too long algorithm, was {algorithm.Length} (max 40)");
            if (publicKeyJwkBase64Url == null) throw new OdinDatabaseValidationException("Cannot be null publicKeyJwkBase64Url");
            if (publicKeyJwkBase64Url?.Length < 16) throw new OdinDatabaseValidationException($"Too short publicKeyJwkBase64Url, was {publicKeyJwkBase64Url.Length} (min 16)");
            if (publicKeyJwkBase64Url?.Length > 600) throw new OdinDatabaseValidationException($"Too long publicKeyJwkBase64Url, was {publicKeyJwkBase64Url.Length} (max 600)");
            if (recordHash == null) throw new OdinDatabaseValidationException("Cannot be null recordHash");
            if (recordHash?.Length < 16) throw new OdinDatabaseValidationException($"Too short recordHash, was {recordHash.Length} (min 16)");
            if (recordHash?.Length > 64) throw new OdinDatabaseValidationException($"Too long recordHash, was {recordHash.Length} (max 64)");
        }
    } // End of record KeyChainRecord

    public abstract class TableKeyChainCRUD : TableBase
    {
        private ScopedKeyChainConnectionFactory _scopedConnectionFactory { get; init; }
        public override string TableName { get; } = "KeyChain";

        public TableKeyChainCRUD(ScopedKeyChainConnectionFactory scopedConnectionFactory)
        {
            _scopedConnectionFactory = scopedConnectionFactory;
        }


        public override async Task EnsureTableExistsAsync(bool dropExisting = false)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            if (dropExisting)
                await SqlHelper.DeleteTableAsync(cn, "KeyChain");
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowid BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE KeyChain IS '{ \"Version\": 0 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS KeyChain( -- { \"Version\": 0 }\n"
                   +rowid
                   +"previousHash BYTEA NOT NULL UNIQUE, "
                   +"identity TEXT NOT NULL, "
                   +"timestamp BIGINT NOT NULL, "
                   +"signedPreviousHash BYTEA NOT NULL UNIQUE, "
                   +"algorithm TEXT NOT NULL, "
                   +"publicKeyJwkBase64Url TEXT NOT NULL UNIQUE, "
                   +"recordHash BYTEA NOT NULL UNIQUE "
                   +", UNIQUE(identity,publicKeyJwkBase64Url)"
                   +$"){wori};"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "KeyChain", createSql, commentSql);
        }

        public virtual async Task<int> InsertAsync(KeyChainRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO KeyChain (previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,recordHash) " +
                                           $"VALUES (@previousHash,@identity,@timestamp,@signedPreviousHash,@algorithm,@publicKeyJwkBase64Url,@recordHash)"+
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
                insertParam7.ParameterName = "@recordHash";
                insertCommand.Parameters.Add(insertParam7);
                insertParam1.Value = item.previousHash;
                insertParam2.Value = item.identity;
                insertParam3.Value = item.timestamp.milliseconds;
                insertParam4.Value = item.signedPreviousHash;
                insertParam5.Value = item.algorithm;
                insertParam6.Value = item.publicKeyJwkBase64Url;
                insertParam7.Value = item.recordHash;
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        public virtual async Task<bool> TryInsertAsync(KeyChainRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO KeyChain (previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,recordHash) " +
                                            $"VALUES (@previousHash,@identity,@timestamp,@signedPreviousHash,@algorithm,@publicKeyJwkBase64Url,@recordHash) " +
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
                insertParam7.ParameterName = "@recordHash";
                insertCommand.Parameters.Add(insertParam7);
                insertParam1.Value = item.previousHash;
                insertParam2.Value = item.identity;
                insertParam3.Value = item.timestamp.milliseconds;
                insertParam4.Value = item.signedPreviousHash;
                insertParam5.Value = item.algorithm;
                insertParam6.Value = item.publicKeyJwkBase64Url;
                insertParam7.Value = item.recordHash;
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                    return true;
                }
                return false;
            }
        }

        public virtual async Task<int> UpsertAsync(KeyChainRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                upsertCommand.CommandText = "INSERT INTO KeyChain (previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,recordHash) " +
                                            $"VALUES (@previousHash,@identity,@timestamp,@signedPreviousHash,@algorithm,@publicKeyJwkBase64Url,@recordHash)"+
                                            "ON CONFLICT (identity,publicKeyJwkBase64Url) DO UPDATE "+
                                            $"SET previousHash = @previousHash,timestamp = @timestamp,signedPreviousHash = @signedPreviousHash,algorithm = @algorithm,recordHash = @recordHash "+
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
                upsertParam7.ParameterName = "@recordHash";
                upsertCommand.Parameters.Add(upsertParam7);
                upsertParam1.Value = item.previousHash;
                upsertParam2.Value = item.identity;
                upsertParam3.Value = item.timestamp.milliseconds;
                upsertParam4.Value = item.signedPreviousHash;
                upsertParam5.Value = item.algorithm;
                upsertParam6.Value = item.publicKeyJwkBase64Url;
                upsertParam7.Value = item.recordHash;
                await using var rdr = await upsertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        public virtual async Task<int> UpdateAsync(KeyChainRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                updateCommand.CommandText = "UPDATE KeyChain " +
                                            $"SET previousHash = @previousHash,timestamp = @timestamp,signedPreviousHash = @signedPreviousHash,algorithm = @algorithm,recordHash = @recordHash "+
                                            "WHERE (identity = @identity AND publicKeyJwkBase64Url = @publicKeyJwkBase64Url) "+
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
                updateParam7.ParameterName = "@recordHash";
                updateCommand.Parameters.Add(updateParam7);
                updateParam1.Value = item.previousHash;
                updateParam2.Value = item.identity;
                updateParam3.Value = item.timestamp.milliseconds;
                updateParam4.Value = item.signedPreviousHash;
                updateParam5.Value = item.algorithm;
                updateParam6.Value = item.publicKeyJwkBase64Url;
                updateParam7.Value = item.recordHash;
                await using var rdr = await updateCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        public new async Task<int> GetCountAsync()
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getCountCommand = cn.CreateCommand();
            {
                 // TODO: this is SQLite specific
                getCountCommand.CommandText = "SELECT COUNT(*) FROM KeyChain;";
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
            sl.Add("previousHash");
            sl.Add("identity");
            sl.Add("timestamp");
            sl.Add("signedPreviousHash");
            sl.Add("algorithm");
            sl.Add("publicKeyJwkBase64Url");
            sl.Add("recordHash");
            return sl;
        }

        // SELECT rowId,previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,recordHash
        public KeyChainRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<KeyChainRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new KeyChainRecord();
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.previousHash = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (byte[])(rdr[1]);
            if (item.previousHash?.Length < 16)
                throw new Exception("Too little data in previousHash...");
            item.identity = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[2];
            item.timestamp = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[3]);
            item.signedPreviousHash = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (byte[])(rdr[4]);
            if (item.signedPreviousHash?.Length < 16)
                throw new Exception("Too little data in signedPreviousHash...");
            item.algorithm = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[5];
            item.publicKeyJwkBase64Url = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[6];
            item.recordHash = (rdr[7] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (byte[])(rdr[7]);
            if (item.recordHash?.Length < 16)
                throw new Exception("Too little data in recordHash...");
            return item;
       }

        public virtual async Task<int> DeleteAsync(string identity,string publicKeyJwkBase64Url)
        {
            if (identity == null) throw new OdinDatabaseValidationException("Cannot be null identity");
            if (identity?.Length < 3) throw new OdinDatabaseValidationException($"Too short identity, was {identity.Length} (min 3)");
            if (identity?.Length > 256) throw new OdinDatabaseValidationException($"Too long identity, was {identity.Length} (max 256)");
            if (publicKeyJwkBase64Url == null) throw new OdinDatabaseValidationException("Cannot be null publicKeyJwkBase64Url");
            if (publicKeyJwkBase64Url?.Length < 16) throw new OdinDatabaseValidationException($"Too short publicKeyJwkBase64Url, was {publicKeyJwkBase64Url.Length} (min 16)");
            if (publicKeyJwkBase64Url?.Length > 600) throw new OdinDatabaseValidationException($"Too long publicKeyJwkBase64Url, was {publicKeyJwkBase64Url.Length} (max 600)");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM KeyChain " +
                                             "WHERE identity = @identity AND publicKeyJwkBase64Url = @publicKeyJwkBase64Url";
                var delete0Param1 = delete0Command.CreateParameter();
                delete0Param1.DbType = DbType.String;
                delete0Param1.ParameterName = "@identity";
                delete0Command.Parameters.Add(delete0Param1);
                var delete0Param2 = delete0Command.CreateParameter();
                delete0Param2.DbType = DbType.String;
                delete0Param2.ParameterName = "@publicKeyJwkBase64Url";
                delete0Command.Parameters.Add(delete0Param2);

                delete0Param1.Value = identity;
                delete0Param2.Value = publicKeyJwkBase64Url;
                var count = await delete0Command.ExecuteNonQueryAsync();
                return count;
            }
        }

        public virtual async Task<KeyChainRecord> PopAsync(string identity,string publicKeyJwkBase64Url)
        {
            if (identity == null) throw new OdinDatabaseValidationException("Cannot be null identity");
            if (identity?.Length < 3) throw new OdinDatabaseValidationException($"Too short identity, was {identity.Length} (min 3)");
            if (identity?.Length > 256) throw new OdinDatabaseValidationException($"Too long identity, was {identity.Length} (max 256)");
            if (publicKeyJwkBase64Url == null) throw new OdinDatabaseValidationException("Cannot be null publicKeyJwkBase64Url");
            if (publicKeyJwkBase64Url?.Length < 16) throw new OdinDatabaseValidationException($"Too short publicKeyJwkBase64Url, was {publicKeyJwkBase64Url.Length} (min 16)");
            if (publicKeyJwkBase64Url?.Length > 600) throw new OdinDatabaseValidationException($"Too long publicKeyJwkBase64Url, was {publicKeyJwkBase64Url.Length} (max 600)");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var deleteCommand = cn.CreateCommand();
            {
                deleteCommand.CommandText = "DELETE FROM KeyChain " +
                                             "WHERE identity = @identity AND publicKeyJwkBase64Url = @publicKeyJwkBase64Url " + 
                                             "RETURNING rowId,previousHash,timestamp,signedPreviousHash,algorithm,recordHash";
                var deleteParam1 = deleteCommand.CreateParameter();
                deleteParam1.DbType = DbType.String;
                deleteParam1.ParameterName = "@identity";
                deleteCommand.Parameters.Add(deleteParam1);
                var deleteParam2 = deleteCommand.CreateParameter();
                deleteParam2.DbType = DbType.String;
                deleteParam2.ParameterName = "@publicKeyJwkBase64Url";
                deleteCommand.Parameters.Add(deleteParam2);

                deleteParam1.Value = identity;
                deleteParam2.Value = publicKeyJwkBase64Url;
                using (var rdr = await deleteCommand.ExecuteReaderAsync(CommandBehavior.SingleRow))
                {
                    if (await rdr.ReadAsync())
                    {
                       return ReadRecordFromReader0(rdr,identity,publicKeyJwkBase64Url);
                    }
                    else
                    {
                       return null;
                    }
                }
            }
        }

        public KeyChainRecord ReadRecordFromReader0(DbDataReader rdr,string identity,string publicKeyJwkBase64Url)
        {
            if (identity == null) throw new OdinDatabaseValidationException("Cannot be null identity");
            if (identity?.Length < 3) throw new OdinDatabaseValidationException($"Too short identity, was {identity.Length} (min 3)");
            if (identity?.Length > 256) throw new OdinDatabaseValidationException($"Too long identity, was {identity.Length} (max 256)");
            if (publicKeyJwkBase64Url == null) throw new OdinDatabaseValidationException("Cannot be null publicKeyJwkBase64Url");
            if (publicKeyJwkBase64Url?.Length < 16) throw new OdinDatabaseValidationException($"Too short publicKeyJwkBase64Url, was {publicKeyJwkBase64Url.Length} (min 16)");
            if (publicKeyJwkBase64Url?.Length > 600) throw new OdinDatabaseValidationException($"Too long publicKeyJwkBase64Url, was {publicKeyJwkBase64Url.Length} (max 600)");
            var result = new List<KeyChainRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new KeyChainRecord();
            item.identity = identity;
            item.publicKeyJwkBase64Url = publicKeyJwkBase64Url;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.previousHash = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (byte[])(rdr[1]);
            if (item.previousHash?.Length < 16)
                throw new Exception("Too little data in previousHash...");
            item.timestamp = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[2]);
            item.signedPreviousHash = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (byte[])(rdr[3]);
            if (item.signedPreviousHash?.Length < 16)
                throw new Exception("Too little data in signedPreviousHash...");
            item.algorithm = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[4];
            item.recordHash = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (byte[])(rdr[5]);
            if (item.recordHash?.Length < 16)
                throw new Exception("Too little data in recordHash...");
            return item;
       }

        public virtual async Task<KeyChainRecord> GetAsync(string identity,string publicKeyJwkBase64Url)
        {
            if (identity == null) throw new OdinDatabaseValidationException("Cannot be null identity");
            if (identity?.Length < 3) throw new OdinDatabaseValidationException($"Too short identity, was {identity.Length} (min 3)");
            if (identity?.Length > 256) throw new OdinDatabaseValidationException($"Too long identity, was {identity.Length} (max 256)");
            if (publicKeyJwkBase64Url == null) throw new OdinDatabaseValidationException("Cannot be null publicKeyJwkBase64Url");
            if (publicKeyJwkBase64Url?.Length < 16) throw new OdinDatabaseValidationException($"Too short publicKeyJwkBase64Url, was {publicKeyJwkBase64Url.Length} (min 16)");
            if (publicKeyJwkBase64Url?.Length > 600) throw new OdinDatabaseValidationException($"Too long publicKeyJwkBase64Url, was {publicKeyJwkBase64Url.Length} (max 600)");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT rowId,previousHash,timestamp,signedPreviousHash,algorithm,recordHash FROM KeyChain " +
                                             "WHERE identity = @identity AND publicKeyJwkBase64Url = @publicKeyJwkBase64Url LIMIT 1;"+
                                             ";";
                var get0Param1 = get0Command.CreateParameter();
                get0Param1.DbType = DbType.String;
                get0Param1.ParameterName = "@identity";
                get0Command.Parameters.Add(get0Param1);
                var get0Param2 = get0Command.CreateParameter();
                get0Param2.DbType = DbType.String;
                get0Param2.ParameterName = "@publicKeyJwkBase64Url";
                get0Command.Parameters.Add(get0Param2);

                get0Param1.Value = identity;
                get0Param2.Value = publicKeyJwkBase64Url;
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr,identity,publicKeyJwkBase64Url);
                        return r;
                    } // using
                } //
            } // using
        }

    }
}
