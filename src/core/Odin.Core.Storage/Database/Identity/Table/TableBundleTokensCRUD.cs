using System;
using System.Data;
using System.Data.Common;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Factory;
using Odin.Core.Storage.Exceptions;
using Odin.Core.Storage.Database.Identity.Connection;

#nullable disable

// THIS FILE IS AUTO GENERATED - DO NOT EDIT

namespace Odin.Core.Storage.Database.Identity.Table
{
    public record BundleTokensRecord
    {
        public Int64 rowId { get; set; }
        public Guid identityId { get; set; }
        public Guid tokenId { get; set; }
        public Guid primaryAppId { get; set; }
        public string friendlyName { get; set; }
        public string accessRegistrationJson { get; set; }
        public UnixTimeUtc expiresAt { get; set; }
        public UnixTimeUtc created { get; set; }
        public UnixTimeUtc modified { get; set; }
        public void Validate()
        {
            identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            tokenId.AssertGuidNotEmpty("Guid parameter tokenId cannot be set to Empty GUID.");
            primaryAppId.AssertGuidNotEmpty("Guid parameter primaryAppId cannot be set to Empty GUID.");
            if (friendlyName == null) throw new OdinDatabaseValidationException("Cannot be null friendlyName");
            if (friendlyName?.Length < 0) throw new OdinDatabaseValidationException($"Too short friendlyName, was {friendlyName.Length} (min 0)");
            if (friendlyName?.Length > 1024) throw new OdinDatabaseValidationException($"Too long friendlyName, was {friendlyName.Length} (max 1024)");
            if (accessRegistrationJson == null) throw new OdinDatabaseValidationException("Cannot be null accessRegistrationJson");
            if (accessRegistrationJson?.Length < 0) throw new OdinDatabaseValidationException($"Too short accessRegistrationJson, was {accessRegistrationJson.Length} (min 0)");
            if (accessRegistrationJson?.Length > 16384) throw new OdinDatabaseValidationException($"Too long accessRegistrationJson, was {accessRegistrationJson.Length} (max 16384)");
        }
    } // End of record BundleTokensRecord

    public abstract class TableBundleTokensCRUD : TableBase
    {
        private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory;
        public override string TableName { get; } = "BundleTokens";

        protected TableBundleTokensCRUD(ScopedIdentityConnectionFactory scopedConnectionFactory)
        {
            _scopedConnectionFactory = scopedConnectionFactory;
        }


       /*
        * This method is no longer used.
        * It is kept here, commented-out, so you can see how the table is created without having to locate its latest migration.
        *
        public override async Task EnsureTableExistsAsync(bool dropExisting = false)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            if (dropExisting)
                await SqlHelper.DeleteTableAsync(cn, "BundleTokens");
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE BundleTokens IS '{ \"Version\": 0 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS BundleTokens( -- { \"Version\": 0 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"tokenId BYTEA NOT NULL, "
                   +"primaryAppId BYTEA NOT NULL, "
                   +"friendlyName TEXT NOT NULL, "
                   +"accessRegistrationJson TEXT NOT NULL, "
                   +"expiresAt BIGINT NOT NULL, "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +", UNIQUE(identityId,tokenId)"
                   +$"){wori};"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "BundleTokens", createSql, commentSql);
        }
       */

