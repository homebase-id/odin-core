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
    public class ImFollowingOldRecord
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
        private OdinId _identity;
        public OdinId identity
        {
           get {
                   return _identity;
               }
           set {
                  _identity = value;
               }
        }
        private Guid _driveId;
        public Guid driveId
        {
           get {
                   return _driveId;
               }
           set {
                  _driveId = value;
               }
        }
        private UnixTimeUtc _created;
        public UnixTimeUtc created
        {
           get {
                   return _created;
               }
           set {
                  _created = value;
               }
        }
        private UnixTimeUtc? _modified;
        public UnixTimeUtc? modified
        {
           get {
                   return _modified;
               }
           set {
                  _modified = value;
               }
        }
    } // End of class ImFollowingOldRecord

    public abstract class TableImFollowingOldCRUD
    {
        private readonly CacheHelper _cache;
        private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory;

        protected TableImFollowingOldCRUD(CacheHelper cache, ScopedIdentityConnectionFactory scopedConnectionFactory)
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
                cmd.CommandText = "DROP TABLE IF EXISTS ImFollowingOld;";
                await cmd.ExecuteNonQueryAsync();
            }
            var rowid = "";
            if (_scopedConnectionFactory.DatabaseType == DatabaseType.Postgres)
            {
                   rowid = ", rowid BIGSERIAL NOT NULL UNIQUE ";
            }
            cmd.CommandText =
                "CREATE TABLE IF NOT EXISTS ImFollowingOld("
                   +"identityId BYTEA NOT NULL, "
                   +"identity TEXT NOT NULL, "
                   +"driveId BYTEA NOT NULL, "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT  "
                   + rowid
                   +", PRIMARY KEY (identityId,identity,driveId)"
                   +");"
                   +"CREATE INDEX IF NOT EXISTS Idx0ImFollowingOld ON ImFollowingOld(identityId,identity);"
                   ;
            await cmd.ExecuteNonQueryAsync();
        }

        public virtual async Task<int> InsertAsync(ImFollowingOldRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO ImFollowingOld (identityId,identity,driveId,created,modified) " +
                                             "VALUES (@identityId,@identity,@driveId,@created,@modified)";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.ParameterName = "@identity";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.ParameterName = "@driveId";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.ParameterName = "@created";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.ParameterName = "@modified";
                insertCommand.Parameters.Add(insertParam5);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.identity.DomainName;
                insertParam3.Value = item.driveId.ToByteArray();
                var now = UnixTimeUtc.Now();
                insertParam4.Value = now.milliseconds;
                item.modified = null;
                insertParam5.Value = DBNull.Value;
                var count = await insertCommand.ExecuteNonQueryAsync();
                if (count > 0)
                {
                     item.created = now;
                    _cache.AddOrUpdate("TableImFollowingOldCRUD", item.identityId.ToString()+item.identity.DomainName+item.driveId.ToString(), item);
                }
                return count;
            }
        }

        public virtual async Task<bool> TryInsertAsync(ImFollowingOldRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO ImFollowingOld (identityId,identity,driveId,created,modified) " +
                                             "VALUES (@identityId,@identity,@driveId,@created,@modified) " +
                                             "ON CONFLICT DO NOTHING";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.ParameterName = "@identity";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.ParameterName = "@driveId";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.ParameterName = "@created";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.ParameterName = "@modified";
                insertCommand.Parameters.Add(insertParam5);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.identity.DomainName;
                insertParam3.Value = item.driveId.ToByteArray();
                var now = UnixTimeUtc.Now();
                insertParam4.Value = now.milliseconds;
                item.modified = null;
                insertParam5.Value = DBNull.Value;
                var count = await insertCommand.ExecuteNonQueryAsync();
                if (count > 0)
                {
                    item.created = now;
                   _cache.AddOrUpdate("TableImFollowingOldCRUD", item.identityId.ToString()+item.identity.DomainName+item.driveId.ToString(), item);
                }
                return count > 0;
            }
        }

        public virtual async Task<int> UpsertAsync(ImFollowingOldRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                upsertCommand.CommandText = "INSERT INTO ImFollowingOld (identityId,identity,driveId,created) " +
                                             "VALUES (@identityId,@identity,@driveId,@created)"+
                                             "ON CONFLICT (identityId,identity,driveId) DO UPDATE "+
                                             "SET modified = @modified "+
                                             "RETURNING created, modified;";
                var upsertParam1 = upsertCommand.CreateParameter();
                upsertParam1.ParameterName = "@identityId";
                upsertCommand.Parameters.Add(upsertParam1);
                var upsertParam2 = upsertCommand.CreateParameter();
                upsertParam2.ParameterName = "@identity";
                upsertCommand.Parameters.Add(upsertParam2);
                var upsertParam3 = upsertCommand.CreateParameter();
                upsertParam3.ParameterName = "@driveId";
                upsertCommand.Parameters.Add(upsertParam3);
                var upsertParam4 = upsertCommand.CreateParameter();
                upsertParam4.ParameterName = "@created";
                upsertCommand.Parameters.Add(upsertParam4);
                var upsertParam5 = upsertCommand.CreateParameter();
                upsertParam5.ParameterName = "@modified";
                upsertCommand.Parameters.Add(upsertParam5);
                var now = UnixTimeUtc.Now();
                upsertParam1.Value = item.identityId.ToByteArray();
                upsertParam2.Value = item.identity.DomainName;
                upsertParam3.Value = item.driveId.ToByteArray();
                upsertParam4.Value = now.milliseconds;
                upsertParam5.Value = now.milliseconds;
                await using var rdr = await upsertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                   long created = (long) rdr[0];
                   long? modified = (rdr[1] == DBNull.Value) ? null : (long) rdr[1];
                   item.created = new UnixTimeUtc(created);
                   if (modified != null)
                      item.modified = new UnixTimeUtc((long)modified);
                   else
                      item.modified = null;
                   _cache.AddOrUpdate("TableImFollowingOldCRUD", item.identityId.ToString()+item.identity.DomainName+item.driveId.ToString(), item);
                   return 1;
                }
                return 0;
            }
        }

        public virtual async Task<int> UpdateAsync(ImFollowingOldRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                updateCommand.CommandText = "UPDATE ImFollowingOld " +
                                             "SET modified = @modified "+
                                             "WHERE (identityId = @identityId AND identity = @identity AND driveId = @driveId)";
                var updateParam1 = updateCommand.CreateParameter();
                updateParam1.ParameterName = "@identityId";
                updateCommand.Parameters.Add(updateParam1);
                var updateParam2 = updateCommand.CreateParameter();
                updateParam2.ParameterName = "@identity";
                updateCommand.Parameters.Add(updateParam2);
                var updateParam3 = updateCommand.CreateParameter();
                updateParam3.ParameterName = "@driveId";
                updateCommand.Parameters.Add(updateParam3);
                var updateParam4 = updateCommand.CreateParameter();
                updateParam4.ParameterName = "@created";
                updateCommand.Parameters.Add(updateParam4);
                var updateParam5 = updateCommand.CreateParameter();
                updateParam5.ParameterName = "@modified";
                updateCommand.Parameters.Add(updateParam5);
                var now = UnixTimeUtc.Now();
                updateParam1.Value = item.identityId.ToByteArray();
                updateParam2.Value = item.identity.DomainName;
                updateParam3.Value = item.driveId.ToByteArray();
                updateParam4.Value = now.milliseconds;
                updateParam5.Value = now.milliseconds;
                var count = await updateCommand.ExecuteNonQueryAsync();
                if (count > 0)
                {
                     item.modified = now;
                    _cache.AddOrUpdate("TableImFollowingOldCRUD", item.identityId.ToString()+item.identity.DomainName+item.driveId.ToString(), item);
                }
                return count;
            }
        }

        public virtual async Task<int> RenameAsync()
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getCountCommand = cn.CreateCommand();
            {
                getCountCommand.CommandText = "ALTER TABLE imFollowing RENAME TO ImFollowingOld;";
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
                getCountCommand.CommandText = "SELECT COUNT(*) FROM ImFollowingOld;";
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
            sl.Add("identity");
            sl.Add("driveId");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        // SELECT identityId,identity,driveId,created,modified
        public ImFollowingOldRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<ImFollowingOldRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new ImFollowingOldRecord();
            item.identityId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[0]);
            item.identity = (rdr[1] == DBNull.Value) ?                 throw new Exception("item is NULL, but set as NOT NULL") : new OdinId((string)rdr[1]);
            item.driveId = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[2]);
            item.created = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[3]);
            item.modified = (rdr[4] == DBNull.Value) ? null : new UnixTimeUtc((long)rdr[4]);
            return item;
       }

        public virtual async Task<int> DeleteAsync(Guid identityId,OdinId identity,Guid driveId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM ImFollowingOld " +
                                             "WHERE identityId = @identityId AND identity = @identity AND driveId = @driveId";
                var delete0Param1 = delete0Command.CreateParameter();
                delete0Param1.ParameterName = "@identityId";
                delete0Command.Parameters.Add(delete0Param1);
                var delete0Param2 = delete0Command.CreateParameter();
                delete0Param2.ParameterName = "@identity";
                delete0Command.Parameters.Add(delete0Param2);
                var delete0Param3 = delete0Command.CreateParameter();
                delete0Param3.ParameterName = "@driveId";
                delete0Command.Parameters.Add(delete0Param3);

                delete0Param1.Value = identityId.ToByteArray();
                delete0Param2.Value = identity.DomainName;
                delete0Param3.Value = driveId.ToByteArray();
                var count = await delete0Command.ExecuteNonQueryAsync();
                if (count > 0)
                    _cache.Remove("TableImFollowingOldCRUD", identityId.ToString()+identity.DomainName+driveId.ToString());
                return count;
            }
        }

        public ImFollowingOldRecord ReadRecordFromReader0(DbDataReader rdr,Guid identityId,OdinId identity)
        {
            var result = new List<ImFollowingOldRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new ImFollowingOldRecord();
            item.identityId = identityId;
            item.identity = identity;
            item.driveId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[0]);
            item.created = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[1]);
            item.modified = (rdr[2] == DBNull.Value) ? null : new UnixTimeUtc((long)rdr[2]);
            return item;
       }

        public virtual async Task<List<ImFollowingOldRecord>> GetAsync(Guid identityId,OdinId identity)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT driveId,created,modified FROM ImFollowingOld " +
                                             "WHERE identityId = @identityId AND identity = @identity;"+
                                             ";";
                var get0Param1 = get0Command.CreateParameter();
                get0Param1.ParameterName = "@identityId";
                get0Command.Parameters.Add(get0Param1);
                var get0Param2 = get0Command.CreateParameter();
                get0Param2.ParameterName = "@identity";
                get0Command.Parameters.Add(get0Param2);

                get0Param1.Value = identityId.ToByteArray();
                get0Param2.Value = identity.DomainName;
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            _cache.AddOrUpdate("TableImFollowingOldCRUD", identityId.ToString()+identity.DomainName, null);
                            return new List<ImFollowingOldRecord>();
                        }
                        var result = new List<ImFollowingOldRecord>();
                        while (true)
                        {
                            result.Add(ReadRecordFromReader0(rdr,identityId,identity));
                            if (!await rdr.ReadAsync())
                                break;
                        }
                        return result;
                    } // using
                } //
            } // using
        }

        public ImFollowingOldRecord ReadRecordFromReader1(DbDataReader rdr)
        {
            var result = new List<ImFollowingOldRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new ImFollowingOldRecord();
            item.identityId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[0]);
            item.identity = (rdr[1] == DBNull.Value) ?                 throw new Exception("item is NULL, but set as NOT NULL") : new OdinId((string)rdr[1]);
            item.driveId = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[2]);
            item.created = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[3]);
            item.modified = (rdr[4] == DBNull.Value) ? null : new UnixTimeUtc((long)rdr[4]);
            return item;
       }

        public virtual async Task<List<ImFollowingOldRecord>> GetAllAsync()
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get1Command = cn.CreateCommand();
            {
                get1Command.CommandText = "SELECT identityId,identity,driveId,created,modified FROM ImFollowingOld " +
                                             ";";

                {
                    using (var rdr = await get1Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return new List<ImFollowingOldRecord>();
                        }
                        var result = new List<ImFollowingOldRecord>();
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

        public ImFollowingOldRecord ReadRecordFromReader2(DbDataReader rdr,Guid identityId,OdinId identity,Guid driveId)
        {
            var result = new List<ImFollowingOldRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new ImFollowingOldRecord();
            item.identityId = identityId;
            item.identity = identity;
            item.driveId = driveId;
            item.created = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[0]);
            item.modified = (rdr[1] == DBNull.Value) ? null : new UnixTimeUtc((long)rdr[1]);
            return item;
       }

        public virtual async Task<ImFollowingOldRecord> GetAsync(Guid identityId,OdinId identity,Guid driveId)
        {
            var (hit, cacheObject) = _cache.Get("TableImFollowingOldCRUD", identityId.ToString()+identity.DomainName+driveId.ToString());
            if (hit)
                return (ImFollowingOldRecord)cacheObject;
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get2Command = cn.CreateCommand();
            {
                get2Command.CommandText = "SELECT created,modified FROM ImFollowingOld " +
                                             "WHERE identityId = @identityId AND identity = @identity AND driveId = @driveId LIMIT 1;"+
                                             ";";
                var get2Param1 = get2Command.CreateParameter();
                get2Param1.ParameterName = "@identityId";
                get2Command.Parameters.Add(get2Param1);
                var get2Param2 = get2Command.CreateParameter();
                get2Param2.ParameterName = "@identity";
                get2Command.Parameters.Add(get2Param2);
                var get2Param3 = get2Command.CreateParameter();
                get2Param3.ParameterName = "@driveId";
                get2Command.Parameters.Add(get2Param3);

                get2Param1.Value = identityId.ToByteArray();
                get2Param2.Value = identity.DomainName;
                get2Param3.Value = driveId.ToByteArray();
                {
                    using (var rdr = await get2Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            _cache.AddOrUpdate("TableImFollowingOldCRUD", identityId.ToString()+identity.DomainName+driveId.ToString(), null);
                            return null;
                        }
                        var r = ReadRecordFromReader2(rdr,identityId,identity,driveId);
                        _cache.AddOrUpdate("TableImFollowingOldCRUD", identityId.ToString()+identity.DomainName+driveId.ToString(), r);
                        return r;
                    } // using
                } //
            } // using
        }

    }
}
