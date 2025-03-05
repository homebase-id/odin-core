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
    public class CircleMemberRecord
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
    } // End of class CircleMemberRecord

    public abstract class TableCircleMemberCRUD
    {
        private readonly CacheHelper _cache;
        private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory;

        protected TableCircleMemberCRUD(CacheHelper cache, ScopedIdentityConnectionFactory scopedConnectionFactory)
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
                cmd.CommandText = "DROP TABLE IF EXISTS circleMember;";
                await cmd.ExecuteNonQueryAsync();
            }
            var wori = "";
            if (_scopedConnectionFactory.DatabaseType != DatabaseType.Postgres)
                   wori = " WITHOUT ROWID";
            cmd.CommandText =
                "CREATE TABLE IF NOT EXISTS circleMember("
                   +"identityId BYTEA NOT NULL, "
                   +"circleId BYTEA NOT NULL, "
                   +"memberId BYTEA NOT NULL, "
                   +"data BYTEA  "
                   +", PRIMARY KEY (identityId,circleId,memberId)"
                   +$"){wori};"
                   ;
            await cmd.ExecuteNonQueryAsync();
        }

        protected virtual async Task<int> InsertAsync(CircleMemberRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.circleId.AssertGuidNotEmpty("Guid parameter circleId cannot be set to Empty GUID.");
            item.memberId.AssertGuidNotEmpty("Guid parameter memberId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO circleMember (identityId,circleId,memberId,data) " +
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
                    _cache.AddOrUpdate("TableCircleMemberCRUD", item.identityId.ToString()+item.circleId.ToString()+item.memberId.ToString(), item);
                }
                return count;
            }
        }

        protected virtual async Task<bool> TryInsertAsync(CircleMemberRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.circleId.AssertGuidNotEmpty("Guid parameter circleId cannot be set to Empty GUID.");
            item.memberId.AssertGuidNotEmpty("Guid parameter memberId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO circleMember (identityId,circleId,memberId,data) " +
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
                   _cache.AddOrUpdate("TableCircleMemberCRUD", item.identityId.ToString()+item.circleId.ToString()+item.memberId.ToString(), item);
                }
                return count > 0;
            }
        }

        protected virtual async Task<int> UpsertAsync(CircleMemberRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.circleId.AssertGuidNotEmpty("Guid parameter circleId cannot be set to Empty GUID.");
            item.memberId.AssertGuidNotEmpty("Guid parameter memberId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                upsertCommand.CommandText = "INSERT INTO circleMember (identityId,circleId,memberId,data) " +
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
                    _cache.AddOrUpdate("TableCircleMemberCRUD", item.identityId.ToString()+item.circleId.ToString()+item.memberId.ToString(), item);
                return count;
            }
        }
        protected virtual async Task<int> UpdateAsync(CircleMemberRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.circleId.AssertGuidNotEmpty("Guid parameter circleId cannot be set to Empty GUID.");
            item.memberId.AssertGuidNotEmpty("Guid parameter memberId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                updateCommand.CommandText = "UPDATE circleMember " +
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
                    _cache.AddOrUpdate("TableCircleMemberCRUD", item.identityId.ToString()+item.circleId.ToString()+item.memberId.ToString(), item);
                }
                return count;
            }
        }

        protected virtual async Task<int> GetCountAsync()
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getCountCommand = cn.CreateCommand();
            {
                 // TODO: this is SQLite specific
                getCountCommand.CommandText = "SELECT COUNT(*) FROM circleMember;";
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
        protected CircleMemberRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<CircleMemberRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new CircleMemberRecord();
            item.identityId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[0]);
            item.circleId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.memberId = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[2]);
            item.dataNoLengthCheck = (rdr[3] == DBNull.Value) ? null : (byte[])(rdr[3]);
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            return item;
       }

        protected virtual async Task<int> DeleteAsync(Guid identityId,Guid circleId,Guid memberId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM circleMember " +
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
                    _cache.Remove("TableCircleMemberCRUD", identityId.ToString()+circleId.ToString()+memberId.ToString());
                return count;
            }
        }

        protected CircleMemberRecord ReadRecordFromReader0(DbDataReader rdr, Guid identityId,Guid circleId,Guid memberId)
        {
            var result = new List<CircleMemberRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new CircleMemberRecord();
            item.identityId = identityId;
            item.circleId = circleId;
            item.memberId = memberId;
            item.dataNoLengthCheck = (rdr[0] == DBNull.Value) ? null : (byte[])(rdr[0]);
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            return item;
       }

        protected virtual async Task<CircleMemberRecord> GetAsync(Guid identityId,Guid circleId,Guid memberId)
        {
            var (hit, cacheObject) = _cache.Get("TableCircleMemberCRUD", identityId.ToString()+circleId.ToString()+memberId.ToString());
            if (hit)
                return (CircleMemberRecord)cacheObject;
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT data FROM circleMember " +
                                             "WHERE identityId = @identityId AND circleId = @circleId AND memberId = @memberId LIMIT 1;";
                var get0Param1 = get0Command.CreateParameter();
                get0Param1.ParameterName = "@identityId";
                get0Command.Parameters.Add(get0Param1);
                var get0Param2 = get0Command.CreateParameter();
                get0Param2.ParameterName = "@circleId";
                get0Command.Parameters.Add(get0Param2);
                var get0Param3 = get0Command.CreateParameter();
                get0Param3.ParameterName = "@memberId";
                get0Command.Parameters.Add(get0Param3);

                get0Param1.Value = identityId.ToByteArray();
                get0Param2.Value = circleId.ToByteArray();
                get0Param3.Value = memberId.ToByteArray();
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            _cache.AddOrUpdate("TableCircleMemberCRUD", identityId.ToString()+circleId.ToString()+memberId.ToString(), null);
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr, identityId,circleId,memberId);
                        _cache.AddOrUpdate("TableCircleMemberCRUD", identityId.ToString()+circleId.ToString()+memberId.ToString(), r);
                        return r;
                    } // using
                } //
            } // using
        }

        protected CircleMemberRecord ReadRecordFromReader1(DbDataReader rdr, Guid identityId,Guid circleId)
        {
            var result = new List<CircleMemberRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new CircleMemberRecord();
            item.identityId = identityId;
            item.circleId = circleId;
            item.memberId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[0]);
            item.dataNoLengthCheck = (rdr[1] == DBNull.Value) ? null : (byte[])(rdr[1]);
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            return item;
       }

        protected virtual async Task<List<CircleMemberRecord>> GetCircleMembersAsync(Guid identityId,Guid circleId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get1Command = cn.CreateCommand();
            {
                get1Command.CommandText = "SELECT memberId,data FROM circleMember " +
                                             "WHERE identityId = @identityId AND circleId = @circleId;";
                var get1Param1 = get1Command.CreateParameter();
                get1Param1.ParameterName = "@identityId";
                get1Command.Parameters.Add(get1Param1);
                var get1Param2 = get1Command.CreateParameter();
                get1Param2.ParameterName = "@circleId";
                get1Command.Parameters.Add(get1Param2);

                get1Param1.Value = identityId.ToByteArray();
                get1Param2.Value = circleId.ToByteArray();
                {
                    using (var rdr = await get1Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            _cache.AddOrUpdate("TableCircleMemberCRUD", identityId.ToString()+circleId.ToString(), null);
                            return new List<CircleMemberRecord>();
                        }
                        var result = new List<CircleMemberRecord>();
                        while (true)
                        {
                            result.Add(ReadRecordFromReader1(rdr, identityId,circleId));
                            if (!await rdr.ReadAsync())
                                break;
                        }
                        return result;
                    } // using
                } //
            } // using
        }

        protected CircleMemberRecord ReadRecordFromReader2(DbDataReader rdr, Guid identityId,Guid memberId)
        {
            var result = new List<CircleMemberRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new CircleMemberRecord();
            item.identityId = identityId;
            item.memberId = memberId;
            item.circleId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[0]);
            item.dataNoLengthCheck = (rdr[1] == DBNull.Value) ? null : (byte[])(rdr[1]);
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            return item;
       }

        protected virtual async Task<List<CircleMemberRecord>> GetMemberCirclesAndDataAsync(Guid identityId,Guid memberId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get2Command = cn.CreateCommand();
            {
                get2Command.CommandText = "SELECT circleId,data FROM circleMember " +
                                             "WHERE identityId = @identityId AND memberId = @memberId;";
                var get2Param1 = get2Command.CreateParameter();
                get2Param1.ParameterName = "@identityId";
                get2Command.Parameters.Add(get2Param1);
                var get2Param2 = get2Command.CreateParameter();
                get2Param2.ParameterName = "@memberId";
                get2Command.Parameters.Add(get2Param2);

                get2Param1.Value = identityId.ToByteArray();
                get2Param2.Value = memberId.ToByteArray();
                {
                    using (var rdr = await get2Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            _cache.AddOrUpdate("TableCircleMemberCRUD", identityId.ToString()+memberId.ToString(), null);
                            return new List<CircleMemberRecord>();
                        }
                        var result = new List<CircleMemberRecord>();
                        while (true)
                        {
                            result.Add(ReadRecordFromReader2(rdr, identityId,memberId));
                            if (!await rdr.ReadAsync())
                                break;
                        }
                        return result;
                    } // using
                } //
            } // using
        }

    }
}
