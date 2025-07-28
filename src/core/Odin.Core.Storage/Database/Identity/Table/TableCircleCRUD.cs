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
using Odin.Core.Util;
using Odin.Core.Storage.Exceptions;
using Odin.Core.Storage.SQLite; //added for homebase social sync

// THIS FILE IS AUTO GENERATED - DO NOT EDIT

namespace Odin.Core.Storage.Database.Identity.Table
{
    public record CircleRecord
    {
        public Int64 rowId { get; set; }
        public Guid identityId { get; set; }
        public Guid circleId { get; set; }
        public string circleName { get; set; }
        public byte[] data { get; set; }
        public void Validate()
        {
            identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            circleId.AssertGuidNotEmpty("Guid parameter circleId cannot be set to Empty GUID.");
            if (circleName == null) throw new OdinDatabaseValidationException("Cannot be null circleName");
            if (circleName?.Length < 2) throw new OdinDatabaseValidationException($"Too short circleName, was {circleName.Length} (min 2)");
            if (circleName?.Length > 80) throw new OdinDatabaseValidationException($"Too long circleName, was {circleName.Length} (max 80)");
            if (data?.Length < 0) throw new OdinDatabaseValidationException($"Too short data, was {data.Length} (min 0)");
            if (data?.Length > 65000) throw new OdinDatabaseValidationException($"Too long data, was {data.Length} (max 65000)");
        }
    } // End of record CircleRecord

    public abstract class TableCircleCRUD
    {
        private readonly CacheHelper _cache;
        private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory;

        protected TableCircleCRUD(CacheHelper cache, ScopedIdentityConnectionFactory scopedConnectionFactory)
        {
            _cache = cache;
            _scopedConnectionFactory = scopedConnectionFactory;
        }