        protected virtual async Task<int> InsertAsync(BundleTokensRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr = insertCommand.SqlNow();
                insertCommand.CommandText = "INSERT INTO BundleTokens (identityId,tokenId,primaryAppId,friendlyName,accessRegistrationJson,expiresAt,created,modified) " +
                                           $"VALUES (@identityId,@tokenId,@primaryAppId,@friendlyName,@accessRegistrationJson,@expiresAt,{sqlNowStr},{sqlNowStr})"+
                                            "RETURNING created,modified,rowId;";
                insertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                insertCommand.AddParameter("@tokenId", DbType.Binary, item.tokenId);
                insertCommand.AddParameter("@primaryAppId", DbType.Binary, item.primaryAppId);
                insertCommand.AddParameter("@friendlyName", DbType.String, item.friendlyName);
                insertCommand.AddParameter("@accessRegistrationJson", DbType.String, item.accessRegistrationJson);
                insertCommand.AddParameter("@expiresAt", DbType.Int64, item.expiresAt.milliseconds);
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    item.created = new UnixTimeUtc(created);
                    long modified = (long) rdr[1];
                    item.modified = new UnixTimeUtc((long)modified);
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<bool> TryInsertAsync(BundleTokensRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr = insertCommand.SqlNow();
                insertCommand.CommandText = "INSERT INTO BundleTokens (identityId,tokenId,primaryAppId,friendlyName,accessRegistrationJson,expiresAt,created,modified) " +
                                            $"VALUES (@identityId,@tokenId,@primaryAppId,@friendlyName,@accessRegistrationJson,@expiresAt,{sqlNowStr},{sqlNowStr}) " +
                                            "ON CONFLICT DO NOTHING "+
                                            "RETURNING created,modified,rowId;";
                insertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                insertCommand.AddParameter("@tokenId", DbType.Binary, item.tokenId);
                insertCommand.AddParameter("@primaryAppId", DbType.Binary, item.primaryAppId);
                insertCommand.AddParameter("@friendlyName", DbType.String, item.friendlyName);
                insertCommand.AddParameter("@accessRegistrationJson", DbType.String, item.accessRegistrationJson);
                insertCommand.AddParameter("@expiresAt", DbType.Int64, item.expiresAt.milliseconds);
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    item.created = new UnixTimeUtc(created);
                    long modified = (long) rdr[1];
                    item.modified = new UnixTimeUtc((long)modified);
                    item.rowId = (long) rdr[2];
                    return true;
                }
                return false;
            }
        }

