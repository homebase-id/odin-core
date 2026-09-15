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
    public record AppRegistrationsRecord
    {
        public Int64 rowId { get; set; }
        public Guid identityId { get; set; }
        public Guid AppId { get; set; }
        public string AppSlug { get; set; }
        public string Name { get; set; }
        public string CorsHostName { get; set; }
        public string grantJson { get; set; }
        public string detailsJson { get; set; }
        public UnixTimeUtc created { get; set; }
        public UnixTimeUtc modified { get; set; }
        public void Validate()
        {
            identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            AppId.AssertGuidNotEmpty("Guid parameter AppId cannot be set to Empty GUID.");
            if (AppSlug == null) throw new OdinDatabaseValidationException("Cannot be null AppSlug");
            if (AppSlug?.Length < 1) throw new OdinDatabaseValidationException($"Too short AppSlug, was {AppSlug.Length} (min 1)");
            if (AppSlug?.Length > 64) throw new OdinDatabaseValidationException($"Too long AppSlug, was {AppSlug.Length} (max 64)");
            if (Name == null) throw new OdinDatabaseValidationException("Cannot be null Name");
            if (Name?.Length < 0) throw new OdinDatabaseValidationException($"Too short Name, was {Name.Length} (min 0)");
            if (Name?.Length > 1024) throw new OdinDatabaseValidationException($"Too long Name, was {Name.Length} (max 1024)");
            if (CorsHostName?.Length < 0) throw new OdinDatabaseValidationException($"Too short CorsHostName, was {CorsHostName.Length} (min 0)");
            if (CorsHostName?.Length > 256) throw new OdinDatabaseValidationException($"Too long CorsHostName, was {CorsHostName.Length} (max 256)");
            if (grantJson == null) throw new OdinDatabaseValidationException("Cannot be null grantJson");
            if (grantJson?.Length < 0) throw new OdinDatabaseValidationException($"Too short grantJson, was {grantJson.Length} (min 0)");
            if (grantJson?.Length > 21504) throw new OdinDatabaseValidationException($"Too long grantJson, was {grantJson.Length} (max 21504)");
            if (detailsJson?.Length < 0) throw new OdinDatabaseValidationException($"Too short detailsJson, was {detailsJson.Length} (min 0)");
            if (detailsJson?.Length > 21504) throw new OdinDatabaseValidationException($"Too long detailsJson, was {detailsJson.Length} (max 21504)");
        }
    } // End of record AppRegistrationsRecord

    public abstract class TableAppRegistrationsCRUD : TableBase
    {
        private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory;
        public override string TableName { get; } = "AppRegistrations";

        protected TableAppRegistrationsCRUD(ScopedIdentityConnectionFactory scopedConnectionFactory)
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
                await SqlHelper.DeleteTableAsync(cn, "AppRegistrations");
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE AppRegistrations IS '{ \"Version\": 202608271000 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS AppRegistrations( -- { \"Version\": 202608271000 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"AppId BYTEA NOT NULL, "
                   +"AppSlug TEXT NOT NULL, "
                   +"Name TEXT NOT NULL, "
                   +"CorsHostName TEXT , "
                   +"grantJson TEXT NOT NULL, "
                   +"detailsJson TEXT , "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +", UNIQUE(identityId,AppId)"
                   +", UNIQUE(identityId,AppSlug)"
                   +$"){wori};"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "AppRegistrations", createSql, commentSql);
        }
       */

        protected virtual async Task<int> InsertAsync(AppRegistrationsRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr = insertCommand.SqlNow();
                insertCommand.CommandText = "INSERT INTO AppRegistrations (identityId,AppId,AppSlug,Name,CorsHostName,grantJson,detailsJson,created,modified) " +
                                           $"VALUES (@identityId,@AppId,@AppSlug,@Name,@CorsHostName,@grantJson,@detailsJson,{sqlNowStr},{sqlNowStr})"+
                                            "RETURNING created,modified,rowId;";
                insertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                insertCommand.AddParameter("@AppId", DbType.Binary, item.AppId);
                insertCommand.AddParameter("@AppSlug", DbType.String, item.AppSlug);
                insertCommand.AddParameter("@Name", DbType.String, item.Name);
                insertCommand.AddParameter("@CorsHostName", DbType.String, item.CorsHostName);
                insertCommand.AddParameter("@grantJson", DbType.String, item.grantJson);
                insertCommand.AddParameter("@detailsJson", DbType.String, item.detailsJson);
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

        protected virtual async Task<bool> TryInsertAsync(AppRegistrationsRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr = insertCommand.SqlNow();
                insertCommand.CommandText = "INSERT INTO AppRegistrations (identityId,AppId,AppSlug,Name,CorsHostName,grantJson,detailsJson,created,modified) " +
                                            $"VALUES (@identityId,@AppId,@AppSlug,@Name,@CorsHostName,@grantJson,@detailsJson,{sqlNowStr},{sqlNowStr}) " +
                                            "ON CONFLICT DO NOTHING "+
                                            "RETURNING created,modified,rowId;";
                insertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                insertCommand.AddParameter("@AppId", DbType.Binary, item.AppId);
                insertCommand.AddParameter("@AppSlug", DbType.String, item.AppSlug);
                insertCommand.AddParameter("@Name", DbType.String, item.Name);
                insertCommand.AddParameter("@CorsHostName", DbType.String, item.CorsHostName);
                insertCommand.AddParameter("@grantJson", DbType.String, item.grantJson);
                insertCommand.AddParameter("@detailsJson", DbType.String, item.detailsJson);
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

        protected virtual async Task<int> UpsertAsync(AppRegistrationsRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                string sqlNowStr = upsertCommand.SqlNow();
                upsertCommand.CommandText = "INSERT INTO AppRegistrations (identityId,AppId,AppSlug,Name,CorsHostName,grantJson,detailsJson,created,modified) " +
                                            $"VALUES (@identityId,@AppId,@AppSlug,@Name,@CorsHostName,@grantJson,@detailsJson,{sqlNowStr},{sqlNowStr})"+
                                            "ON CONFLICT (identityId,AppId) DO UPDATE "+
                                            $"SET AppSlug = @AppSlug,Name = @Name,CorsHostName = @CorsHostName,grantJson = @grantJson,detailsJson = @detailsJson,modified = {upsertCommand.SqlMax()}(AppRegistrations.modified+1,{sqlNowStr}) "+
                                            "RETURNING created,modified,rowId;";
                upsertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                upsertCommand.AddParameter("@AppId", DbType.Binary, item.AppId);
                upsertCommand.AddParameter("@AppSlug", DbType.String, item.AppSlug);
                upsertCommand.AddParameter("@Name", DbType.String, item.Name);
                upsertCommand.AddParameter("@CorsHostName", DbType.String, item.CorsHostName);
                upsertCommand.AddParameter("@grantJson", DbType.String, item.grantJson);
                upsertCommand.AddParameter("@detailsJson", DbType.String, item.detailsJson);
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

        protected virtual async Task<int> UpdateAsync(AppRegistrationsRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                string sqlNowStr = updateCommand.SqlNow();
                updateCommand.CommandText = "UPDATE AppRegistrations " +
                                            $"SET AppSlug = @AppSlug,Name = @Name,CorsHostName = @CorsHostName,grantJson = @grantJson,detailsJson = @detailsJson,modified = {updateCommand.SqlMax()}(AppRegistrations.modified+1,{sqlNowStr}) "+
                                            "WHERE (identityId = @identityId AND AppId = @AppId) "+
                                            "RETURNING created,modified,rowId;";
                updateCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                updateCommand.AddParameter("@AppId", DbType.Binary, item.AppId);
                updateCommand.AddParameter("@AppSlug", DbType.String, item.AppSlug);
                updateCommand.AddParameter("@Name", DbType.String, item.Name);
                updateCommand.AddParameter("@CorsHostName", DbType.String, item.CorsHostName);
                updateCommand.AddParameter("@grantJson", DbType.String, item.grantJson);
                updateCommand.AddParameter("@detailsJson", DbType.String, item.detailsJson);
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
                getCountCommand.CommandText = "SELECT COUNT(*) FROM AppRegistrations;";
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
            sl.Add("AppId");
            sl.Add("AppSlug");
            sl.Add("Name");
            sl.Add("CorsHostName");
            sl.Add("grantJson");
            sl.Add("detailsJson");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        // SELECT rowId,identityId,AppId,AppSlug,Name,CorsHostName,grantJson,detailsJson,created,modified
        protected AppRegistrationsRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<AppRegistrationsRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new AppRegistrationsRecord();
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.identityId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.AppId = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[2]);
            item.AppSlug = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[3];
            item.Name = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[4];
            item.CorsHostName = (rdr[5] == DBNull.Value) ? null : (string)rdr[5];
            item.grantJson = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[6];
            item.detailsJson = (rdr[7] == DBNull.Value) ? null : (string)rdr[7];
            item.created = (rdr[8] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[8]);
            item.modified = (rdr[9] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[9]);
            return item;
       }

        internal virtual async Task ExportRowsAsync(Guid identityId, Func<AppRegistrationsRecord, Task> onRow)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var exportCommand = cn.CreateCommand();
            {
                exportCommand.CommandText = "SELECT rowId,identityId,AppId,AppSlug,Name,CorsHostName,grantJson,detailsJson,created,modified FROM AppRegistrations " +
                                            "WHERE identityId = @identityId ORDER BY rowId ASC;";
                exportCommand.AddParameter("@identityId", DbType.Binary, identityId);
                await using var rdr = await exportCommand.ExecuteReaderAsync(CommandBehavior.Default);
                while (await rdr.ReadAsync())
                {
                    await onRow(ReadRecordFromReaderAll(rdr));
                }
            }
        }

        internal virtual async Task<int> ImportRowAsync(AppRegistrationsRecord item)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var importCommand = cn.CreateCommand();
            {
                importCommand.CommandText = "INSERT INTO AppRegistrations (identityId,AppId,AppSlug,Name,CorsHostName,grantJson,detailsJson,created,modified) " +
                                            "VALUES (@identityId,@AppId,@AppSlug,@Name,@CorsHostName,@grantJson,@detailsJson,@created,@modified);";
                importCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                importCommand.AddParameter("@AppId", DbType.Binary, item.AppId);
                importCommand.AddParameter("@AppSlug", DbType.String, item.AppSlug);
                importCommand.AddParameter("@Name", DbType.String, item.Name);
                importCommand.AddParameter("@CorsHostName", DbType.String, item.CorsHostName);
                importCommand.AddParameter("@grantJson", DbType.String, item.grantJson);
                importCommand.AddParameter("@detailsJson", DbType.String, item.detailsJson);
                importCommand.AddParameter("@created", DbType.Int64, item.created.milliseconds);
                importCommand.AddParameter("@modified", DbType.Int64, item.modified.milliseconds);
                return await importCommand.ExecuteNonQueryAsync();
            }
        }

        protected virtual async Task<int> DeleteAsync(Guid identityId,Guid AppId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM AppRegistrations " +
                                             "WHERE identityId = @identityId AND AppId = @AppId";

                delete0Command.AddParameter("@identityId", DbType.Binary, identityId);
                delete0Command.AddParameter("@AppId", DbType.Binary, AppId);
                var count = await delete0Command.ExecuteNonQueryAsync();
                return count;
            }
        }

        protected virtual async Task<AppRegistrationsRecord> PopAsync(Guid identityId,Guid AppId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var deleteCommand = cn.CreateCommand();
            {
                deleteCommand.CommandText = "DELETE FROM AppRegistrations " +
                                             "WHERE identityId = @identityId AND AppId = @AppId " + 
                                             "RETURNING rowId,AppSlug,Name,CorsHostName,grantJson,detailsJson,created,modified";

                deleteCommand.AddParameter("@identityId", DbType.Binary, identityId);
                deleteCommand.AddParameter("@AppId", DbType.Binary, AppId);
                using (var rdr = await deleteCommand.ExecuteReaderAsync(CommandBehavior.SingleRow))
                {
                    if (await rdr.ReadAsync())
                    {
                       return ReadRecordFromReader0(rdr,identityId,AppId);
                    }
                    else
                    {
                       return null;
                    }
                }
            }
        }

        protected AppRegistrationsRecord ReadRecordFromReader0(DbDataReader rdr,Guid identityId,Guid AppId)
        {
            var result = new List<AppRegistrationsRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new AppRegistrationsRecord();
            item.identityId = identityId;
            item.AppId = AppId;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.AppSlug = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[1];
            item.Name = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[2];
            item.CorsHostName = (rdr[3] == DBNull.Value) ? null : (string)rdr[3];
            item.grantJson = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[4];
            item.detailsJson = (rdr[5] == DBNull.Value) ? null : (string)rdr[5];
            item.created = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[6]);
            item.modified = (rdr[7] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[7]);
            return item;
       }

        protected virtual async Task<AppRegistrationsRecord> GetAsync(Guid identityId,Guid AppId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT rowId,AppSlug,Name,CorsHostName,grantJson,detailsJson,created,modified FROM AppRegistrations " +
                                             "WHERE identityId = @identityId AND AppId = @AppId LIMIT 1 "+
                                             ";";

                get0Command.AddParameter("@identityId", DbType.Binary, identityId);
                get0Command.AddParameter("@AppId", DbType.Binary, AppId);
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr,identityId,AppId);
                        return r;
                    } // using
                } //
            } // using
        }

        protected AppRegistrationsRecord ReadRecordFromReader1(DbDataReader rdr,Guid identityId)
        {
            var result = new List<AppRegistrationsRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new AppRegistrationsRecord();
            item.identityId = identityId;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.AppId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.AppSlug = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[2];
            item.Name = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[3];
            item.CorsHostName = (rdr[4] == DBNull.Value) ? null : (string)rdr[4];
            item.grantJson = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[5];
            item.detailsJson = (rdr[6] == DBNull.Value) ? null : (string)rdr[6];
            item.created = (rdr[7] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[7]);
            item.modified = (rdr[8] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[8]);
            return item;
       }

        protected virtual async Task<List<AppRegistrationsRecord>> GetAllAsync(Guid identityId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get1Command = cn.CreateCommand();
            {
                get1Command.CommandText = "SELECT rowId,AppId,AppSlug,Name,CorsHostName,grantJson,detailsJson,created,modified FROM AppRegistrations " +
                                             "WHERE identityId = @identityId "+
                                             ";";

                get1Command.AddParameter("@identityId", DbType.Binary, identityId);
                {
                    using (var rdr = await get1Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return new List<AppRegistrationsRecord>();
                        }
                        var result = new List<AppRegistrationsRecord>();
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

        protected AppRegistrationsRecord ReadRecordFromReader2(DbDataReader rdr,Guid identityId,string AppSlug)
        {
            if (AppSlug == null) throw new OdinDatabaseValidationException("Cannot be null AppSlug");
            if (AppSlug?.Length < 1) throw new OdinDatabaseValidationException($"Too short AppSlug, was {AppSlug.Length} (min 1)");
            if (AppSlug?.Length > 64) throw new OdinDatabaseValidationException($"Too long AppSlug, was {AppSlug.Length} (max 64)");
            var result = new List<AppRegistrationsRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new AppRegistrationsRecord();
            item.identityId = identityId;
            item.AppSlug = AppSlug;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.AppId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.Name = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[2];
            item.CorsHostName = (rdr[3] == DBNull.Value) ? null : (string)rdr[3];
            item.grantJson = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[4];
            item.detailsJson = (rdr[5] == DBNull.Value) ? null : (string)rdr[5];
            item.created = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[6]);
            item.modified = (rdr[7] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[7]);
            return item;
       }

        protected virtual async Task<AppRegistrationsRecord> GetByAppSlugAsync(Guid identityId,string AppSlug)
        {
            if (AppSlug == null) throw new OdinDatabaseValidationException("Cannot be null AppSlug");
            if (AppSlug?.Length < 1) throw new OdinDatabaseValidationException($"Too short AppSlug, was {AppSlug.Length} (min 1)");
            if (AppSlug?.Length > 64) throw new OdinDatabaseValidationException($"Too long AppSlug, was {AppSlug.Length} (max 64)");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get2Command = cn.CreateCommand();
            {
                get2Command.CommandText = "SELECT rowId,AppId,Name,CorsHostName,grantJson,detailsJson,created,modified FROM AppRegistrations " +
                                             "WHERE identityId = @identityId AND AppSlug = @AppSlug LIMIT 1 "+
                                             ";";

                get2Command.AddParameter("@identityId", DbType.Binary, identityId);
                get2Command.AddParameter("@AppSlug", DbType.String, AppSlug);
                {
                    using (var rdr = await get2Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader2(rdr,identityId,AppSlug);
                        return r;
                    } // using
                } //
            } // using
        }

        protected virtual async Task<(List<AppRegistrationsRecord>, Int64? nextCursor)> PagingByRowIdAsync(int count, Guid identityId, Int64? inCursor)
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
                getPaging0Command.CommandText = "SELECT rowId,identityId,AppId,AppSlug,Name,CorsHostName,grantJson,detailsJson,created,modified FROM AppRegistrations " +
                                            "WHERE (identityId = @identityId) AND rowId > @rowId  ORDER BY rowId ASC  LIMIT @count;";

                getPaging0Command.AddParameter("@rowId", DbType.Int64, inCursor);
                getPaging0Command.AddParameter("@count", DbType.Int64, count+1);
                getPaging0Command.AddParameter("@identityId", DbType.Binary, identityId);

                {
                    await using (var rdr = await getPaging0Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        var result = new List<AppRegistrationsRecord>();
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