        public virtual async Task EnsureTableExistsAsync(bool dropExisting = false)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            if (dropExisting)
                await SqlHelper.DeleteTableAsync(cn, "Circle");
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowid BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE Circle IS '{ \"Version\": 0 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS Circle( -- { \"Version\": 0 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"circleId BYTEA NOT NULL UNIQUE, "
                   +"circleName TEXT NOT NULL, "
                   +"data BYTEA  "
                   +", UNIQUE(identityId,circleId)"
                   +$"){wori};"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "Circle", createSql, commentSql);
        }

        protected virtual async Task<int> InsertAsync(CircleRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO Circle (identityId,circleId,circleName,data) " +
                                           $"VALUES (@identityId,@circleId,@circleName,@data)"+
                                            "RETURNING -1,-1,rowId;";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.DbType = DbType.Binary;
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.DbType = DbType.Binary;
                insertParam2.ParameterName = "@circleId";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.DbType = DbType.String;
                insertParam3.ParameterName = "@circleName";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.DbType = DbType.Binary;
                insertParam4.ParameterName = "@data";
                insertCommand.Parameters.Add(insertParam4);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.circleId.ToByteArray();
                insertParam3.Value = item.circleName;
                insertParam4.Value = item.data ?? (object)DBNull.Value;
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                    _cache.AddOrUpdate("TableCircleCRUD", item.identityId.ToString()+item.circleId.ToString(), item);
                    return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<bool> TryInsertAsync(CircleRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO Circle (identityId,circleId,circleName,data) " +
                                            $"VALUES (@identityId,@circleId,@circleName,@data) " +
                                            "ON CONFLICT DO NOTHING "+
                                            "RETURNING -1,-1,rowId;";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.DbType = DbType.Binary;
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.DbType = DbType.Binary;
                insertParam2.ParameterName = "@circleId";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.DbType = DbType.String;
                insertParam3.ParameterName = "@circleName";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.DbType = DbType.Binary;
                insertParam4.ParameterName = "@data";
                insertCommand.Parameters.Add(insertParam4);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.circleId.ToByteArray();
                insertParam3.Value = item.circleName;
                insertParam4.Value = item.data ?? (object)DBNull.Value;
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                   _cache.AddOrUpdate("TableCircleCRUD", item.identityId.ToString()+item.circleId.ToString(), item);
                    return true;
                }
                return false;
            }
        }

        protected virtual async Task<int> UpsertAsync(CircleRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                upsertCommand.CommandText = "INSERT INTO Circle (identityId,circleId,circleName,data) " +
                                            $"VALUES (@identityId,@circleId,@circleName,@data)"+
                                            "ON CONFLICT (identityId,circleId) DO UPDATE "+
                                            $"SET circleName = @circleName,data = @data "+
                                            "RETURNING -1,-1,rowId;";
                var upsertParam1 = upsertCommand.CreateParameter();
                upsertParam1.DbType = DbType.Binary;
                upsertParam1.ParameterName = "@identityId";
                upsertCommand.Parameters.Add(upsertParam1);
                var upsertParam2 = upsertCommand.CreateParameter();
                upsertParam2.DbType = DbType.Binary;
                upsertParam2.ParameterName = "@circleId";
                upsertCommand.Parameters.Add(upsertParam2);
                var upsertParam3 = upsertCommand.CreateParameter();
                upsertParam3.DbType = DbType.String;
                upsertParam3.ParameterName = "@circleName";
                upsertCommand.Parameters.Add(upsertParam3);
                var upsertParam4 = upsertCommand.CreateParameter();
                upsertParam4.DbType = DbType.Binary;
                upsertParam4.ParameterName = "@data";
                upsertCommand.Parameters.Add(upsertParam4);
                upsertParam1.Value = item.identityId.ToByteArray();
                upsertParam2.Value = item.circleId.ToByteArray();
                upsertParam3.Value = item.circleName;
                upsertParam4.Value = item.data ?? (object)DBNull.Value;
                await using var rdr = await upsertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                   _cache.AddOrUpdate("TableCircleCRUD", item.identityId.ToString()+item.circleId.ToString(), item);
                    return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<int> UpdateAsync(CircleRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                updateCommand.CommandText = "UPDATE Circle " +
                                            $"SET circleName = @circleName,data = @data "+
                                            "WHERE (identityId = @identityId AND circleId = @circleId) "+
                                            "RETURNING -1,-1,rowId;";
                var updateParam1 = updateCommand.CreateParameter();
                updateParam1.DbType = DbType.Binary;
                updateParam1.ParameterName = "@identityId";
                updateCommand.Parameters.Add(updateParam1);
                var updateParam2 = updateCommand.CreateParameter();
                updateParam2.DbType = DbType.Binary;
                updateParam2.ParameterName = "@circleId";
                updateCommand.Parameters.Add(updateParam2);
                var updateParam3 = updateCommand.CreateParameter();
                updateParam3.DbType = DbType.String;
                updateParam3.ParameterName = "@circleName";
                updateCommand.Parameters.Add(updateParam3);
                var updateParam4 = updateCommand.CreateParameter();
                updateParam4.DbType = DbType.Binary;
                updateParam4.ParameterName = "@data";
                updateCommand.Parameters.Add(updateParam4);
                updateParam1.Value = item.identityId.ToByteArray();
                updateParam2.Value = item.circleId.ToByteArray();
                updateParam3.Value = item.circleName;
                updateParam4.Value = item.data ?? (object)DBNull.Value;
                await using var rdr = await updateCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                   _cache.AddOrUpdate("TableCircleCRUD", item.identityId.ToString()+item.circleId.ToString(), item);
                    return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<int> GetCountAsync()
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getCountCommand = cn.CreateCommand();
            {
                 // TODO: this is SQLite specific
                getCountCommand.CommandText = "SELECT COUNT(*) FROM Circle;";
                var count = await getCountCommand.ExecuteScalarAsync();
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
            sl.Add("identityId");
            sl.Add("circleId");
            sl.Add("circleName");
            sl.Add("data");
            return sl;
        }

        // SELECT rowId,identityId,circleId,circleName,data
        protected CircleRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<CircleRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new CircleRecord();
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.identityId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.circleId = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[2]);
            item.circleName = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[3];
            item.data = (rdr[4] == DBNull.Value) ? null : (byte[])(rdr[4]);
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            return item;
       }

        protected virtual async Task<int> DeleteAsync(Guid identityId,Guid circleId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM Circle " +
                                             "WHERE identityId = @identityId AND circleId = @circleId";
                var delete0Param1 = delete0Command.CreateParameter();
                delete0Param1.DbType = DbType.Binary;
                delete0Param1.ParameterName = "@identityId";
                delete0Command.Parameters.Add(delete0Param1);
                var delete0Param2 = delete0Command.CreateParameter();
                delete0Param2.DbType = DbType.Binary;
                delete0Param2.ParameterName = "@circleId";
                delete0Command.Parameters.Add(delete0Param2);

                delete0Param1.Value = identityId.ToByteArray();
                delete0Param2.Value = circleId.ToByteArray();
                var count = await delete0Command.ExecuteNonQueryAsync();
                if (count > 0)
                    _cache.Remove("TableCircleCRUD", identityId.ToString()+circleId.ToString());
                return count;
            }
        }

        protected virtual async Task<CircleRecord> PopAsync(Guid identityId,Guid circleId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var deleteCommand = cn.CreateCommand();
            {
                deleteCommand.CommandText = "DELETE FROM Circle " +
                                             "WHERE identityId = @identityId AND circleId = @circleId " + 
                                             "RETURNING rowId,circleName,data";
                var deleteParam1 = deleteCommand.CreateParameter();
                deleteParam1.DbType = DbType.Binary;
                deleteParam1.ParameterName = "@identityId";
                deleteCommand.Parameters.Add(deleteParam1);
                var deleteParam2 = deleteCommand.CreateParameter();
                deleteParam2.DbType = DbType.Binary;
                deleteParam2.ParameterName = "@circleId";
                deleteCommand.Parameters.Add(deleteParam2);

                deleteParam1.Value = identityId.ToByteArray();
                deleteParam2.Value = circleId.ToByteArray();
                using (var rdr = await deleteCommand.ExecuteReaderAsync(CommandBehavior.SingleRow))
                {
                    if (await rdr.ReadAsync())
                    {
                       return ReadRecordFromReader0(rdr,identityId,circleId);
                    }
                    else
                    {
                       return null;
                    }
                }
            }
        }

        protected CircleRecord ReadRecordFromReader0(DbDataReader rdr,Guid identityId,Guid circleId)
        {
            var result = new List<CircleRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new CircleRecord();
            item.identityId = identityId;
            item.circleId = circleId;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.circleName = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[1];
            item.data = (rdr[2] == DBNull.Value) ? null : (byte[])(rdr[2]);
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            return item;
       }

        protected virtual async Task<CircleRecord> GetAsync(Guid identityId,Guid circleId)
        {
            var (hit, cacheObject) = _cache.Get("TableCircleCRUD", identityId.ToString()+circleId.ToString());
            if (hit)
                return (CircleRecord)cacheObject;
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT rowId,circleName,data FROM Circle " +
                                             "WHERE identityId = @identityId AND circleId = @circleId LIMIT 1;"+
                                             ";";
                var get0Param1 = get0Command.CreateParameter();
                get0Param1.DbType = DbType.Binary;
                get0Param1.ParameterName = "@identityId";
                get0Command.Parameters.Add(get0Param1);
                var get0Param2 = get0Command.CreateParameter();
                get0Param2.DbType = DbType.Binary;
                get0Param2.ParameterName = "@circleId";
                get0Command.Parameters.Add(get0Param2);

                get0Param1.Value = identityId.ToByteArray();
                get0Param2.Value = circleId.ToByteArray();
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            _cache.AddOrUpdate("TableCircleCRUD", identityId.ToString()+circleId.ToString(), null);
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr,identityId,circleId);
                        _cache.AddOrUpdate("TableCircleCRUD", identityId.ToString()+circleId.ToString(), r);
                        return r;
                    } // using
                } //
            } // using
        }

        protected virtual async Task<(List<CircleRecord>, Guid? nextCursor)> PagingByCircleIdAsync(int count, Guid identityId, Guid? inCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");
            if (count == int.MaxValue)
                count--; // avoid overflow when doing +1 on the param below
            if (inCursor == null)
                inCursor = Guid.Empty;

            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getPaging2Command = cn.CreateCommand();
            {
                getPaging2Command.CommandText = "SELECT rowId,identityId,circleId,circleName,data FROM Circle " +
                                            "WHERE (identityId = @identityId) AND circleId > @circleId  ORDER BY circleId ASC  LIMIT @count;";
                var getPaging2Param1 = getPaging2Command.CreateParameter();
                getPaging2Param1.DbType = DbType.Binary;
                getPaging2Param1.ParameterName = "@circleId";
                getPaging2Command.Parameters.Add(getPaging2Param1);
                var getPaging2Param2 = getPaging2Command.CreateParameter();
                getPaging2Param2.DbType = DbType.Int64;
                getPaging2Param2.ParameterName = "@count";
                getPaging2Command.Parameters.Add(getPaging2Param2);
                var getPaging2Param3 = getPaging2Command.CreateParameter();
                getPaging2Param3.DbType = DbType.Binary;
                getPaging2Param3.ParameterName = "@identityId";
                getPaging2Command.Parameters.Add(getPaging2Param3);

                getPaging2Param1.Value = inCursor?.ToByteArray();
                getPaging2Param2.Value = count+1;
                getPaging2Param3.Value = identityId.ToByteArray();

                {
                    await using (var rdr = await getPaging2Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        var result = new List<CircleRecord>();
                        Guid? nextCursor;
                        int n = 0;
                        while ((n < count) && await rdr.ReadAsync())
                        {
                            n++;
                            result.Add(ReadRecordFromReaderAll(rdr));
                        } // while
                        if ((n > 0) && await rdr.ReadAsync())
                        {
                                nextCursor = result[n - 1].circleId;
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

        protected virtual async Task<(List<CircleRecord>, Int64? nextCursor)> PagingByRowIdAsync(int count, Int64? inCursor)
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
                getPaging0Command.CommandText = "SELECT rowId,identityId,circleId,circleName,data FROM Circle " +
                                            "WHERE rowId > @rowId  ORDER BY rowId ASC  LIMIT @count;";
                var getPaging0Param1 = getPaging0Command.CreateParameter();
                getPaging0Param1.DbType = DbType.Int64;
                getPaging0Param1.ParameterName = "@rowId";
                getPaging0Command.Parameters.Add(getPaging0Param1);
                var getPaging0Param2 = getPaging0Command.CreateParameter();
                getPaging0Param2.DbType = DbType.Int64;
                getPaging0Param2.ParameterName = "@count";
                getPaging0Command.Parameters.Add(getPaging0Param2);

                getPaging0Param1.Value = inCursor;
                getPaging0Param2.Value = count+1;

                {
                    await using (var rdr = await getPaging0Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        var result = new List<CircleRecord>();
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
