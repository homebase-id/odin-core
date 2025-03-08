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

// THIS FILE IS AUTO GENERATED - DO NOT EDIT

namespace Odin.Core.Storage.Database.Identity.Table
{
    public class AppGrantsOldRecord
    {
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
                    if (value?.Length < 0) throw new Exception("Too short");
                    if (value?.Length > 65535) throw new Exception("Too long");
                  _data = value;
               }
        }
        internal byte[] dataNoLengthCheck
        {
           get {
                   return _data;
               }
           set {
                    if (value?.Length < 0) throw new Exception("Too short");
                  _data = value;
               }
        }
    } // End of class AppGrantsOldRecord

    public abstract class TableAppGrantsOldCRUD
    {
        private readonly CacheHelper _cache;
        private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory;

        protected TableAppGrantsOldCRUD(CacheHelper cache, ScopedIdentityConnectionFactory scopedConnectionFactory)
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
                cmd.CommandText = "DROP TABLE IF EXISTS AppGrantsOld;";
                await cmd.ExecuteNonQueryAsync();
            }
            var rowid = "";
            if (_scopedConnectionFactory.DatabaseType == DatabaseType.Postgres)
            {
                   rowid = ", rowid BIGSERIAL NOT NULL UNIQUE ";
            }
            cmd.CommandText =
                "CREATE TABLE IF NOT EXISTS AppGrantsOld("
                   +"identityId BYTEA NOT NULL, "
                   +"odinHashId BYTEA NOT NULL, "
                   +"appId BYTEA NOT NULL, "
                   +"circleId BYTEA NOT NULL, "
                   +"data BYTEA  "
                   + rowid
                   +", PRIMARY KEY (identityId,odinHashId,appId,circleId)"
                   +");"
                   ;
            await cmd.ExecuteNonQueryAsync();
        }

        public virtual async Task<int> InsertAsync(AppGrantsOldRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.odinHashId.AssertGuidNotEmpty("Guid parameter odinHashId cannot be set to Empty GUID.");
            item.appId.AssertGuidNotEmpty("Guid parameter appId cannot be set to Empty GUID.");
            item.circleId.AssertGuidNotEmpty("Guid parameter circleId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO AppGrantsOld (identityId,odinHashId,appId,circleId,data) " +
                                             "VALUES (@identityId,@odinHashId,@appId,@circleId,@data)";
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
                var count = await insertCommand.ExecuteNonQueryAsync();
                if (count > 0)
                {
                    _cache.AddOrUpdate("TableAppGrantsOldCRUD", item.identityId.ToString()+item.odinHashId.ToString()+item.appId.ToString()+item.circleId.ToString(), item);
                }
                return count;
            }
        }

        public virtual async Task<bool> TryInsertAsync(AppGrantsOldRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.odinHashId.AssertGuidNotEmpty("Guid parameter odinHashId cannot be set to Empty GUID.");
            item.appId.AssertGuidNotEmpty("Guid parameter appId cannot be set to Empty GUID.");
            item.circleId.AssertGuidNotEmpty("Guid parameter circleId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO AppGrantsOld (identityId,odinHashId,appId,circleId,data) " +
                                             "VALUES (@identityId,@odinHashId,@appId,@circleId,@data) " +
                                             "ON CONFLICT DO NOTHING";
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
                var count = await insertCommand.ExecuteNonQueryAsync();
                if (count > 0)
                {
                   _cache.AddOrUpdate("TableAppGrantsOldCRUD", item.identityId.ToString()+item.odinHashId.ToString()+item.appId.ToString()+item.circleId.ToString(), item);
                }
                return count > 0;
            }
        }

        public virtual async Task<int> UpsertAsync(AppGrantsOldRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.odinHashId.AssertGuidNotEmpty("Guid parameter odinHashId cannot be set to Empty GUID.");
            item.appId.AssertGuidNotEmpty("Guid parameter appId cannot be set to Empty GUID.");
            item.circleId.AssertGuidNotEmpty("Guid parameter circleId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                upsertCommand.CommandText = "INSERT INTO AppGrantsOld (identityId,odinHashId,appId,circleId,data) " +
                                             "VALUES (@identityId,@odinHashId,@appId,@circleId,@data)"+
                                             "ON CONFLICT (identityId,odinHashId,appId,circleId) DO UPDATE "+
                                             "SET data = @data "+
                                             ";";
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
                var count = await upsertCommand.ExecuteNonQueryAsync();
                if (count > 0)
                    _cache.AddOrUpdate("TableAppGrantsOldCRUD", item.identityId.ToString()+item.odinHashId.ToString()+item.appId.ToString()+item.circleId.ToString(), item);
                return count;
            }
        }
        public virtual async Task<int> UpdateAsync(AppGrantsOldRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.odinHashId.AssertGuidNotEmpty("Guid parameter odinHashId cannot be set to Empty GUID.");
            item.appId.AssertGuidNotEmpty("Guid parameter appId cannot be set to Empty GUID.");
            item.circleId.AssertGuidNotEmpty("Guid parameter circleId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                updateCommand.CommandText = "UPDATE AppGrantsOld " +
                                             "SET data = @data "+
                                             "WHERE (identityId = @identityId AND odinHashId = @odinHashId AND appId = @appId AND circleId = @circleId)";
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
                var count = await updateCommand.ExecuteNonQueryAsync();
                if (count > 0)
                {
                    _cache.AddOrUpdate("TableAppGrantsOldCRUD", item.identityId.ToString()+item.odinHashId.ToString()+item.appId.ToString()+item.circleId.ToString(), item);
                }
                return count;
            }
        }

        public virtual async Task<int> RenameAsync()
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getCountCommand = cn.CreateCommand();
            {
                getCountCommand.CommandText = "ALTER TABLE appGrants RENAME TO AppGrantsOld;";
                var count = await getCountCommand.ExecuteScalarAsync();
                if (count == null || count == DBNull.Value || !(count is int || count is long))
                    return -1;
                else
                    return Convert.ToInt32(count);
            }
        }

        public virtual async Task<int> GetCountAsync()
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getCountCommand = cn.CreateCommand();
            {
                 // TODO: this is SQLite specific
                getCountCommand.CommandText = "SELECT COUNT(*) FROM AppGrantsOld;";
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
            sl.Add("identityId");
            sl.Add("odinHashId");
            sl.Add("appId");
            sl.Add("circleId");
            sl.Add("data");
            return sl;
        }

        // SELECT identityId,odinHashId,appId,circleId,data
        public AppGrantsOldRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<AppGrantsOldRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new AppGrantsOldRecord();
            item.identityId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[0]);
            item.odinHashId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.appId = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[2]);
            item.circleId = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[3]);
            item.dataNoLengthCheck = (rdr[4] == DBNull.Value) ? null : (byte[])(rdr[4]);
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            return item;
       }

        public virtual async Task<int> DeleteAsync(Guid identityId,Guid odinHashId,Guid appId,Guid circleId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM AppGrantsOld " +
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
                    _cache.Remove("TableAppGrantsOldCRUD", identityId.ToString()+odinHashId.ToString()+appId.ToString()+circleId.ToString());
                return count;
            }
        }

        public AppGrantsOldRecord ReadRecordFromReader0(DbDataReader rdr,Guid identityId,Guid odinHashId)
        {
            var result = new List<AppGrantsOldRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new AppGrantsOldRecord();
            item.identityId = identityId;
            item.odinHashId = odinHashId;
            item.appId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[0]);
            item.circleId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.dataNoLengthCheck = (rdr[2] == DBNull.Value) ? null : (byte[])(rdr[2]);
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            return item;
       }

        public virtual async Task<List<AppGrantsOldRecord>> GetByOdinHashIdAsync(Guid identityId,Guid odinHashId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT appId,circleId,data FROM AppGrantsOld " +
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
                            _cache.AddOrUpdate("TableAppGrantsOldCRUD", identityId.ToString()+odinHashId.ToString(), null);
                            return new List<AppGrantsOldRecord>();
                        }
                        var result = new List<AppGrantsOldRecord>();
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

        public AppGrantsOldRecord ReadRecordFromReader1(DbDataReader rdr)
        {
            var result = new List<AppGrantsOldRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new AppGrantsOldRecord();
            item.identityId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[0]);
            item.odinHashId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.appId = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[2]);
            item.circleId = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[3]);
            item.dataNoLengthCheck = (rdr[4] == DBNull.Value) ? null : (byte[])(rdr[4]);
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            return item;
       }

        public virtual async Task<List<AppGrantsOldRecord>> GetAllAsync()
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get1Command = cn.CreateCommand();
            {
                get1Command.CommandText = "SELECT identityId,odinHashId,appId,circleId,data FROM AppGrantsOld " +
                                             ";";

                {
                    using (var rdr = await get1Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return new List<AppGrantsOldRecord>();
                        }
                        var result = new List<AppGrantsOldRecord>();
                        while (true)
                        {
                            result.Add(ReadRecordFromReader1(rdr));
                            if (!await rdr.ReadAsync())
                                break;
                        }
                        return result;
                    } // using
                } //
            } // using
        }

        public AppGrantsOldRecord ReadRecordFromReader2(DbDataReader rdr,Guid identityId,Guid odinHashId,Guid appId,Guid circleId)
        {
            var result = new List<AppGrantsOldRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new AppGrantsOldRecord();
            item.identityId = identityId;
            item.odinHashId = odinHashId;
            item.appId = appId;
            item.circleId = circleId;
            item.dataNoLengthCheck = (rdr[0] == DBNull.Value) ? null : (byte[])(rdr[0]);
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            return item;
       }

        public virtual async Task<AppGrantsOldRecord> GetAsync(Guid identityId,Guid odinHashId,Guid appId,Guid circleId)
        {
            var (hit, cacheObject) = _cache.Get("TableAppGrantsOldCRUD", identityId.ToString()+odinHashId.ToString()+appId.ToString()+circleId.ToString());
            if (hit)
                return (AppGrantsOldRecord)cacheObject;
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get2Command = cn.CreateCommand();
            {
                get2Command.CommandText = "SELECT data FROM AppGrantsOld " +
                                             "WHERE identityId = @identityId AND odinHashId = @odinHashId AND appId = @appId AND circleId = @circleId LIMIT 1;"+
                                             ";";
                var get2Param1 = get2Command.CreateParameter();
                get2Param1.ParameterName = "@identityId";
                get2Command.Parameters.Add(get2Param1);
                var get2Param2 = get2Command.CreateParameter();
                get2Param2.ParameterName = "@odinHashId";
                get2Command.Parameters.Add(get2Param2);
                var get2Param3 = get2Command.CreateParameter();
                get2Param3.ParameterName = "@appId";
                get2Command.Parameters.Add(get2Param3);
                var get2Param4 = get2Command.CreateParameter();
                get2Param4.ParameterName = "@circleId";
                get2Command.Parameters.Add(get2Param4);

                get2Param1.Value = identityId.ToByteArray();
                get2Param2.Value = odinHashId.ToByteArray();
                get2Param3.Value = appId.ToByteArray();
                get2Param4.Value = circleId.ToByteArray();
                {
                    using (var rdr = await get2Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            _cache.AddOrUpdate("TableAppGrantsOldCRUD", identityId.ToString()+odinHashId.ToString()+appId.ToString()+circleId.ToString(), null);
                            return null;
                        }
                        var r = ReadRecordFromReader2(rdr,identityId,odinHashId,appId,circleId);
                        _cache.AddOrUpdate("TableAppGrantsOldCRUD", identityId.ToString()+odinHashId.ToString()+appId.ToString()+circleId.ToString(), r);
                        return r;
                    } // using
                } //
            } // using
        }

    }
}