        protected virtual async Task<int> UpsertAsync(BundleTokensRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                string sqlNowStr = upsertCommand.SqlNow();
                upsertCommand.CommandText = "INSERT INTO BundleTokens (identityId,tokenId,primaryAppId,friendlyName,accessRegistrationJson,expiresAt,created,modified) " +
                                            $"VALUES (@identityId,@tokenId,@primaryAppId,@friendlyName,@accessRegistrationJson,@expiresAt,{sqlNowStr},{sqlNowStr})"+
                                            "ON CONFLICT (identityId,tokenId) DO UPDATE "+
                                            $"SET primaryAppId = @primaryAppId,friendlyName = @friendlyName,accessRegistrationJson = @accessRegistrationJson,expiresAt = @expiresAt,modified = {upsertCommand.SqlMax()}(BundleTokens.modified+1,{sqlNowStr}) "+
                                            "RETURNING created,modified,rowId;";
                upsertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                upsertCommand.AddParameter("@tokenId", DbType.Binary, item.tokenId);
                upsertCommand.AddParameter("@primaryAppId", DbType.Binary, item.primaryAppId);
                upsertCommand.AddParameter("@friendlyName", DbType.String, item.friendlyName);
                upsertCommand.AddParameter("@accessRegistrationJson", DbType.String, item.accessRegistrationJson);
                upsertCommand.AddParameter("@expiresAt", DbType.Int64, item.expiresAt.milliseconds);
                await using var rdr = await upsertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    item.created = new UnixTimeUtc(created);
                    long modified = (long) rdr[1];
                    item.modified = new UnixTimeUtc((long)modified);
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<int> UpdateAsync(BundleTokensRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                string sqlNowStr = updateCommand.SqlNow();
                updateCommand.CommandText = "UPDATE BundleTokens " +
                                            $"SET primaryAppId = @primaryAppId,friendlyName = @friendlyName,accessRegistrationJson = @accessRegistrationJson,expiresAt = @expiresAt,modified = {updateCommand.SqlMax()}(BundleTokens.modified+1,{sqlNowStr}) "+
                                            "WHERE (identityId = @identityId AND tokenId = @tokenId) "+
                                            "RETURNING created,modified,rowId;";
                updateCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                updateCommand.AddParameter("@tokenId", DbType.Binary, item.tokenId);
                updateCommand.AddParameter("@primaryAppId", DbType.Binary, item.primaryAppId);
                updateCommand.AddParameter("@friendlyName", DbType.String, item.friendlyName);
                updateCommand.AddParameter("@accessRegistrationJson", DbType.String, item.accessRegistrationJson);
                updateCommand.AddParameter("@expiresAt", DbType.Int64, item.expiresAt.milliseconds);
                await using var rdr = await updateCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    item.created = new UnixTimeUtc(created);
                    long modified = (long) rdr[1];
                    item.modified = new UnixTimeUtc((long)modified);
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        protected new async Task<int> GetCountAsync()
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getCountCommand = cn.CreateCommand();
            {
                 // TODO: this is SQLite specific
                getCountCommand.CommandText = "SELECT COUNT(*) FROM BundleTokens;";
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
            sl.Add("identityId");
            sl.Add("tokenId");
            sl.Add("primaryAppId");
            sl.Add("friendlyName");
            sl.Add("accessRegistrationJson");
            sl.Add("expiresAt");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        // SELECT rowId,identityId,tokenId,primaryAppId,friendlyName,accessRegistrationJson,expiresAt,created,modified
        protected BundleTokensRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<BundleTokensRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new BundleTokensRecord();
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.identityId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.tokenId = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[2]);
            item.primaryAppId = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[3]);
            item.friendlyName = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[4];
            item.accessRegistrationJson = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[5];
            item.expiresAt = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[6]);
            item.created = (rdr[7] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[7]);
            item.modified = (rdr[8] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[8]);
            return item;
       }

        internal virtual async Task ExportRowsAsync(Guid identityId, Func<BundleTokensRecord, Task> onRow)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var exportCommand = cn.CreateCommand();
            {
                exportCommand.CommandText = "SELECT rowId,identityId,tokenId,primaryAppId,friendlyName,accessRegistrationJson,expiresAt,created,modified FROM BundleTokens " +
                                            "WHERE identityId = @identityId ORDER BY rowId ASC;";
                exportCommand.AddParameter("@identityId", DbType.Binary, identityId);
                await using var rdr = await exportCommand.ExecuteReaderAsync(CommandBehavior.Default);
                while (await rdr.ReadAsync())
                {
                    await onRow(ReadRecordFromReaderAll(rdr));
                }
            }
        }

        internal virtual async Task<int> ImportRowAsync(BundleTokensRecord item)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var importCommand = cn.CreateCommand();
            {
                importCommand.CommandText = "INSERT INTO BundleTokens (identityId,tokenId,primaryAppId,friendlyName,accessRegistrationJson,expiresAt,created,modified) " +
                                            "VALUES (@identityId,@tokenId,@primaryAppId,@friendlyName,@accessRegistrationJson,@expiresAt,@created,@modified);";
                importCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                importCommand.AddParameter("@tokenId", DbType.Binary, item.tokenId);
                importCommand.AddParameter("@primaryAppId", DbType.Binary, item.primaryAppId);
                importCommand.AddParameter("@friendlyName", DbType.String, item.friendlyName);
                importCommand.AddParameter("@accessRegistrationJson", DbType.String, item.accessRegistrationJson);
                importCommand.AddParameter("@expiresAt", DbType.Int64, item.expiresAt.milliseconds);
                importCommand.AddParameter("@created", DbType.Int64, item.created.milliseconds);
                importCommand.AddParameter("@modified", DbType.Int64, item.modified.milliseconds);
                return await importCommand.ExecuteNonQueryAsync();
            }
        }

        protected virtual async Task<int> DeleteAsync(Guid identityId,Guid tokenId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM BundleTokens " +
                                             "WHERE identityId = @identityId AND tokenId = @tokenId";

                delete0Command.AddParameter("@identityId", DbType.Binary, identityId);
                delete0Command.AddParameter("@tokenId", DbType.Binary, tokenId);
                var count = await delete0Command.ExecuteNonQueryAsync();
                return count;
            }
        }

        protected virtual async Task<BundleTokensRecord> PopAsync(Guid identityId,Guid tokenId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var deleteCommand = cn.CreateCommand();
            {
                deleteCommand.CommandText = "DELETE FROM BundleTokens " +
                                             "WHERE identityId = @identityId AND tokenId = @tokenId " + 
                                             "RETURNING rowId,primaryAppId,friendlyName,accessRegistrationJson,expiresAt,created,modified";

                deleteCommand.AddParameter("@identityId", DbType.Binary, identityId);
                deleteCommand.AddParameter("@tokenId", DbType.Binary, tokenId);
                using (var rdr = await deleteCommand.ExecuteReaderAsync(CommandBehavior.SingleRow))
                {
                    if (await rdr.ReadAsync())
                    {
                       return ReadRecordFromReader0(rdr,identityId,tokenId);
                    }
                    else
                    {
                       return null;
                    }
                }
            }
        }

        protected BundleTokensRecord ReadRecordFromReader0(DbDataReader rdr,Guid identityId,Guid tokenId)
        {
            var result = new List<BundleTokensRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new BundleTokensRecord();
            item.identityId = identityId;
            item.tokenId = tokenId;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.primaryAppId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.friendlyName = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[2];
            item.accessRegistrationJson = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[3];
            item.expiresAt = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[4]);
            item.created = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[5]);
            item.modified = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[6]);
            return item;
       }

        protected virtual async Task<BundleTokensRecord> GetAsync(Guid identityId,Guid tokenId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT rowId,primaryAppId,friendlyName,accessRegistrationJson,expiresAt,created,modified FROM BundleTokens " +
                                             "WHERE identityId = @identityId AND tokenId = @tokenId LIMIT 1 "+
                                             ";";

                get0Command.AddParameter("@identityId", DbType.Binary, identityId);
                get0Command.AddParameter("@tokenId", DbType.Binary, tokenId);
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr,identityId,tokenId);
                        return r;
                    } // using
                } //
            } // using
        }

        protected BundleTokensRecord ReadRecordFromReader1(DbDataReader rdr,Guid identityId)
        {
            var result = new List<BundleTokensRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new BundleTokensRecord();
            item.identityId = identityId;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.tokenId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.primaryAppId = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[2]);
            item.friendlyName = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[3];
            item.accessRegistrationJson = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[4];
            item.expiresAt = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[5]);
            item.created = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[6]);
            item.modified = (rdr[7] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[7]);
            return item;
       }

        protected virtual async Task<List<BundleTokensRecord>> GetAllAsync(Guid identityId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get1Command = cn.CreateCommand();
            {
                get1Command.CommandText = "SELECT rowId,tokenId,primaryAppId,friendlyName,accessRegistrationJson,expiresAt,created,modified FROM BundleTokens " +
                                             "WHERE identityId = @identityId "+
                                             ";";

                get1Command.AddParameter("@identityId", DbType.Binary, identityId);
                {
                    using (var rdr = await get1Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return new List<BundleTokensRecord>();
                        }
                        var result = new List<BundleTokensRecord>();
                        while (true)
                        {
                            result.Add(ReadRecordFromReader1(rdr,identityId));
                            if (!await rdr.ReadAsync())
                                break;
                        }
                        return result;
                    } // using
                } //
            } // using
        }

        protected virtual async Task<(List<BundleTokensRecord>, Int64? nextCursor)> PagingByRowIdAsync(int count, Guid identityId, Int64? inCursor)
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
                getPaging0Command.CommandText = "SELECT rowId,identityId,tokenId,primaryAppId,friendlyName,accessRegistrationJson,expiresAt,created,modified FROM BundleTokens " +
                                            "WHERE (identityId = @identityId) AND rowId > @rowId  ORDER BY rowId ASC  LIMIT @count;";

                getPaging0Command.AddParameter("@rowId", DbType.Int64, inCursor);
                getPaging0Command.AddParameter("@count", DbType.Int64, count+1);
                getPaging0Command.AddParameter("@identityId", DbType.Binary, identityId);

                {
                    await using (var rdr = await getPaging0Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        var result = new List<BundleTokensRecord>();
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
