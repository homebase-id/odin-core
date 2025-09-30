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

namespace Odin.Core.Storage.Database.Notary.Table
{
    public record NotaryChainRecord
    {
        public Int64 rowId { get; set; }
        public byte[] previousHash { get; set; }
        public string identity { get; set; }
        public UnixTimeUtc timestamp { get; set; }
        public byte[] signedPreviousHash { get; set; }
        public string algorithm { get; set; }
        public string publicKeyJwkBase64Url { get; set; }
        public byte[] notarySignature { get; set; }
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
            if (notarySignature == null) throw new OdinDatabaseValidationException("Cannot be null notarySignature");
            if (notarySignature?.Length < 16) throw new OdinDatabaseValidationException($"Too short notarySignature, was {notarySignature.Length} (min 16)");
            if (notarySignature?.Length > 200) throw new OdinDatabaseValidationException($"Too long notarySignature, was {notarySignature.Length} (max 200)");
            if (recordHash == null) throw new OdinDatabaseValidationException("Cannot be null recordHash");
            if (recordHash?.Length < 16) throw new OdinDatabaseValidationException($"Too short recordHash, was {recordHash.Length} (min 16)");
            if (recordHash?.Length > 64) throw new OdinDatabaseValidationException($"Too long recordHash, was {recordHash.Length} (max 64)");
        }
    } // End of record NotaryChainRecord

    public abstract class TableNotaryChainCRUD : TableBase
    {
        private ScopedNotaryConnectionFactory _scopedConnectionFactory { get; init; }
        public override string TableName { get; } = "NotaryChain";

        public TableNotaryChainCRUD(ScopedNotaryConnectionFactory scopedConnectionFactory)
        {
            _scopedConnectionFactory = scopedConnectionFactory;
        }


        public override async Task EnsureTableExistsAsync(bool dropExisting = false)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            if (dropExisting)
                await SqlHelper.DeleteTableAsync(cn, "NotaryChain");
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE NotaryChain IS '{ \"Version\": 0 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS NotaryChain( -- { \"Version\": 0 }\n"
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
            await SqlHelper.CreateTableWithCommentAsync(cn, "NotaryChain", createSql, commentSql);
        }

        public virtual async Task<int> InsertAsync(NotaryChainRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO NotaryChain (previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,notarySignature,recordHash) " +
                                           $"VALUES (@previousHash,@identity,@timestamp,@signedPreviousHash,@algorithm,@publicKeyJwkBase64Url,@notarySignature,@recordHash)"+
                                            "RETURNING -1,-1,rowId;";
                insertCommand.AddParameter("@previousHash", DbType.Binary, item.previousHash);
                insertCommand.AddParameter("@identity", DbType.String, item.identity);
                insertCommand.AddParameter("@timestamp", DbType.Int64, item.timestamp.milliseconds);
                insertCommand.AddParameter("@signedPreviousHash", DbType.Binary, item.signedPreviousHash);
                insertCommand.AddParameter("@algorithm", DbType.String, item.algorithm);
                insertCommand.AddParameter("@publicKeyJwkBase64Url", DbType.String, item.publicKeyJwkBase64Url);
                insertCommand.AddParameter("@notarySignature", DbType.Binary, item.notarySignature);
                insertCommand.AddParameter("@recordHash", DbType.Binary, item.recordHash);
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        public virtual async Task<bool> TryInsertAsync(NotaryChainRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO NotaryChain (previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,notarySignature,recordHash) " +
                                            $"VALUES (@previousHash,@identity,@timestamp,@signedPreviousHash,@algorithm,@publicKeyJwkBase64Url,@notarySignature,@recordHash) " +
                                            "ON CONFLICT DO NOTHING "+
                                            "RETURNING -1,-1,rowId;";
                insertCommand.AddParameter("@previousHash", DbType.Binary, item.previousHash);
                insertCommand.AddParameter("@identity", DbType.String, item.identity);
                insertCommand.AddParameter("@timestamp", DbType.Int64, item.timestamp.milliseconds);
                insertCommand.AddParameter("@signedPreviousHash", DbType.Binary, item.signedPreviousHash);
                insertCommand.AddParameter("@algorithm", DbType.String, item.algorithm);
                insertCommand.AddParameter("@publicKeyJwkBase64Url", DbType.String, item.publicKeyJwkBase64Url);
                insertCommand.AddParameter("@notarySignature", DbType.Binary, item.notarySignature);
                insertCommand.AddParameter("@recordHash", DbType.Binary, item.recordHash);
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                    return true;
                }
                return false;
            }
        }

        public virtual async Task<int> UpsertAsync(NotaryChainRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                upsertCommand.CommandText = "INSERT INTO NotaryChain (previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,notarySignature,recordHash) " +
                                            $"VALUES (@previousHash,@identity,@timestamp,@signedPreviousHash,@algorithm,@publicKeyJwkBase64Url,@notarySignature,@recordHash)"+
                                            "ON CONFLICT (notarySignature) DO UPDATE "+
                                            $"SET previousHash = @previousHash,identity = @identity,timestamp = @timestamp,signedPreviousHash = @signedPreviousHash,algorithm = @algorithm,publicKeyJwkBase64Url = @publicKeyJwkBase64Url,recordHash = @recordHash "+
                                            "RETURNING -1,-1,rowId;";
                upsertCommand.AddParameter("@previousHash", DbType.Binary, item.previousHash);
                upsertCommand.AddParameter("@identity", DbType.String, item.identity);
                upsertCommand.AddParameter("@timestamp", DbType.Int64, item.timestamp.milliseconds);
                upsertCommand.AddParameter("@signedPreviousHash", DbType.Binary, item.signedPreviousHash);
                upsertCommand.AddParameter("@algorithm", DbType.String, item.algorithm);
                upsertCommand.AddParameter("@publicKeyJwkBase64Url", DbType.String, item.publicKeyJwkBase64Url);
                upsertCommand.AddParameter("@notarySignature", DbType.Binary, item.notarySignature);
                upsertCommand.AddParameter("@recordHash", DbType.Binary, item.recordHash);
                await using var rdr = await upsertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        public virtual async Task<int> UpdateAsync(NotaryChainRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                updateCommand.CommandText = "UPDATE NotaryChain " +
                                            $"SET previousHash = @previousHash,identity = @identity,timestamp = @timestamp,signedPreviousHash = @signedPreviousHash,algorithm = @algorithm,publicKeyJwkBase64Url = @publicKeyJwkBase64Url,recordHash = @recordHash "+
                                            "WHERE (notarySignature = @notarySignature) "+
                                            "RETURNING -1,-1,rowId;";
                updateCommand.AddParameter("@previousHash", DbType.Binary, item.previousHash);
                updateCommand.AddParameter("@identity", DbType.String, item.identity);
                updateCommand.AddParameter("@timestamp", DbType.Int64, item.timestamp.milliseconds);
                updateCommand.AddParameter("@signedPreviousHash", DbType.Binary, item.signedPreviousHash);
                updateCommand.AddParameter("@algorithm", DbType.String, item.algorithm);
                updateCommand.AddParameter("@publicKeyJwkBase64Url", DbType.String, item.publicKeyJwkBase64Url);
                updateCommand.AddParameter("@notarySignature", DbType.Binary, item.notarySignature);
                updateCommand.AddParameter("@recordHash", DbType.Binary, item.recordHash);
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
                getCountCommand.CommandText = "SELECT COUNT(*) FROM NotaryChain;";
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
            item.notarySignature = (rdr[7] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (byte[])(rdr[7]);
            if (item.notarySignature?.Length < 16)
                throw new Exception("Too little data in notarySignature...");
            item.recordHash = (rdr[8] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (byte[])(rdr[8]);
            if (item.recordHash?.Length < 16)
                throw new Exception("Too little data in recordHash...");
            return item;
       }

        public virtual async Task<int> DeleteAsync(byte[] notarySignature)
        {
            if (notarySignature == null) throw new OdinDatabaseValidationException("Cannot be null notarySignature");
            if (notarySignature?.Length < 16) throw new OdinDatabaseValidationException($"Too short notarySignature, was {notarySignature.Length} (min 16)");
            if (notarySignature?.Length > 200) throw new OdinDatabaseValidationException($"Too long notarySignature, was {notarySignature.Length} (max 200)");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM NotaryChain " +
                                             "WHERE notarySignature = @notarySignature";

                delete0Command.AddParameter("@notarySignature", DbType.Binary, notarySignature);
                var count = await delete0Command.ExecuteNonQueryAsync();
                return count;
            }
        }

        public virtual async Task<NotaryChainRecord> PopAsync(byte[] notarySignature)
        {
            if (notarySignature == null) throw new OdinDatabaseValidationException("Cannot be null notarySignature");
            if (notarySignature?.Length < 16) throw new OdinDatabaseValidationException($"Too short notarySignature, was {notarySignature.Length} (min 16)");
            if (notarySignature?.Length > 200) throw new OdinDatabaseValidationException($"Too long notarySignature, was {notarySignature.Length} (max 200)");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var deleteCommand = cn.CreateCommand();
            {
                deleteCommand.CommandText = "DELETE FROM NotaryChain " +
                                             "WHERE notarySignature = @notarySignature " + 
                                             "RETURNING rowId,previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,recordHash";

                deleteCommand.AddParameter("@notarySignature", DbType.Binary, notarySignature);
                using (var rdr = await deleteCommand.ExecuteReaderAsync(CommandBehavior.SingleRow))
                {
                    if (await rdr.ReadAsync())
                    {
                       return ReadRecordFromReader0(rdr,notarySignature);
                    }
                    else
                    {
                       return null;
                    }
                }
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

        public virtual async Task<NotaryChainRecord> GetAsync(byte[] notarySignature)
        {
            if (notarySignature == null) throw new OdinDatabaseValidationException("Cannot be null notarySignature");
            if (notarySignature?.Length < 16) throw new OdinDatabaseValidationException($"Too short notarySignature, was {notarySignature.Length} (min 16)");
            if (notarySignature?.Length > 200) throw new OdinDatabaseValidationException($"Too long notarySignature, was {notarySignature.Length} (max 200)");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT rowId,previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,recordHash FROM NotaryChain " +
                                             "WHERE notarySignature = @notarySignature LIMIT 1 "+
                                             ";";

                get0Command.AddParameter("@notarySignature", DbType.Binary, notarySignature);
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr,notarySignature);
                        return r;
                    } // using
                } //
            } // using
        }

    }
}
