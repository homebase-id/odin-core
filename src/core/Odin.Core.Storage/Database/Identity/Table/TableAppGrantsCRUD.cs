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

namespace Odin.Core.Storage.Database.Identity.Table
{
    public record AppGrantsRecord
    {
        public Int64 rowId { get; set; }
        public Guid identityId { get; set; }
        public Guid odinHashId { get; set; }
        public Guid appId { get; set; }
        public Guid circleId { get; set; }
        public byte[] data { get; set; }
        public void Validate()
        {
            identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            odinHashId.AssertGuidNotEmpty("Guid parameter odinHashId cannot be set to Empty GUID.");
            appId.AssertGuidNotEmpty("Guid parameter appId cannot be set to Empty GUID.");
            circleId.AssertGuidNotEmpty("Guid parameter circleId cannot be set to Empty GUID.");
            if (data?.Length < 0) throw new OdinDatabaseValidationException($"Too short data, was {data.Length} (min 0)");
            if (data?.Length > 65535) throw new OdinDatabaseValidationException($"Too long data, was {data.Length} (max 65535)");
        }
    } // End of record AppGrantsRecord

    public abstract class TableAppGrantsCRUD : TableBase
    {
        private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory;
        public override string TableName { get; } = "AppGrants";

        protected TableAppGrantsCRUD(ScopedIdentityConnectionFactory scopedConnectionFactory)
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
                await SqlHelper.DeleteTableAsync(cn, "AppGrants");
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE AppGrants IS '{ \"Version\": 0 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS AppGrants( -- { \"Version\": 0 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"odinHashId BYTEA NOT NULL, "
                   +"appId BYTEA NOT NULL, "
                   +"circleId BYTEA NOT NULL, "
                   +"data BYTEA  "
                   +", UNIQUE(identityId,odinHashId,appId,circleId)"
                   +$"){wori};"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "AppGrants", createSql, commentSql);
        }
       */

        protected virtual async Task<int> InsertAsync(AppGrantsRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO AppGrants (identityId,odinHashId,appId,circleId,data) " +
                                           $"VALUES (@identityId,@odinHashId,@appId,@circleId,@data)"+
                                            "RETURNING -1,-1,rowId;";
                insertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                insertCommand.AddParameter("@odinHashId", DbType.Binary, item.odinHashId);
                insertCommand.AddParameter("@appId", DbType.Binary, item.appId);
                insertCommand.AddParameter("@circleId", DbType.Binary, item.circleId);
                insertCommand.AddParameter("@data", DbType.Binary, item.data);
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<bool> TryInsertAsync(AppGrantsRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO AppGrants (identityId,odinHashId,appId,circleId,data) " +
                                            $"VALUES (@identityId,@odinHashId,@appId,@circleId,@data) " +
                                            "ON CONFLICT DO NOTHING "+
                                            "RETURNING -1,-1,rowId;";
                insertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                insertCommand.AddParameter("@odinHashId", DbType.Binary, item.odinHashId);
                insertCommand.AddParameter("@appId", DbType.Binary, item.appId);
                insertCommand.AddParameter("@circleId", DbType.Binary, item.circleId);
                insertCommand.AddParameter("@data", DbType.Binary, item.data);
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                    return true;
                }
                return false;
            }
        }

        protected virtual async Task<int> UpsertAsync(AppGrantsRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                upsertCommand.CommandText = "INSERT INTO AppGrants (identityId,odinHashId,appId,circleId,data) " +
                                            $"VALUES (@identityId,@odinHashId,@appId,@circleId,@data)"+
                                            "ON CONFLICT (identityId,odinHashId,appId,circleId) DO UPDATE "+
                                            $"SET data = @data "+
                                            "RETURNING -1,-1,rowId;";
                upsertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                upsertCommand.AddParameter("@odinHashId", DbType.Binary, item.odinHashId);
                upsertCommand.AddParameter("@appId", DbType.Binary, item.appId);
                upsertCommand.AddParameter("@circleId", DbType.Binary, item.circleId);
                upsertCommand.AddParameter("@data", DbType.Binary, item.data);
                await using var rdr = await upsertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<int> UpdateAsync(AppGrantsRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                updateCommand.CommandText = "UPDATE AppGrants " +
                                            $"SET data = @data "+
                                            "WHERE (identityId = @identityId AND odinHashId = @odinHashId AND appId = @appId AND circleId = @circleId) "+
                                            "RETURNING -1,-1,rowId;";
                updateCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                updateCommand.AddParameter("@odinHashId", DbType.Binary, item.odinHashId);
                updateCommand.AddParameter("@appId", DbType.Binary, item.appId);
                updateCommand.AddParameter("@circleId", DbType.Binary, item.circleId);
                updateCommand.AddParameter("@data", DbType.Binary, item.data);
                await using var rdr = await updateCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
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
                getCountCommand.CommandText = "SELECT COUNT(*) FROM AppGrants;";
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
            sl.Add("odinHashId");
            sl.Add("appId");
            sl.Add("circleId");
            sl.Add("data");
            return sl;
        }

        // SELECT rowId,identityId,odinHashId,appId,circleId,data
        protected AppGrantsRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<AppGrantsRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new AppGrantsRecord();
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.identityId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.odinHashId = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[2]);
            item.appId = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[3]);
            item.circleId = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[4]);
            item.data = (rdr[5] == DBNull.Value) ? null : (byte[])(rdr[5]);
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            return item;
       }

        protected virtual async Task<int> DeleteAsync(Guid identityId,Guid odinHashId,Guid appId,Guid circleId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM AppGrants " +
                                             "WHERE identityId = @identityId AND odinHashId = @odinHashId AND appId = @appId AND circleId = @circleId";

                delete0Command.AddParameter("@identityId", DbType.Binary, identityId);
                delete0Command.AddParameter("@odinHashId", DbType.Binary, odinHashId);
                delete0Command.AddParameter("@appId", DbType.Binary, appId);
                delete0Command.AddParameter("@circleId", DbType.Binary, circleId);
                var count = await delete0Command.ExecuteNonQueryAsync();
                return count;
            }
        }

        protected virtual async Task<AppGrantsRecord> PopAsync(Guid identityId,Guid odinHashId,Guid appId,Guid circleId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var deleteCommand = cn.CreateCommand();
            {
                deleteCommand.CommandText = "DELETE FROM AppGrants " +
                                             "WHERE identityId = @identityId AND odinHashId = @odinHashId AND appId = @appId AND circleId = @circleId " + 
                                             "RETURNING rowId,data";

                deleteCommand.AddParameter("@identityId", DbType.Binary, identityId);
                deleteCommand.AddParameter("@odinHashId", DbType.Binary, odinHashId);
                deleteCommand.AddParameter("@appId", DbType.Binary, appId);
                deleteCommand.AddParameter("@circleId", DbType.Binary, circleId);
                using (var rdr = await deleteCommand.ExecuteReaderAsync(CommandBehavior.SingleRow))
                {
                    if (await rdr.ReadAsync())
                    {
                       return ReadRecordFromReader0(rdr,identityId,odinHashId,appId,circleId);
                    }
                    else
                    {
                       return null;
                    }
                }
            }
        }

        protected AppGrantsRecord ReadRecordFromReader0(DbDataReader rdr,Guid identityId,Guid odinHashId,Guid appId,Guid circleId)
        {
            var result = new List<AppGrantsRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new AppGrantsRecord();
            item.identityId = identityId;
            item.odinHashId = odinHashId;
            item.appId = appId;
            item.circleId = circleId;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.data = (rdr[1] == DBNull.Value) ? null : (byte[])(rdr[1]);
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            return item;
       }

        protected virtual async Task<AppGrantsRecord> GetAsync(Guid identityId,Guid odinHashId,Guid appId,Guid circleId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT rowId,data FROM AppGrants " +
                                             "WHERE identityId = @identityId AND odinHashId = @odinHashId AND appId = @appId AND circleId = @circleId LIMIT 1 "+
                                             ";";

                get0Command.AddParameter("@identityId", DbType.Binary, identityId);
                get0Command.AddParameter("@odinHashId", DbType.Binary, odinHashId);
                get0Command.AddParameter("@appId", DbType.Binary, appId);
                get0Command.AddParameter("@circleId", DbType.Binary, circleId);
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr,identityId,odinHashId,appId,circleId);
                        return r;
                    } // using
                } //
            } // using
        }

        protected AppGrantsRecord ReadRecordFromReader1(DbDataReader rdr,Guid identityId,Guid odinHashId)
        {
            var result = new List<AppGrantsRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new AppGrantsRecord();
            item.identityId = identityId;
            item.odinHashId = odinHashId;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.appId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.circleId = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[2]);
            item.data = (rdr[3] == DBNull.Value) ? null : (byte[])(rdr[3]);
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            return item;
       }

        protected virtual async Task<List<AppGrantsRecord>> GetByOdinHashIdAsync(Guid identityId,Guid odinHashId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get1Command = cn.CreateCommand();
            {
                get1Command.CommandText = "SELECT rowId,appId,circleId,data FROM AppGrants " +
                                             "WHERE identityId = @identityId AND odinHashId = @odinHashId "+
                                             ";";

                get1Command.AddParameter("@identityId", DbType.Binary, identityId);
                get1Command.AddParameter("@odinHashId", DbType.Binary, odinHashId);
                {
                    using (var rdr = await get1Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return new List<AppGrantsRecord>();
                        }
                        var result = new List<AppGrantsRecord>();
                        while (true)
                        {
                            result.Add(ReadRecordFromReader1(rdr,identityId,odinHashId));
                            if (!await rdr.ReadAsync())
                                break;
                        }
                        return result;
                    } // using
                } //
            } // using
        }

        protected AppGrantsRecord ReadRecordFromReader2(DbDataReader rdr,Guid identityId)
        {
            var result = new List<AppGrantsRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new AppGrantsRecord();
            item.identityId = identityId;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.odinHashId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.appId = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[2]);
            item.circleId = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[3]);
            item.data = (rdr[4] == DBNull.Value) ? null : (byte[])(rdr[4]);
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            return item;
       }

        protected virtual async Task<List<AppGrantsRecord>> GetAllAsync(Guid identityId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get2Command = cn.CreateCommand();
            {
                get2Command.CommandText = "SELECT rowId,odinHashId,appId,circleId,data FROM AppGrants " +
                                             "WHERE identityId = @identityId "+
                                             ";";

                get2Command.AddParameter("@identityId", DbType.Binary, identityId);
                {
                    using (var rdr = await get2Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return new List<AppGrantsRecord>();
                        }
                        var result = new List<AppGrantsRecord>();
                        while (true)
                        {
                            result.Add(ReadRecordFromReader2(rdr,identityId));
                            if (!await rdr.ReadAsync())
                                break;
                        }
                        return result;
                    } // using
                } //
            } // using
        }

        protected virtual async Task<(List<AppGrantsRecord>, Int64? nextCursor)> PagingByRowIdAsync(int count, Int64? inCursor)
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
                getPaging0Command.CommandText = "SELECT rowId,identityId,odinHashId,appId,circleId,data FROM AppGrants " +
                                            "WHERE rowId > @rowId  ORDER BY rowId ASC  LIMIT @count;";

                getPaging0Command.AddParameter("@rowId", DbType.Int64, inCursor);
                getPaging0Command.AddParameter("@count", DbType.Int64, count+1);

                {
                    await using (var rdr = await getPaging0Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        var result = new List<AppGrantsRecord>();
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
