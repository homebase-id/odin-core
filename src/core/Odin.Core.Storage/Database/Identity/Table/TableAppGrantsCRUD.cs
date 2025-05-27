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

namespace Odin.Core.Storage.Database.Identity.Table
{
    public record AppGrantsRecord
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
        private Guid _identityId;
        public Guid identityId
        {
           get {
                   return _identityId;
               }
           set {
                  _identityId = value;
               }
        }
        private Guid _odinHashId;
        public Guid odinHashId
        {
           get {
                   return _odinHashId;
               }
           set {
                  _odinHashId = value;
               }
        }
        private Guid _appId;
        public Guid appId
        {
           get {
                   return _appId;
               }
           set {
                  _appId = value;
               }
        }
        private Guid _circleId;
        public Guid circleId
        {
           get {
                   return _circleId;
               }
           set {
                  _circleId = value;
               }
        }
        private byte[] _data;
        public byte[] data
        {
           get {
                   return _data;
               }
           set {
                    if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short data, was {value.Length} (min 0)");
                    if (value?.Length > 65535) throw new OdinDatabaseValidationException($"Too long data, was {value.Length} (max 65535)");
                  _data = value;
               }
        }
        internal byte[] dataNoLengthCheck
        {
           get {
                   return _data;
               }
           set {
                    if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short data, was {value.Length} (min 0)");
                  _data = value;
               }
        }
    } // End of record AppGrantsRecord

    public abstract class TableAppGrantsCRUD
    {
        private readonly CacheHelper _cache;
        private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory;

        protected TableAppGrantsCRUD(CacheHelper cache, ScopedIdentityConnectionFactory scopedConnectionFactory)
        {
            _cache = cache;
            _scopedConnectionFactory = scopedConnectionFactory;
        }


        public virtual async Task EnsureTableExistsAsync(bool dropExisting = false)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var cmd = cn.CreateCommand();
            if (dropExisting)
            {
                cmd.CommandText = "DROP TABLE IF EXISTS AppGrants;";
                await cmd.ExecuteNonQueryAsync();
            }
            var rowid = "";
            if (_scopedConnectionFactory.DatabaseType == DatabaseType.Postgres)
               rowid = "rowid BIGSERIAL PRIMARY KEY,";
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            cmd.CommandText =
                "CREATE TABLE IF NOT EXISTS AppGrants("
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"odinHashId BYTEA NOT NULL, "
                   +"appId BYTEA NOT NULL, "
                   +"circleId BYTEA NOT NULL, "
                   +"data BYTEA  "
                   +", UNIQUE(identityId,odinHashId,appId,circleId)"
                   +$"){wori};"
                   ;
            await cmd.ExecuteNonQueryAsync();
        }

        protected virtual async Task<int> InsertAsync(AppGrantsRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.odinHashId.AssertGuidNotEmpty("Guid parameter odinHashId cannot be set to Empty GUID.");
            item.appId.AssertGuidNotEmpty("Guid parameter appId cannot be set to Empty GUID.");
            item.circleId.AssertGuidNotEmpty("Guid parameter circleId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO AppGrants (identityId,odinHashId,appId,circleId,data) " +
                                             $"VALUES (@identityId,@odinHashId,@appId,@circleId,@data)"+
                                            "RETURNING -1,-1,AppGrants.rowId;";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.ParameterName = "@odinHashId";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.ParameterName = "@appId";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.ParameterName = "@circleId";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.ParameterName = "@data";
                insertCommand.Parameters.Add(insertParam5);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.odinHashId.ToByteArray();
                insertParam3.Value = item.appId.ToByteArray();
                insertParam4.Value = item.circleId.ToByteArray();
                insertParam5.Value = item.data ?? (object)DBNull.Value;
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                    _cache.AddOrUpdate("TableAppGrantsCRUD", item.identityId.ToString()+item.odinHashId.ToString()+item.appId.ToString()+item.circleId.ToString(), item);
                    return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<bool> TryInsertAsync(AppGrantsRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.odinHashId.AssertGuidNotEmpty("Guid parameter odinHashId cannot be set to Empty GUID.");
            item.appId.AssertGuidNotEmpty("Guid parameter appId cannot be set to Empty GUID.");
            item.circleId.AssertGuidNotEmpty("Guid parameter circleId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO AppGrants (identityId,odinHashId,appId,circleId,data) " +
                                            $"VALUES (@identityId,@odinHashId,@appId,@circleId,@data) " +
                                            "ON CONFLICT DO NOTHING "+
                                            "RETURNING -1,-1,AppGrants.rowId;";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.ParameterName = "@odinHashId";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.ParameterName = "@appId";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.ParameterName = "@circleId";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.ParameterName = "@data";
                insertCommand.Parameters.Add(insertParam5);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.odinHashId.ToByteArray();
                insertParam3.Value = item.appId.ToByteArray();
                insertParam4.Value = item.circleId.ToByteArray();
                insertParam5.Value = item.data ?? (object)DBNull.Value;
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                   _cache.AddOrUpdate("TableAppGrantsCRUD", item.identityId.ToString()+item.odinHashId.ToString()+item.appId.ToString()+item.circleId.ToString(), item);
                    return true;
                }
                return false;
            }
        }

        protected virtual async Task<int> UpsertAsync(AppGrantsRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.odinHashId.AssertGuidNotEmpty("Guid parameter odinHashId cannot be set to Empty GUID.");
            item.appId.AssertGuidNotEmpty("Guid parameter appId cannot be set to Empty GUID.");
            item.circleId.AssertGuidNotEmpty("Guid parameter circleId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                upsertCommand.CommandText = "INSERT INTO AppGrants (identityId,odinHashId,appId,circleId,data) " +
                                            $"VALUES (@identityId,@odinHashId,@appId,@circleId,@data)"+
                                            "ON CONFLICT (identityId,odinHashId,appId,circleId) DO UPDATE "+
                                            $"SET data = @data "+
                                            "RETURNING -1,-1,AppGrants.rowId;";
                var upsertParam1 = upsertCommand.CreateParameter();
                upsertParam1.ParameterName = "@identityId";
                upsertCommand.Parameters.Add(upsertParam1);
                var upsertParam2 = upsertCommand.CreateParameter();
                upsertParam2.ParameterName = "@odinHashId";
                upsertCommand.Parameters.Add(upsertParam2);
                var upsertParam3 = upsertCommand.CreateParameter();
                upsertParam3.ParameterName = "@appId";
                upsertCommand.Parameters.Add(upsertParam3);
                var upsertParam4 = upsertCommand.CreateParameter();
                upsertParam4.ParameterName = "@circleId";
                upsertCommand.Parameters.Add(upsertParam4);
                var upsertParam5 = upsertCommand.CreateParameter();
                upsertParam5.ParameterName = "@data";
                upsertCommand.Parameters.Add(upsertParam5);
                upsertParam1.Value = item.identityId.ToByteArray();
                upsertParam2.Value = item.odinHashId.ToByteArray();
                upsertParam3.Value = item.appId.ToByteArray();
                upsertParam4.Value = item.circleId.ToByteArray();
                upsertParam5.Value = item.data ?? (object)DBNull.Value;
                await using var rdr = await upsertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                   _cache.AddOrUpdate("TableAppGrantsCRUD", item.identityId.ToString()+item.odinHashId.ToString()+item.appId.ToString()+item.circleId.ToString(), item);
                    return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<int> UpdateAsync(AppGrantsRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.odinHashId.AssertGuidNotEmpty("Guid parameter odinHashId cannot be set to Empty GUID.");
            item.appId.AssertGuidNotEmpty("Guid parameter appId cannot be set to Empty GUID.");
            item.circleId.AssertGuidNotEmpty("Guid parameter circleId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                updateCommand.CommandText = "UPDATE AppGrants " +
                                            $"SET data = @data "+
                                            "WHERE (identityId = @identityId AND odinHashId = @odinHashId AND appId = @appId AND circleId = @circleId) "+
                                            "RETURNING -1,-1,AppGrants.rowId;";
                var updateParam1 = updateCommand.CreateParameter();
                updateParam1.ParameterName = "@identityId";
                updateCommand.Parameters.Add(updateParam1);
                var updateParam2 = updateCommand.CreateParameter();
                updateParam2.ParameterName = "@odinHashId";
                updateCommand.Parameters.Add(updateParam2);
                var updateParam3 = updateCommand.CreateParameter();
                updateParam3.ParameterName = "@appId";
                updateCommand.Parameters.Add(updateParam3);
                var updateParam4 = updateCommand.CreateParameter();
                updateParam4.ParameterName = "@circleId";
                updateCommand.Parameters.Add(updateParam4);
                var updateParam5 = updateCommand.CreateParameter();
                updateParam5.ParameterName = "@data";
                updateCommand.Parameters.Add(updateParam5);
                updateParam1.Value = item.identityId.ToByteArray();
                updateParam2.Value = item.odinHashId.ToByteArray();
                updateParam3.Value = item.appId.ToByteArray();
                updateParam4.Value = item.circleId.ToByteArray();
                updateParam5.Value = item.data ?? (object)DBNull.Value;
                await using var rdr = await updateCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                   _cache.AddOrUpdate("TableAppGrantsCRUD", item.identityId.ToString()+item.odinHashId.ToString()+item.appId.ToString()+item.circleId.ToString(), item);
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
                getCountCommand.CommandText = "SELECT COUNT(*) FROM AppGrants;";
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
            item.dataNoLengthCheck = (rdr[5] == DBNull.Value) ? null : (byte[])(rdr[5]);
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
                var delete0Param1 = delete0Command.CreateParameter();
                delete0Param1.ParameterName = "@identityId";
                delete0Command.Parameters.Add(delete0Param1);
                var delete0Param2 = delete0Command.CreateParameter();
                delete0Param2.ParameterName = "@odinHashId";
                delete0Command.Parameters.Add(delete0Param2);
                var delete0Param3 = delete0Command.CreateParameter();
                delete0Param3.ParameterName = "@appId";
                delete0Command.Parameters.Add(delete0Param3);
                var delete0Param4 = delete0Command.CreateParameter();
                delete0Param4.ParameterName = "@circleId";
                delete0Command.Parameters.Add(delete0Param4);

                delete0Param1.Value = identityId.ToByteArray();
                delete0Param2.Value = odinHashId.ToByteArray();
                delete0Param3.Value = appId.ToByteArray();
                delete0Param4.Value = circleId.ToByteArray();
                var count = await delete0Command.ExecuteNonQueryAsync();
                if (count > 0)
                    _cache.Remove("TableAppGrantsCRUD", identityId.ToString()+odinHashId.ToString()+appId.ToString()+circleId.ToString());
                return count;
            }
        }

        protected AppGrantsRecord ReadRecordFromReader0(DbDataReader rdr,Guid identityId,Guid odinHashId)
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
            item.dataNoLengthCheck = (rdr[3] == DBNull.Value) ? null : (byte[])(rdr[3]);
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            return item;
       }

        protected virtual async Task<List<AppGrantsRecord>> GetByOdinHashIdAsync(Guid identityId,Guid odinHashId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT rowId,appId,circleId,data FROM AppGrants " +
                                             "WHERE identityId = @identityId AND odinHashId = @odinHashId;"+
                                             ";";
                var get0Param1 = get0Command.CreateParameter();
                get0Param1.ParameterName = "@identityId";
                get0Command.Parameters.Add(get0Param1);
                var get0Param2 = get0Command.CreateParameter();
                get0Param2.ParameterName = "@odinHashId";
                get0Command.Parameters.Add(get0Param2);

                get0Param1.Value = identityId.ToByteArray();
                get0Param2.Value = odinHashId.ToByteArray();
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            _cache.AddOrUpdate("TableAppGrantsCRUD", identityId.ToString()+odinHashId.ToString(), null);
                            return new List<AppGrantsRecord>();
                        }
                        var result = new List<AppGrantsRecord>();
                        while (true)
                        {
                            result.Add(ReadRecordFromReader0(rdr,identityId,odinHashId));
                            if (!await rdr.ReadAsync())
                                break;
                        }
                        return result;
                    } // using
                } //
            } // using
        }

        protected AppGrantsRecord ReadRecordFromReader1(DbDataReader rdr,Guid identityId,Guid odinHashId,Guid appId,Guid circleId)
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
            item.dataNoLengthCheck = (rdr[1] == DBNull.Value) ? null : (byte[])(rdr[1]);
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            return item;
       }

        protected virtual async Task<AppGrantsRecord> GetAsync(Guid identityId,Guid odinHashId,Guid appId,Guid circleId)
        {
            var (hit, cacheObject) = _cache.Get("TableAppGrantsCRUD", identityId.ToString()+odinHashId.ToString()+appId.ToString()+circleId.ToString());
            if (hit)
                return (AppGrantsRecord)cacheObject;
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get1Command = cn.CreateCommand();
            {
                get1Command.CommandText = "SELECT rowId,data FROM AppGrants " +
                                             "WHERE identityId = @identityId AND odinHashId = @odinHashId AND appId = @appId AND circleId = @circleId LIMIT 1;"+
                                             ";";
                var get1Param1 = get1Command.CreateParameter();
                get1Param1.ParameterName = "@identityId";
                get1Command.Parameters.Add(get1Param1);
                var get1Param2 = get1Command.CreateParameter();
                get1Param2.ParameterName = "@odinHashId";
                get1Command.Parameters.Add(get1Param2);
                var get1Param3 = get1Command.CreateParameter();
                get1Param3.ParameterName = "@appId";
                get1Command.Parameters.Add(get1Param3);
                var get1Param4 = get1Command.CreateParameter();
                get1Param4.ParameterName = "@circleId";
                get1Command.Parameters.Add(get1Param4);

                get1Param1.Value = identityId.ToByteArray();
                get1Param2.Value = odinHashId.ToByteArray();
                get1Param3.Value = appId.ToByteArray();
                get1Param4.Value = circleId.ToByteArray();
                {
                    using (var rdr = await get1Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            _cache.AddOrUpdate("TableAppGrantsCRUD", identityId.ToString()+odinHashId.ToString()+appId.ToString()+circleId.ToString(), null);
                            return null;
                        }
                        var r = ReadRecordFromReader1(rdr,identityId,odinHashId,appId,circleId);
                        _cache.AddOrUpdate("TableAppGrantsCRUD", identityId.ToString()+odinHashId.ToString()+appId.ToString()+circleId.ToString(), r);
                        return r;
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
                var getPaging0Param1 = getPaging0Command.CreateParameter();
                getPaging0Param1.ParameterName = "@rowId";
                getPaging0Command.Parameters.Add(getPaging0Param1);
                var getPaging0Param2 = getPaging0Command.CreateParameter();
                getPaging0Param2.ParameterName = "@count";
                getPaging0Command.Parameters.Add(getPaging0Param2);

                getPaging0Param1.Value = inCursor;
                getPaging0Param2.Value = count+1;

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
