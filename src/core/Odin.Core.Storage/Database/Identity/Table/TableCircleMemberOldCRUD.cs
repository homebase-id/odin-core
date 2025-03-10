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
    public class CircleMemberOldRecord
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
        private Guid _memberId;
        public Guid memberId
        {
           get {
                   return _memberId;
               }
           set {
                  _memberId = value;
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
    } // End of class CircleMemberOldRecord

    public abstract class TableCircleMemberOldCRUD
    {
        private readonly CacheHelper _cache;
        private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory;

        protected TableCircleMemberOldCRUD(CacheHelper cache, ScopedIdentityConnectionFactory scopedConnectionFactory)
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
                cmd.CommandText = "DROP TABLE IF EXISTS CircleMemberOld;";
                await cmd.ExecuteNonQueryAsync();
            }
            var rowid = "";
            if (_scopedConnectionFactory.DatabaseType == DatabaseType.Postgres)
            {
                   rowid = ", rowid BIGSERIAL NOT NULL UNIQUE ";
            }
            cmd.CommandText =
                "CREATE TABLE IF NOT EXISTS CircleMemberOld("
                   +"identityId BYTEA NOT NULL, "
                   +"circleId BYTEA NOT NULL, "
                   +"memberId BYTEA NOT NULL, "
                   +"data BYTEA  "
                   + rowid
                   +", PRIMARY KEY (identityId,circleId,memberId)"
                   +");"
                   ;
            await cmd.ExecuteNonQueryAsync();
        }

        public virtual async Task<int> InsertAsync(CircleMemberOldRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.circleId.AssertGuidNotEmpty("Guid parameter circleId cannot be set to Empty GUID.");
            item.memberId.AssertGuidNotEmpty("Guid parameter memberId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO CircleMemberOld (identityId,circleId,memberId,data) " +
                                             "VALUES (@identityId,@circleId,@memberId,@data)";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.ParameterName = "@circleId";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.ParameterName = "@memberId";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.ParameterName = "@data";
                insertCommand.Parameters.Add(insertParam4);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.circleId.ToByteArray();
                insertParam3.Value = item.memberId.ToByteArray();
                insertParam4.Value = item.data ?? (object)DBNull.Value;
                var count = await insertCommand.ExecuteNonQueryAsync();
                if (count > 0)
                {
                    _cache.AddOrUpdate("TableCircleMemberOldCRUD", item.identityId.ToString()+item.circleId.ToString()+item.memberId.ToString(), item);
                }
                return count;
            }
        }

        public virtual async Task<bool> TryInsertAsync(CircleMemberOldRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.circleId.AssertGuidNotEmpty("Guid parameter circleId cannot be set to Empty GUID.");
            item.memberId.AssertGuidNotEmpty("Guid parameter memberId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO CircleMemberOld (identityId,circleId,memberId,data) " +
                                             "VALUES (@identityId,@circleId,@memberId,@data) " +
                                             "ON CONFLICT DO NOTHING";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.ParameterName = "@circleId";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.ParameterName = "@memberId";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.ParameterName = "@data";
                insertCommand.Parameters.Add(insertParam4);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.circleId.ToByteArray();
                insertParam3.Value = item.memberId.ToByteArray();
                insertParam4.Value = item.data ?? (object)DBNull.Value;
                var count = await insertCommand.ExecuteNonQueryAsync();
                if (count > 0)
                {
                   _cache.AddOrUpdate("TableCircleMemberOldCRUD", item.identityId.ToString()+item.circleId.ToString()+item.memberId.ToString(), item);
                }
                return count > 0;
            }
        }

        public virtual async Task<int> UpsertAsync(CircleMemberOldRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.circleId.AssertGuidNotEmpty("Guid parameter circleId cannot be set to Empty GUID.");
            item.memberId.AssertGuidNotEmpty("Guid parameter memberId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                upsertCommand.CommandText = "INSERT INTO CircleMemberOld (identityId,circleId,memberId,data) " +
                                             "VALUES (@identityId,@circleId,@memberId,@data)"+
                                             "ON CONFLICT (identityId,circleId,memberId) DO UPDATE "+
                                             "SET data = @data "+
                                             ";";
                var upsertParam1 = upsertCommand.CreateParameter();
                upsertParam1.ParameterName = "@identityId";
                upsertCommand.Parameters.Add(upsertParam1);
                var upsertParam2 = upsertCommand.CreateParameter();
                upsertParam2.ParameterName = "@circleId";
                upsertCommand.Parameters.Add(upsertParam2);
                var upsertParam3 = upsertCommand.CreateParameter();
                upsertParam3.ParameterName = "@memberId";
                upsertCommand.Parameters.Add(upsertParam3);
                var upsertParam4 = upsertCommand.CreateParameter();
                upsertParam4.ParameterName = "@data";
                upsertCommand.Parameters.Add(upsertParam4);
                upsertParam1.Value = item.identityId.ToByteArray();
                upsertParam2.Value = item.circleId.ToByteArray();
                upsertParam3.Value = item.memberId.ToByteArray();
                upsertParam4.Value = item.data ?? (object)DBNull.Value;
                var count = await upsertCommand.ExecuteNonQueryAsync();
                if (count > 0)
                    _cache.AddOrUpdate("TableCircleMemberOldCRUD", item.identityId.ToString()+item.circleId.ToString()+item.memberId.ToString(), item);
                return count;
            }
        }
        public virtual async Task<int> UpdateAsync(CircleMemberOldRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.circleId.AssertGuidNotEmpty("Guid parameter circleId cannot be set to Empty GUID.");
            item.memberId.AssertGuidNotEmpty("Guid parameter memberId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                updateCommand.CommandText = "UPDATE CircleMemberOld " +
                                             "SET data = @data "+
                                             "WHERE (identityId = @identityId AND circleId = @circleId AND memberId = @memberId)";
                var updateParam1 = updateCommand.CreateParameter();
                updateParam1.ParameterName = "@identityId";
                updateCommand.Parameters.Add(updateParam1);
                var updateParam2 = updateCommand.CreateParameter();
                updateParam2.ParameterName = "@circleId";
                updateCommand.Parameters.Add(updateParam2);
                var updateParam3 = updateCommand.CreateParameter();
                updateParam3.ParameterName = "@memberId";
                updateCommand.Parameters.Add(updateParam3);
                var updateParam4 = updateCommand.CreateParameter();
                updateParam4.ParameterName = "@data";
                updateCommand.Parameters.Add(updateParam4);
                updateParam1.Value = item.identityId.ToByteArray();
                updateParam2.Value = item.circleId.ToByteArray();
                updateParam3.Value = item.memberId.ToByteArray();
                updateParam4.Value = item.data ?? (object)DBNull.Value;
                var count = await updateCommand.ExecuteNonQueryAsync();
                if (count > 0)
                {
                    _cache.AddOrUpdate("TableCircleMemberOldCRUD", item.identityId.ToString()+item.circleId.ToString()+item.memberId.ToString(), item);
                }
                return count;
            }
        }

        public virtual async Task<int> RenameAsync()
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getCountCommand = cn.CreateCommand();
            {
                getCountCommand.CommandText = "ALTER TABLE circleMember RENAME TO CircleMemberOld;";
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
                getCountCommand.CommandText = "SELECT COUNT(*) FROM CircleMemberOld;";
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
            sl.Add("circleId");
            sl.Add("memberId");
            sl.Add("data");
            return sl;
        }

        // SELECT identityId,circleId,memberId,data
        public CircleMemberOldRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<CircleMemberOldRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new CircleMemberOldRecord();
            item.identityId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[0]);
            item.circleId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.memberId = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[2]);
            item.dataNoLengthCheck = (rdr[3] == DBNull.Value) ? null : (byte[])(rdr[3]);
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            return item;
       }

        public virtual async Task<int> DeleteAsync(Guid identityId,Guid circleId,Guid memberId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM CircleMemberOld " +
                                             "WHERE identityId = @identityId AND circleId = @circleId AND memberId = @memberId";
                var delete0Param1 = delete0Command.CreateParameter();
                delete0Param1.ParameterName = "@identityId";
                delete0Command.Parameters.Add(delete0Param1);
                var delete0Param2 = delete0Command.CreateParameter();
                delete0Param2.ParameterName = "@circleId";
                delete0Command.Parameters.Add(delete0Param2);
                var delete0Param3 = delete0Command.CreateParameter();
                delete0Param3.ParameterName = "@memberId";
                delete0Command.Parameters.Add(delete0Param3);

                delete0Param1.Value = identityId.ToByteArray();
                delete0Param2.Value = circleId.ToByteArray();
                delete0Param3.Value = memberId.ToByteArray();
                var count = await delete0Command.ExecuteNonQueryAsync();
                if (count > 0)
                    _cache.Remove("TableCircleMemberOldCRUD", identityId.ToString()+circleId.ToString()+memberId.ToString());
                return count;
            }
        }

        public CircleMemberOldRecord ReadRecordFromReader0(DbDataReader rdr,Guid identityId,Guid circleId)
        {
            var result = new List<CircleMemberOldRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new CircleMemberOldRecord();
            item.identityId = identityId;
            item.circleId = circleId;
            item.memberId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[0]);
            item.dataNoLengthCheck = (rdr[1] == DBNull.Value) ? null : (byte[])(rdr[1]);
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            return item;
       }

        public virtual async Task<List<CircleMemberOldRecord>> GetCircleMembersAsync(Guid identityId,Guid circleId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT memberId,data FROM CircleMemberOld " +
                                             "WHERE identityId = @identityId AND circleId = @circleId;"+
                                             ";";
                var get0Param1 = get0Command.CreateParameter();
                get0Param1.ParameterName = "@identityId";
                get0Command.Parameters.Add(get0Param1);
                var get0Param2 = get0Command.CreateParameter();
                get0Param2.ParameterName = "@circleId";
                get0Command.Parameters.Add(get0Param2);

                get0Param1.Value = identityId.ToByteArray();
                get0Param2.Value = circleId.ToByteArray();
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            _cache.AddOrUpdate("TableCircleMemberOldCRUD", identityId.ToString()+circleId.ToString(), null);
                            return new List<CircleMemberOldRecord>();
                        }
                        var result = new List<CircleMemberOldRecord>();
                        while (true)
                        {
                            result.Add(ReadRecordFromReader0(rdr,identityId,circleId));
                            if (!await rdr.ReadAsync())
                                break;
                        }
                        return result;
                    } // using
                } //
            } // using
        }

        public CircleMemberOldRecord ReadRecordFromReader1(DbDataReader rdr,Guid identityId,Guid memberId)
        {
            var result = new List<CircleMemberOldRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new CircleMemberOldRecord();
            item.identityId = identityId;
            item.memberId = memberId;
            item.circleId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[0]);
            item.dataNoLengthCheck = (rdr[1] == DBNull.Value) ? null : (byte[])(rdr[1]);
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            return item;
       }

        public virtual async Task<List<CircleMemberOldRecord>> GetMemberCirclesAndDataAsync(Guid identityId,Guid memberId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get1Command = cn.CreateCommand();
            {
                get1Command.CommandText = "SELECT circleId,data FROM CircleMemberOld " +
                                             "WHERE identityId = @identityId AND memberId = @memberId;"+
                                             ";";
                var get1Param1 = get1Command.CreateParameter();
                get1Param1.ParameterName = "@identityId";
                get1Command.Parameters.Add(get1Param1);
                var get1Param2 = get1Command.CreateParameter();
                get1Param2.ParameterName = "@memberId";
                get1Command.Parameters.Add(get1Param2);

                get1Param1.Value = identityId.ToByteArray();
                get1Param2.Value = memberId.ToByteArray();
                {
                    using (var rdr = await get1Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            _cache.AddOrUpdate("TableCircleMemberOldCRUD", identityId.ToString()+memberId.ToString(), null);
                            return new List<CircleMemberOldRecord>();
                        }
                        var result = new List<CircleMemberOldRecord>();
                        while (true)
                        {
                            result.Add(ReadRecordFromReader1(rdr,identityId,memberId));
                            if (!await rdr.ReadAsync())
                                break;
                        }
                        return result;
                    } // using
                } //
            } // using
        }

        public CircleMemberOldRecord ReadRecordFromReader2(DbDataReader rdr)
        {
            var result = new List<CircleMemberOldRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new CircleMemberOldRecord();
            item.identityId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[0]);
            item.circleId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.memberId = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[2]);
            item.dataNoLengthCheck = (rdr[3] == DBNull.Value) ? null : (byte[])(rdr[3]);
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            return item;
       }

        public virtual async Task<List<CircleMemberOldRecord>> GetAllAsync()
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get2Command = cn.CreateCommand();
            {
                get2Command.CommandText = "SELECT identityId,circleId,memberId,data FROM CircleMemberOld " +
                                             ";";

                {
                    using (var rdr = await get2Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return new List<CircleMemberOldRecord>();
                        }
                        var result = new List<CircleMemberOldRecord>();
                        while (true)
                        {
                            result.Add(ReadRecordFromReader2(rdr));
                            if (!await rdr.ReadAsync())
                                break;
                        }
                        return result;
                    } // using
                } //
            } // using
        }

        public CircleMemberOldRecord ReadRecordFromReader3(DbDataReader rdr,Guid identityId,Guid circleId,Guid memberId)
        {
            var result = new List<CircleMemberOldRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new CircleMemberOldRecord();
            item.identityId = identityId;
            item.circleId = circleId;
            item.memberId = memberId;
            item.dataNoLengthCheck = (rdr[0] == DBNull.Value) ? null : (byte[])(rdr[0]);
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            return item;
       }

        public virtual async Task<CircleMemberOldRecord> GetAsync(Guid identityId,Guid circleId,Guid memberId)
        {
            var (hit, cacheObject) = _cache.Get("TableCircleMemberOldCRUD", identityId.ToString()+circleId.ToString()+memberId.ToString());
            if (hit)
                return (CircleMemberOldRecord)cacheObject;
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get3Command = cn.CreateCommand();
            {
                get3Command.CommandText = "SELECT data FROM CircleMemberOld " +
                                             "WHERE identityId = @identityId AND circleId = @circleId AND memberId = @memberId LIMIT 1;"+
                                             ";";
                var get3Param1 = get3Command.CreateParameter();
                get3Param1.ParameterName = "@identityId";
                get3Command.Parameters.Add(get3Param1);
                var get3Param2 = get3Command.CreateParameter();
                get3Param2.ParameterName = "@circleId";
                get3Command.Parameters.Add(get3Param2);
                var get3Param3 = get3Command.CreateParameter();
                get3Param3.ParameterName = "@memberId";
                get3Command.Parameters.Add(get3Param3);

                get3Param1.Value = identityId.ToByteArray();
                get3Param2.Value = circleId.ToByteArray();
                get3Param3.Value = memberId.ToByteArray();
                {
                    using (var rdr = await get3Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            _cache.AddOrUpdate("TableCircleMemberOldCRUD", identityId.ToString()+circleId.ToString()+memberId.ToString(), null);
                            return null;
                        }
                        var r = ReadRecordFromReader3(rdr,identityId,circleId,memberId);
                        _cache.AddOrUpdate("TableCircleMemberOldCRUD", identityId.ToString()+circleId.ToString()+memberId.ToString(), r);
                        return r;
                    } // using
                } //
            } // using
        }

    }
}
