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
    public record BundleTokenAppsRecord
    {
        public Int64 rowId { get; set; }
        public Guid identityId { get; set; }
        public Guid tokenId { get; set; }
        public Guid appId { get; set; }
        public string encryptedKeyStoreKeyJson { get; set; }
        public UnixTimeUtc created { get; set; }
        public UnixTimeUtc modified { get; set; }
        public void Validate()
        {
            identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            tokenId.AssertGuidNotEmpty("Guid parameter tokenId cannot be set to Empty GUID.");
            appId.AssertGuidNotEmpty("Guid parameter appId cannot be set to Empty GUID.");
            if (encryptedKeyStoreKeyJson == null) throw new OdinDatabaseValidationException("Cannot be null encryptedKeyStoreKeyJson");
            if (encryptedKeyStoreKeyJson?.Length < 0) throw new OdinDatabaseValidationException($"Too short encryptedKeyStoreKeyJson, was {encryptedKeyStoreKeyJson.Length} (min 0)");
            if (encryptedKeyStoreKeyJson?.Length > 4096) throw new OdinDatabaseValidationException($"Too long encryptedKeyStoreKeyJson, was {encryptedKeyStoreKeyJson.Length} (max 4096)");
        }
    } // End of record BundleTokenAppsRecord

    public abstract class TableBundleTokenAppsCRUD : TableBase
    {
        private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory;
        public override string TableName { get; } = "BundleTokenApps";

        protected TableBundleTokenAppsCRUD(ScopedIdentityConnectionFactory scopedConnectionFactory)
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
                await SqlHelper.DeleteTableAsync(cn, "BundleTokenApps");
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE BundleTokenApps IS '{ \"Version\": 0 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS BundleTokenApps( -- { \"Version\": 0 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"tokenId BYTEA NOT NULL, "
                   +"appId BYTEA NOT NULL, "
                   +"encryptedKeyStoreKeyJson TEXT NOT NULL, "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +", UNIQUE(identityId,tokenId,appId)"
                   +$"){wori};"
                   +"CREATE INDEX IF NOT EXISTS Idx0BundleTokenApps ON BundleTokenApps(identityId,appId);"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "BundleTokenApps", createSql, commentSql);
        }
       */

        protected virtual async Task<int> InsertAsync(BundleTokenAppsRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr = insertCommand.SqlNow();
                insertCommand.CommandText = "INSERT INTO BundleTokenApps (identityId,tokenId,appId,encryptedKeyStoreKeyJson,created,modified) " +
                                           $"VALUES (@identityId,@tokenId,@appId,@encryptedKeyStoreKeyJson,{sqlNowStr},{sqlNowStr})"+
                                            "RETURNING created,modified,rowId;";
                insertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                insertCommand.AddParameter("@tokenId", DbType.Binary, item.tokenId);
                insertCommand.AddParameter("@appId", DbType.Binary, item.appId);
                insertCommand.AddParameter("@encryptedKeyStoreKeyJson", DbType.String, item.encryptedKeyStoreKeyJson);
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

        protected virtual async Task<bool> TryInsertAsync(BundleTokenAppsRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr = insertCommand.SqlNow();
                insertCommand.CommandText = "INSERT INTO BundleTokenApps (identityId,tokenId,appId,encryptedKeyStoreKeyJson,created,modified) " +
                                            $"VALUES (@identityId,@tokenId,@appId,@encryptedKeyStoreKeyJson,{sqlNowStr},{sqlNowStr}) " +
                                            "ON CONFLICT DO NOTHING "+
                                            "RETURNING created,modified,rowId;";
                insertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                insertCommand.AddParameter("@tokenId", DbType.Binary, item.tokenId);
                insertCommand.AddParameter("@appId", DbType.Binary, item.appId);
                insertCommand.AddParameter("@encryptedKeyStoreKeyJson", DbType.String, item.encryptedKeyStoreKeyJson);
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

        protected virtual async Task<int> UpsertAsync(BundleTokenAppsRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                string sqlNowStr = upsertCommand.SqlNow();
                upsertCommand.CommandText = "INSERT INTO BundleTokenApps (identityId,tokenId,appId,encryptedKeyStoreKeyJson,created,modified) " +
                                            $"VALUES (@identityId,@tokenId,@appId,@encryptedKeyStoreKeyJson,{sqlNowStr},{sqlNowStr})"+
                                            "ON CONFLICT (identityId,tokenId,appId) DO UPDATE "+
                                            $"SET encryptedKeyStoreKeyJson = @encryptedKeyStoreKeyJson,modified = {upsertCommand.SqlMax()}(BundleTokenApps.modified+1,{sqlNowStr}) "+
                                            "RETURNING created,modified,rowId;";
                upsertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                upsertCommand.AddParameter("@tokenId", DbType.Binary, item.tokenId);
                upsertCommand.AddParameter("@appId", DbType.Binary, item.appId);
                upsertCommand.AddParameter("@encryptedKeyStoreKeyJson", DbType.String, item.encryptedKeyStoreKeyJson);
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

        protected virtual async Task<int> UpdateAsync(BundleTokenAppsRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                string sqlNowStr = updateCommand.SqlNow();
                updateCommand.CommandText = "UPDATE BundleTokenApps " +
                                            $"SET encryptedKeyStoreKeyJson = @encryptedKeyStoreKeyJson,modified = {updateCommand.SqlMax()}(BundleTokenApps.modified+1,{sqlNowStr}) "+
                                            "WHERE (identityId = @identityId AND tokenId = @tokenId AND appId = @appId) "+
                                            "RETURNING created,modified,rowId;";
                updateCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                updateCommand.AddParameter("@tokenId", DbType.Binary, item.tokenId);
                updateCommand.AddParameter("@appId", DbType.Binary, item.appId);
                updateCommand.AddParameter("@encryptedKeyStoreKeyJson", DbType.String, item.encryptedKeyStoreKeyJson);
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
                getCountCommand.CommandText = "SELECT COUNT(*) FROM BundleTokenApps;";
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
            sl.Add("appId");
            sl.Add("encryptedKeyStoreKeyJson");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        // SELECT rowId,identityId,tokenId,appId,encryptedKeyStoreKeyJson,created,modified
        protected BundleTokenAppsRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<BundleTokenAppsRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new BundleTokenAppsRecord();
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.identityId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.tokenId = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[2]);
            item.appId = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[3]);
            item.encryptedKeyStoreKeyJson = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[4];
            item.created = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[5]);
            item.modified = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[6]);
            return item;
       }

        internal virtual async Task ExportRowsAsync(Guid identityId, Func<BundleTokenAppsRecord, Task> onRow)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var exportCommand = cn.CreateCommand();
            {
                exportCommand.CommandText = "SELECT rowId,identityId,tokenId,appId,encryptedKeyStoreKeyJson,created,modified FROM BundleTokenApps " +
                                            "WHERE identityId = @identityId ORDER BY rowId ASC;";
                exportCommand.AddParameter("@identityId", DbType.Binary, identityId);
                await using var rdr = await exportCommand.ExecuteReaderAsync(CommandBehavior.Default);
                while (await rdr.ReadAsync())
                {
                    await onRow(ReadRecordFromReaderAll(rdr));
                }
            }
        }

        internal virtual async Task<int> ImportRowAsync(BundleTokenAppsRecord item)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var importCommand = cn.CreateCommand();
            {
                importCommand.CommandText = "INSERT INTO BundleTokenApps (identityId,tokenId,appId,encryptedKeyStoreKeyJson,created,modified) " +
                                            "VALUES (@identityId,@tokenId,@appId,@encryptedKeyStoreKeyJson,@created,@modified);";
                importCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                importCommand.AddParameter("@tokenId", DbType.Binary, item.tokenId);
                importCommand.AddParameter("@appId", DbType.Binary, item.appId);
                importCommand.AddParameter("@encryptedKeyStoreKeyJson", DbType.String, item.encryptedKeyStoreKeyJson);
                importCommand.AddParameter("@created", DbType.Int64, item.created.milliseconds);
                importCommand.AddParameter("@modified", DbType.Int64, item.modified.milliseconds);
                return await importCommand.ExecuteNonQueryAsync();
            }
        }

        protected virtual async Task<int> DeleteByTokenIdAsync(Guid identityId,Guid tokenId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM BundleTokenApps " +
                                             "WHERE identityId = @identityId AND tokenId = @tokenId";

                delete0Command.AddParameter("@identityId", DbType.Binary, identityId);
                delete0Command.AddParameter("@tokenId", DbType.Binary, tokenId);
                var count = await delete0Command.ExecuteNonQueryAsync();
                return count;
            }
        }

        protected virtual async Task<int> DeleteAsync(Guid identityId,Guid tokenId,Guid appId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete1Command = cn.CreateCommand();
            {
                delete1Command.CommandText = "DELETE FROM BundleTokenApps " +
                                             "WHERE identityId = @identityId AND tokenId = @tokenId AND appId = @appId";

                delete1Command.AddParameter("@identityId", DbType.Binary, identityId);
                delete1Command.AddParameter("@tokenId", DbType.Binary, tokenId);
                delete1Command.AddParameter("@appId", DbType.Binary, appId);
                var count = await delete1Command.ExecuteNonQueryAsync();
                return count;
            }
        }

        protected virtual async Task<BundleTokenAppsRecord> PopAsync(Guid identityId,Guid tokenId,Guid appId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var deleteCommand = cn.CreateCommand();
            {
                deleteCommand.CommandText = "DELETE FROM BundleTokenApps " +
                                             "WHERE identityId = @identityId AND tokenId = @tokenId AND appId = @appId " + 
                                             "RETURNING rowId,encryptedKeyStoreKeyJson,created,modified";

                deleteCommand.AddParameter("@identityId", DbType.Binary, identityId);
                deleteCommand.AddParameter("@tokenId", DbType.Binary, tokenId);
                deleteCommand.AddParameter("@appId", DbType.Binary, appId);
                using (var rdr = await deleteCommand.ExecuteReaderAsync(CommandBehavior.SingleRow))
                {
                    if (await rdr.ReadAsync())
                    {
                       return ReadRecordFromReader0(rdr,identityId,tokenId,appId);
                    }
                    else
                    {
                       return null;
                    }
                }
            }
        }

        protected BundleTokenAppsRecord ReadRecordFromReader0(DbDataReader rdr,Guid identityId,Guid tokenId,Guid appId)
        {
            var result = new List<BundleTokenAppsRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new BundleTokenAppsRecord();
            item.identityId = identityId;
            item.tokenId = tokenId;
            item.appId = appId;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.encryptedKeyStoreKeyJson = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[1];
            item.created = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[2]);
            item.modified = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[3]);
            return item;
       }

        protected virtual async Task<BundleTokenAppsRecord> GetAsync(Guid identityId,Guid tokenId,Guid appId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT rowId,encryptedKeyStoreKeyJson,created,modified FROM BundleTokenApps " +
                                             "WHERE identityId = @identityId AND tokenId = @tokenId AND appId = @appId LIMIT 1 "+
                                             ";";

                get0Command.AddParameter("@identityId", DbType.Binary, identityId);
                get0Command.AddParameter("@tokenId", DbType.Binary, tokenId);
                get0Command.AddParameter("@appId", DbType.Binary, appId);
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr,identityId,tokenId,appId);
                        return r;
                    } // using
                } //
            } // using
        }

        protected BundleTokenAppsRecord ReadRecordFromReader1(DbDataReader rdr,Guid identityId,Guid tokenId)
        {
            var result = new List<BundleTokenAppsRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new BundleTokenAppsRecord();
            item.identityId = identityId;
            item.tokenId = tokenId;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.appId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.encryptedKeyStoreKeyJson = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[2];
            item.created = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[3]);
            item.modified = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[4]);
            return item;
       }

        protected virtual async Task<List<BundleTokenAppsRecord>> GetByTokenIdAsync(Guid identityId,Guid tokenId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get1Command = cn.CreateCommand();
            {
                get1Command.CommandText = "SELECT rowId,appId,encryptedKeyStoreKeyJson,created,modified FROM BundleTokenApps " +
                                             "WHERE identityId = @identityId AND tokenId = @tokenId "+
                                             ";";

                get1Command.AddParameter("@identityId", DbType.Binary, identityId);
                get1Command.AddParameter("@tokenId", DbType.Binary, tokenId);
                {
                    using (var rdr = await get1Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return new List<BundleTokenAppsRecord>();
                        }
                        var result = new List<BundleTokenAppsRecord>();
                        while (true)
                        {
                            result.Add(ReadRecordFromReader1(rdr,identityId,tokenId));
                            if (!await rdr.ReadAsync())
                                break;
                        }
                        return result;
                    } // using
                } //
            } // using
        }

        protected BundleTokenAppsRecord ReadRecordFromReader2(DbDataReader rdr,Guid identityId,Guid appId)
        {
            var result = new List<BundleTokenAppsRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new BundleTokenAppsRecord();
            item.identityId = identityId;
            item.appId = appId;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.tokenId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.encryptedKeyStoreKeyJson = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[2];
            item.created = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[3]);
            item.modified = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[4]);
            return item;
       }

        protected virtual async Task<List<BundleTokenAppsRecord>> GetByAppIdAsync(Guid identityId,Guid appId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get2Command = cn.CreateCommand();
            {
                get2Command.CommandText = "SELECT rowId,tokenId,encryptedKeyStoreKeyJson,created,modified FROM BundleTokenApps " +
                                             "WHERE identityId = @identityId AND appId = @appId "+
                                             ";";

                get2Command.AddParameter("@identityId", DbType.Binary, identityId);
                get2Command.AddParameter("@appId", DbType.Binary, appId);
                {
                    using (var rdr = await get2Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return new List<BundleTokenAppsRecord>();
                        }
                        var result = new List<BundleTokenAppsRecord>();
                        while (true)
                        {
                            result.Add(ReadRecordFromReader2(rdr,identityId,appId));
                            if (!await rdr.ReadAsync())
                                break;
                        }
                        return result;
                    } // using
                } //
            } // using
        }

        protected BundleTokenAppsRecord ReadRecordFromReader3(DbDataReader rdr,Guid identityId)
        {
            var result = new List<BundleTokenAppsRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new BundleTokenAppsRecord();
            item.identityId = identityId;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.tokenId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.appId = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[2]);
            item.encryptedKeyStoreKeyJson = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[3];
            item.created = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[4]);
            item.modified = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[5]);
            return item;
       }

        protected virtual async Task<List<BundleTokenAppsRecord>> GetAllAsync(Guid identityId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get3Command = cn.CreateCommand();
            {
                get3Command.CommandText = "SELECT rowId,tokenId,appId,encryptedKeyStoreKeyJson,created,modified FROM BundleTokenApps " +
                                             "WHERE identityId = @identityId "+
                                             ";";

                get3Command.AddParameter("@identityId", DbType.Binary, identityId);
                {
                    using (var rdr = await get3Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return new List<BundleTokenAppsRecord>();
                        }
                        var result = new List<BundleTokenAppsRecord>();
                        while (true)
                        {
                            result.Add(ReadRecordFromReader3(rdr,identityId));
                            if (!await rdr.ReadAsync())
                                break;
                        }
                        return result;
                    } // using
                } //
            } // using
        }

        protected virtual async Task<(List<BundleTokenAppsRecord>, Int64? nextCursor)> PagingByRowIdAsync(int count, Guid identityId, Int64? inCursor)
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
                getPaging0Command.CommandText = "SELECT rowId,identityId,tokenId,appId,encryptedKeyStoreKeyJson,created,modified FROM BundleTokenApps " +
                                            "WHERE (identityId = @identityId) AND rowId > @rowId  ORDER BY rowId ASC  LIMIT @count;";

                getPaging0Command.AddParameter("@rowId", DbType.Int64, inCursor);
                getPaging0Command.AddParameter("@count", DbType.Int64, count+1);
                getPaging0Command.AddParameter("@identityId", DbType.Binary, identityId);

                {
                    await using (var rdr = await getPaging0Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        var result = new List<BundleTokenAppsRecord>();
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
