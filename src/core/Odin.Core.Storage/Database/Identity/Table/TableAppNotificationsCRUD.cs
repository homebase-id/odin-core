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
    public record AppNotificationsRecord
    {
        public Int64 rowId { get; set; }
        public Guid identityId { get; set; }
        public Guid notificationId { get; set; }
        public Int32 unread { get; set; }
        public string senderId { get; set; }
        public UnixTimeUtc timestamp { get; set; }
        public byte[] data { get; set; }
        public UnixTimeUtc created { get; set; }
        public UnixTimeUtc modified { get; set; }
        public void Validate()
        {
            identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            notificationId.AssertGuidNotEmpty("Guid parameter notificationId cannot be set to Empty GUID.");
            if (senderId?.Length < 0) throw new OdinDatabaseValidationException($"Too short senderId, was {senderId.Length} (min 0)");
            if (senderId?.Length > 256) throw new OdinDatabaseValidationException($"Too long senderId, was {senderId.Length} (max 256)");
            if (data?.Length < 0) throw new OdinDatabaseValidationException($"Too short data, was {data.Length} (min 0)");
            if (data?.Length > 65000) throw new OdinDatabaseValidationException($"Too long data, was {data.Length} (max 65000)");
        }
    } // End of record AppNotificationsRecord

    public abstract class TableAppNotificationsCRUD
    {
        private readonly CacheHelper _cache;
        private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory;

        protected TableAppNotificationsCRUD(CacheHelper cache, ScopedIdentityConnectionFactory scopedConnectionFactory)
        {
            _cache = cache;
            _scopedConnectionFactory = scopedConnectionFactory;
        }


        public virtual async Task EnsureTableExistsAsync(bool dropExisting = false)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            if (dropExisting)
                await MigrationBase.DeleteTableAsync(cn, "AppNotifications");
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowid BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE AppNotifications IS '{ \"Version\": 0 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS AppNotifications( -- { \"Version\": 0 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"notificationId BYTEA NOT NULL UNIQUE, "
                   +"unread BIGINT NOT NULL, "
                   +"senderId TEXT , "
                   +"timestamp BIGINT NOT NULL, "
                   +"data BYTEA , "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +", UNIQUE(identityId,notificationId)"
                   +$"){wori};"
                   +"CREATE INDEX IF NOT EXISTS Idx0AppNotifications ON AppNotifications(identityId,created);"
                   ;
            await MigrationBase.CreateTableAsync(cn, createSql, commentSql);
        }

        protected virtual async Task<int> InsertAsync(AppNotificationsRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr = insertCommand.SqlNow();
                insertCommand.CommandText = "INSERT INTO AppNotifications (identityId,notificationId,unread,senderId,timestamp,data,created,modified) " +
                                           $"VALUES (@identityId,@notificationId,@unread,@senderId,@timestamp,@data,{sqlNowStr},{sqlNowStr})"+
                                            "RETURNING created,modified,rowId;";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.DbType = DbType.Binary;
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.DbType = DbType.Binary;
                insertParam2.ParameterName = "@notificationId";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.DbType = DbType.Int32;
                insertParam3.ParameterName = "@unread";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.DbType = DbType.String;
                insertParam4.ParameterName = "@senderId";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.DbType = DbType.Int64;
                insertParam5.ParameterName = "@timestamp";
                insertCommand.Parameters.Add(insertParam5);
                var insertParam6 = insertCommand.CreateParameter();
                insertParam6.DbType = DbType.Binary;
                insertParam6.ParameterName = "@data";
                insertCommand.Parameters.Add(insertParam6);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.notificationId.ToByteArray();
                insertParam3.Value = item.unread;
                insertParam4.Value = item.senderId ?? (object)DBNull.Value;
                insertParam5.Value = item.timestamp.milliseconds;
                insertParam6.Value = item.data ?? (object)DBNull.Value;
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    item.created = new UnixTimeUtc(created);
                    long modified = (long) rdr[1];
                    item.modified = new UnixTimeUtc((long)modified);
                    item.rowId = (long) rdr[2];
                    _cache.AddOrUpdate("TableAppNotificationsCRUD", item.identityId.ToString()+item.notificationId.ToString(), item);
                    return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<bool> TryInsertAsync(AppNotificationsRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr = insertCommand.SqlNow();
                insertCommand.CommandText = "INSERT INTO AppNotifications (identityId,notificationId,unread,senderId,timestamp,data,created,modified) " +
                                            $"VALUES (@identityId,@notificationId,@unread,@senderId,@timestamp,@data,{sqlNowStr},{sqlNowStr}) " +
                                            "ON CONFLICT DO NOTHING "+
                                            "RETURNING created,modified,rowId;";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.DbType = DbType.Binary;
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.DbType = DbType.Binary;
                insertParam2.ParameterName = "@notificationId";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.DbType = DbType.Int32;
                insertParam3.ParameterName = "@unread";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.DbType = DbType.String;
                insertParam4.ParameterName = "@senderId";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.DbType = DbType.Int64;
                insertParam5.ParameterName = "@timestamp";
                insertCommand.Parameters.Add(insertParam5);
                var insertParam6 = insertCommand.CreateParameter();
                insertParam6.DbType = DbType.Binary;
                insertParam6.ParameterName = "@data";
                insertCommand.Parameters.Add(insertParam6);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.notificationId.ToByteArray();
                insertParam3.Value = item.unread;
                insertParam4.Value = item.senderId ?? (object)DBNull.Value;
                insertParam5.Value = item.timestamp.milliseconds;
                insertParam6.Value = item.data ?? (object)DBNull.Value;
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    item.created = new UnixTimeUtc(created);
                    long modified = (long) rdr[1];
                    item.modified = new UnixTimeUtc((long)modified);
                    item.rowId = (long) rdr[2];
                   _cache.AddOrUpdate("TableAppNotificationsCRUD", item.identityId.ToString()+item.notificationId.ToString(), item);
                    return true;
                }
                return false;
            }
        }

        protected virtual async Task<int> UpsertAsync(AppNotificationsRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                string sqlNowStr = upsertCommand.SqlNow();
                upsertCommand.CommandText = "INSERT INTO AppNotifications (identityId,notificationId,unread,senderId,timestamp,data,created,modified) " +
                                            $"VALUES (@identityId,@notificationId,@unread,@senderId,@timestamp,@data,{sqlNowStr},{sqlNowStr})"+
                                            "ON CONFLICT (identityId,notificationId) DO UPDATE "+
                                            $"SET unread = @unread,senderId = @senderId,timestamp = @timestamp,data = @data,modified = {upsertCommand.SqlMax()}(AppNotifications.modified+1,{sqlNowStr}) "+
                                            "RETURNING created,modified,rowId;";
                var upsertParam1 = upsertCommand.CreateParameter();
                upsertParam1.DbType = DbType.Binary;
                upsertParam1.ParameterName = "@identityId";
                upsertCommand.Parameters.Add(upsertParam1);
                var upsertParam2 = upsertCommand.CreateParameter();
                upsertParam2.DbType = DbType.Binary;
                upsertParam2.ParameterName = "@notificationId";
                upsertCommand.Parameters.Add(upsertParam2);
                var upsertParam3 = upsertCommand.CreateParameter();
                upsertParam3.DbType = DbType.Int32;
                upsertParam3.ParameterName = "@unread";
                upsertCommand.Parameters.Add(upsertParam3);
                var upsertParam4 = upsertCommand.CreateParameter();
                upsertParam4.DbType = DbType.String;
                upsertParam4.ParameterName = "@senderId";
                upsertCommand.Parameters.Add(upsertParam4);
                var upsertParam5 = upsertCommand.CreateParameter();
                upsertParam5.DbType = DbType.Int64;
                upsertParam5.ParameterName = "@timestamp";
                upsertCommand.Parameters.Add(upsertParam5);
                var upsertParam6 = upsertCommand.CreateParameter();
                upsertParam6.DbType = DbType.Binary;
                upsertParam6.ParameterName = "@data";
                upsertCommand.Parameters.Add(upsertParam6);
                upsertParam1.Value = item.identityId.ToByteArray();
                upsertParam2.Value = item.notificationId.ToByteArray();
                upsertParam3.Value = item.unread;
                upsertParam4.Value = item.senderId ?? (object)DBNull.Value;
                upsertParam5.Value = item.timestamp.milliseconds;
                upsertParam6.Value = item.data ?? (object)DBNull.Value;
                await using var rdr = await upsertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    item.created = new UnixTimeUtc(created);
                    long modified = (long) rdr[1];
                    item.modified = new UnixTimeUtc((long)modified);
                    item.rowId = (long) rdr[2];
                   _cache.AddOrUpdate("TableAppNotificationsCRUD", item.identityId.ToString()+item.notificationId.ToString(), item);
                    return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<int> UpdateAsync(AppNotificationsRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                string sqlNowStr = updateCommand.SqlNow();
                updateCommand.CommandText = "UPDATE AppNotifications " +
                                            $"SET unread = @unread,senderId = @senderId,timestamp = @timestamp,data = @data,modified = {updateCommand.SqlMax()}(AppNotifications.modified+1,{sqlNowStr}) "+
                                            "WHERE (identityId = @identityId AND notificationId = @notificationId) "+
                                            "RETURNING created,modified,rowId;";
                var updateParam1 = updateCommand.CreateParameter();
                updateParam1.DbType = DbType.Binary;
                updateParam1.ParameterName = "@identityId";
                updateCommand.Parameters.Add(updateParam1);
                var updateParam2 = updateCommand.CreateParameter();
                updateParam2.DbType = DbType.Binary;
                updateParam2.ParameterName = "@notificationId";
                updateCommand.Parameters.Add(updateParam2);
                var updateParam3 = updateCommand.CreateParameter();
                updateParam3.DbType = DbType.Int32;
                updateParam3.ParameterName = "@unread";
                updateCommand.Parameters.Add(updateParam3);
                var updateParam4 = updateCommand.CreateParameter();
                updateParam4.DbType = DbType.String;
                updateParam4.ParameterName = "@senderId";
                updateCommand.Parameters.Add(updateParam4);
                var updateParam5 = updateCommand.CreateParameter();
                updateParam5.DbType = DbType.Int64;
                updateParam5.ParameterName = "@timestamp";
                updateCommand.Parameters.Add(updateParam5);
                var updateParam6 = updateCommand.CreateParameter();
                updateParam6.DbType = DbType.Binary;
                updateParam6.ParameterName = "@data";
                updateCommand.Parameters.Add(updateParam6);
                updateParam1.Value = item.identityId.ToByteArray();
                updateParam2.Value = item.notificationId.ToByteArray();
                updateParam3.Value = item.unread;
                updateParam4.Value = item.senderId ?? (object)DBNull.Value;
                updateParam5.Value = item.timestamp.milliseconds;
                updateParam6.Value = item.data ?? (object)DBNull.Value;
                await using var rdr = await updateCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    item.created = new UnixTimeUtc(created);
                    long modified = (long) rdr[1];
                    item.modified = new UnixTimeUtc((long)modified);
                    item.rowId = (long) rdr[2];
                   _cache.AddOrUpdate("TableAppNotificationsCRUD", item.identityId.ToString()+item.notificationId.ToString(), item);
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
                getCountCommand.CommandText = "SELECT COUNT(*) FROM AppNotifications;";
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
            sl.Add("notificationId");
            sl.Add("unread");
            sl.Add("senderId");
            sl.Add("timestamp");
            sl.Add("data");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        // SELECT rowId,identityId,notificationId,unread,senderId,timestamp,data,created,modified
        protected AppNotificationsRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<AppNotificationsRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new AppNotificationsRecord();
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.identityId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.notificationId = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[2]);
            item.unread = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[3];
            item.senderId = (rdr[4] == DBNull.Value) ? null : (string)rdr[4];
            item.timestamp = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[5]);
            item.data = (rdr[6] == DBNull.Value) ? null : (byte[])(rdr[6]);
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            item.created = (rdr[7] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[7]);
            item.modified = (rdr[8] == DBNull.Value) ? item.created : new UnixTimeUtc((long)rdr[8]); // HACK
            return item;
       }

        protected virtual async Task<int> DeleteAsync(Guid identityId,Guid notificationId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM AppNotifications " +
                                             "WHERE identityId = @identityId AND notificationId = @notificationId";
                var delete0Param1 = delete0Command.CreateParameter();
                delete0Param1.DbType = DbType.Binary;
                delete0Param1.ParameterName = "@identityId";
                delete0Command.Parameters.Add(delete0Param1);
                var delete0Param2 = delete0Command.CreateParameter();
                delete0Param2.DbType = DbType.Binary;
                delete0Param2.ParameterName = "@notificationId";
                delete0Command.Parameters.Add(delete0Param2);

                delete0Param1.Value = identityId.ToByteArray();
                delete0Param2.Value = notificationId.ToByteArray();
                var count = await delete0Command.ExecuteNonQueryAsync();
                if (count > 0)
                    _cache.Remove("TableAppNotificationsCRUD", identityId.ToString()+notificationId.ToString());
                return count;
            }
        }

        protected virtual async Task<AppNotificationsRecord> PopAsync(Guid identityId,Guid notificationId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var deleteCommand = cn.CreateCommand();
            {
                deleteCommand.CommandText = "DELETE FROM AppNotifications " +
                                             "WHERE identityId = @identityId AND notificationId = @notificationId " + 
                                             "RETURNING rowId,unread,senderId,timestamp,data,created,modified";
                var deleteParam1 = deleteCommand.CreateParameter();
                deleteParam1.DbType = DbType.Binary;
                deleteParam1.ParameterName = "@identityId";
                deleteCommand.Parameters.Add(deleteParam1);
                var deleteParam2 = deleteCommand.CreateParameter();
                deleteParam2.DbType = DbType.Binary;
                deleteParam2.ParameterName = "@notificationId";
                deleteCommand.Parameters.Add(deleteParam2);

                deleteParam1.Value = identityId.ToByteArray();
                deleteParam2.Value = notificationId.ToByteArray();
                using (var rdr = await deleteCommand.ExecuteReaderAsync(CommandBehavior.SingleRow))
                {
                    if (await rdr.ReadAsync())
                    {
                       return ReadRecordFromReader0(rdr,identityId,notificationId);
                    }
                    else
                    {
                       return null;
                    }
                }
            }
        }

        protected AppNotificationsRecord ReadRecordFromReader0(DbDataReader rdr,Guid identityId,Guid notificationId)
        {
            var result = new List<AppNotificationsRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new AppNotificationsRecord();
            item.identityId = identityId;
            item.notificationId = notificationId;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.unread = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[1];
            item.senderId = (rdr[2] == DBNull.Value) ? null : (string)rdr[2];
            item.timestamp = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[3]);
            item.data = (rdr[4] == DBNull.Value) ? null : (byte[])(rdr[4]);
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            item.created = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[5]);
            item.modified = (rdr[6] == DBNull.Value) ? item.created : new UnixTimeUtc((long)rdr[6]); // HACK
            return item;
       }

        protected virtual async Task<AppNotificationsRecord> GetAsync(Guid identityId,Guid notificationId)
        {
            var (hit, cacheObject) = _cache.Get("TableAppNotificationsCRUD", identityId.ToString()+notificationId.ToString());
            if (hit)
                return (AppNotificationsRecord)cacheObject;
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT rowId,unread,senderId,timestamp,data,created,modified FROM AppNotifications " +
                                             "WHERE identityId = @identityId AND notificationId = @notificationId LIMIT 1;"+
                                             ";";
                var get0Param1 = get0Command.CreateParameter();
                get0Param1.DbType = DbType.Binary;
                get0Param1.ParameterName = "@identityId";
                get0Command.Parameters.Add(get0Param1);
                var get0Param2 = get0Command.CreateParameter();
                get0Param2.DbType = DbType.Binary;
                get0Param2.ParameterName = "@notificationId";
                get0Command.Parameters.Add(get0Param2);

                get0Param1.Value = identityId.ToByteArray();
                get0Param2.Value = notificationId.ToByteArray();
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            _cache.AddOrUpdate("TableAppNotificationsCRUD", identityId.ToString()+notificationId.ToString(), null);
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr,identityId,notificationId);
                        _cache.AddOrUpdate("TableAppNotificationsCRUD", identityId.ToString()+notificationId.ToString(), r);
                        return r;
                    } // using
                } //
            } // using
        }

        protected virtual async Task<(List<AppNotificationsRecord>, UnixTimeUtc? nextCursor, long nextRowId)> PagingByCreatedAsync(int count, Guid identityId, UnixTimeUtc? inCursor, long? rowid)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");
            if (count == int.MaxValue)
                count--; // avoid overflow when doing +1 on the param below
            if (inCursor == null)
                inCursor = new UnixTimeUtc(long.MaxValue);
            if (rowid == null)
                rowid = long.MaxValue;

            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getPaging7Command = cn.CreateCommand();
            {
                getPaging7Command.CommandText = "SELECT rowId,identityId,notificationId,unread,senderId,timestamp,data,created,modified FROM AppNotifications " +
                                            "WHERE (identityId = @identityId) AND created <= @created AND rowId < @rowId ORDER BY created DESC , rowId DESC LIMIT @count;";
                var getPaging7Param1 = getPaging7Command.CreateParameter();
                getPaging7Param1.DbType = DbType.Int64;
                getPaging7Param1.ParameterName = "@created";
                getPaging7Command.Parameters.Add(getPaging7Param1);
                var getPaging7Param2 = getPaging7Command.CreateParameter();
                getPaging7Param2.DbType = DbType.Int64;
                getPaging7Param2.ParameterName = "@rowId";
                getPaging7Command.Parameters.Add(getPaging7Param2);
                var getPaging7Param3 = getPaging7Command.CreateParameter();
                getPaging7Param3.DbType = DbType.Int64;
                getPaging7Param3.ParameterName = "@count";
                getPaging7Command.Parameters.Add(getPaging7Param3);
                var getPaging7Param4 = getPaging7Command.CreateParameter();
                getPaging7Param4.DbType = DbType.Binary;
                getPaging7Param4.ParameterName = "@identityId";
                getPaging7Command.Parameters.Add(getPaging7Param4);

                getPaging7Param1.Value = inCursor?.milliseconds;
                getPaging7Param2.Value = rowid;
                getPaging7Param3.Value = count+1;
                getPaging7Param4.Value = identityId.ToByteArray();

                {
                    await using (var rdr = await getPaging7Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        var result = new List<AppNotificationsRecord>();
                        UnixTimeUtc? nextCursor;
                        long nextRowId;
                        int n = 0;
                        while ((n < count) && await rdr.ReadAsync())
                        {
                            n++;
                            result.Add(ReadRecordFromReaderAll(rdr));
                        } // while
                        if ((n > 0) && await rdr.ReadAsync())
                        {
                                nextCursor = result[n - 1].created;
                                nextRowId = result[n - 1].rowId;
                        }
                        else
                        {
                            nextCursor = null;
                            nextRowId = 0;
                        }
                        return (result, nextCursor, nextRowId);
                    } // using
                } //
            } // using 
        } // PagingGet

        protected virtual async Task<(List<AppNotificationsRecord>, Int64? nextCursor)> PagingByRowIdAsync(int count, Int64? inCursor)
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
                getPaging0Command.CommandText = "SELECT rowId,identityId,notificationId,unread,senderId,timestamp,data,created,modified FROM AppNotifications " +
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
                        var result = new List<AppNotificationsRecord>();
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
