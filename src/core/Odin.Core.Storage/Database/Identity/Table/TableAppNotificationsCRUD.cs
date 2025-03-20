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

// THIS FILE IS AUTO GENERATED - DO NOT EDIT

namespace Odin.Core.Storage.Database.Identity.Table
{
    public class AppNotificationsRecord
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
        private Guid _notificationId;
        public Guid notificationId
        {
           get {
                   return _notificationId;
               }
           set {
                  _notificationId = value;
               }
        }
        private Int32 _unread;
        public Int32 unread
        {
           get {
                   return _unread;
               }
           set {
                  _unread = value;
               }
        }
        private string _senderId;
        public string senderId
        {
           get {
                   return _senderId;
               }
           set {
                    if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short senderId, was {value.Length} (min 0)");
                    if (value?.Length > 256) throw new OdinDatabaseValidationException($"Too long senderId, was {value.Length} (max 256)");
                  _senderId = value;
               }
        }
        internal string senderIdNoLengthCheck
        {
           get {
                   return _senderId;
               }
           set {
                    if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short senderId, was {value.Length} (min 0)");
                  _senderId = value;
               }
        }
        private UnixTimeUtc _timestamp;
        public UnixTimeUtc timestamp
        {
           get {
                   return _timestamp;
               }
           set {
                  _timestamp = value;
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
                    if (value?.Length > 65000) throw new OdinDatabaseValidationException($"Too long data, was {value.Length} (max 65000)");
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
    } // End of class AppNotificationsRecord

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
            await using var cmd = cn.CreateCommand();
            if (dropExisting)
            {
                cmd.CommandText = "DROP TABLE IF EXISTS AppNotifications;";
                await cmd.ExecuteNonQueryAsync();
            }
            var rowid = "";
            if (_scopedConnectionFactory.DatabaseType == DatabaseType.Postgres)
               rowid = "rowid BIGSERIAL PRIMARY KEY,";
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            cmd.CommandText =
                "CREATE TABLE IF NOT EXISTS AppNotifications("
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"notificationId BYTEA NOT NULL UNIQUE, "
                   +"unread BIGINT NOT NULL, "
                   +"senderId TEXT , "
                   +"timestamp BIGINT NOT NULL, "
                   +"data BYTEA , "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT  "
                   +", UNIQUE(identityId,notificationId)"
                   +$"){wori};"
                   +"CREATE INDEX IF NOT EXISTS Idx0AppNotifications ON AppNotifications(identityId,created);"
                   ;
            await cmd.ExecuteNonQueryAsync();
        }

        protected virtual async Task<int> InsertAsync(AppNotificationsRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.notificationId.AssertGuidNotEmpty("Guid parameter notificationId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr;
                if (_scopedConnectionFactory.DatabaseType == DatabaseType.Sqlite)
                    sqlNowStr = "CAST((julianday('now') - 2440587.5) * 86400000 AS INTEGER)";
                else
                    sqlNowStr = "EXTRACT(EPOCH FROM NOW() AT TIME ZONE 'UTC') * 1000";
                insertCommand.CommandText = "INSERT INTO AppNotifications (identityId,notificationId,unread,senderId,timestamp,data,created,modified) " +
                                             $"VALUES (@identityId,@notificationId,@unread,@senderId,@timestamp,@data,{sqlNowStr},NULL)"+
                                            "RETURNING created,modified,rowId;";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.ParameterName = "@notificationId";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.ParameterName = "@unread";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.ParameterName = "@senderId";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.ParameterName = "@timestamp";
                insertCommand.Parameters.Add(insertParam5);
                var insertParam6 = insertCommand.CreateParameter();
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
                    long? modified = (rdr[1] == DBNull.Value) ? null : (long) rdr[1];
                    item.created = new UnixTimeUtc(created);
                    if (modified != null)
                        item.modified = new UnixTimeUtc((long)modified);
                    else
                        item.modified = null;
                    item.rowId = (long) rdr[2];
                    _cache.AddOrUpdate("TableAppNotificationsCRUD", item.identityId.ToString()+item.notificationId.ToString(), item);
                    return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<bool> TryInsertAsync(AppNotificationsRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.notificationId.AssertGuidNotEmpty("Guid parameter notificationId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr;
                if (_scopedConnectionFactory.DatabaseType == DatabaseType.Sqlite)
                    sqlNowStr = "CAST((julianday('now') - 2440587.5) * 86400000 AS INTEGER)";
                else
                    sqlNowStr = "EXTRACT(EPOCH FROM NOW() AT TIME ZONE 'UTC') * 1000";
                insertCommand.CommandText = "INSERT INTO AppNotifications (identityId,notificationId,unread,senderId,timestamp,data,created,modified) " +
                                            $"VALUES (@identityId,@notificationId,@unread,@senderId,@timestamp,@data,{sqlNowStr},NULL) " +
                                            "ON CONFLICT DO NOTHING "+
                                            "RETURNING created,modified,rowId;";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.ParameterName = "@notificationId";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.ParameterName = "@unread";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.ParameterName = "@senderId";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.ParameterName = "@timestamp";
                insertCommand.Parameters.Add(insertParam5);
                var insertParam6 = insertCommand.CreateParameter();
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
                    long? modified = (rdr[1] == DBNull.Value) ? null : (long) rdr[1];
                    item.created = new UnixTimeUtc(created);
                    if (modified != null)
                        item.modified = new UnixTimeUtc((long)modified);
                    else
                        item.modified = null;
                    item.rowId = (long) rdr[2];
                   _cache.AddOrUpdate("TableAppNotificationsCRUD", item.identityId.ToString()+item.notificationId.ToString(), item);
                    return true;
                }
                return false;
            }
        }

        protected virtual async Task<int> UpsertAsync(AppNotificationsRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.notificationId.AssertGuidNotEmpty("Guid parameter notificationId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                string sqlNowStr;
                if (_scopedConnectionFactory.DatabaseType == DatabaseType.Sqlite)
                    sqlNowStr = "CAST((julianday('now') - 2440587.5) * 86400000 AS INTEGER)";
                else
                    sqlNowStr = "EXTRACT(EPOCH FROM NOW() AT TIME ZONE 'UTC') * 1000";
                upsertCommand.CommandText = "INSERT INTO AppNotifications (identityId,notificationId,unread,senderId,timestamp,data,created,modified) " +
                                            $"VALUES (@identityId,@notificationId,@unread,@senderId,@timestamp,@data,{sqlNowStr},NULL)"+
                                            "ON CONFLICT (identityId,notificationId) DO UPDATE "+
                                            $"SET unread = @unread,senderId = @senderId,timestamp = @timestamp,data = @data,modified = {sqlNowStr} "+
                                            "RETURNING created,modified,rowId;";
                var upsertParam1 = upsertCommand.CreateParameter();
                upsertParam1.ParameterName = "@identityId";
                upsertCommand.Parameters.Add(upsertParam1);
                var upsertParam2 = upsertCommand.CreateParameter();
                upsertParam2.ParameterName = "@notificationId";
                upsertCommand.Parameters.Add(upsertParam2);
                var upsertParam3 = upsertCommand.CreateParameter();
                upsertParam3.ParameterName = "@unread";
                upsertCommand.Parameters.Add(upsertParam3);
                var upsertParam4 = upsertCommand.CreateParameter();
                upsertParam4.ParameterName = "@senderId";
                upsertCommand.Parameters.Add(upsertParam4);
                var upsertParam5 = upsertCommand.CreateParameter();
                upsertParam5.ParameterName = "@timestamp";
                upsertCommand.Parameters.Add(upsertParam5);
                var upsertParam6 = upsertCommand.CreateParameter();
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
                    long? modified = (rdr[1] == DBNull.Value) ? null : (long) rdr[1];
                    item.created = new UnixTimeUtc(created);
                    if (modified != null)
                        item.modified = new UnixTimeUtc((long)modified);
                    else
                        item.modified = null;
                    item.rowId = (long) rdr[2];
                   _cache.AddOrUpdate("TableAppNotificationsCRUD", item.identityId.ToString()+item.notificationId.ToString(), item);
                    return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<int> UpdateAsync(AppNotificationsRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.notificationId.AssertGuidNotEmpty("Guid parameter notificationId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                string sqlNowStr;
                if (_scopedConnectionFactory.DatabaseType == DatabaseType.Sqlite)
                    sqlNowStr = "CAST((julianday('now') - 2440587.5) * 86400000 AS INTEGER)";
                else
                    sqlNowStr = "EXTRACT(EPOCH FROM NOW() AT TIME ZONE 'UTC') * 1000";
                updateCommand.CommandText = "UPDATE AppNotifications " +
                                            $"SET unread = @unread,senderId = @senderId,timestamp = @timestamp,data = @data,modified = {sqlNowStr} "+
                                            "WHERE (identityId = @identityId AND notificationId = @notificationId) "+
                                            "RETURNING created,modified,rowId;";
                var updateParam1 = updateCommand.CreateParameter();
                updateParam1.ParameterName = "@identityId";
                updateCommand.Parameters.Add(updateParam1);
                var updateParam2 = updateCommand.CreateParameter();
                updateParam2.ParameterName = "@notificationId";
                updateCommand.Parameters.Add(updateParam2);
                var updateParam3 = updateCommand.CreateParameter();
                updateParam3.ParameterName = "@unread";
                updateCommand.Parameters.Add(updateParam3);
                var updateParam4 = updateCommand.CreateParameter();
                updateParam4.ParameterName = "@senderId";
                updateCommand.Parameters.Add(updateParam4);
                var updateParam5 = updateCommand.CreateParameter();
                updateParam5.ParameterName = "@timestamp";
                updateCommand.Parameters.Add(updateParam5);
                var updateParam6 = updateCommand.CreateParameter();
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
                    long? modified = (rdr[1] == DBNull.Value) ? null : (long) rdr[1];
                    item.created = new UnixTimeUtc(created);
                    if (modified != null)
                        item.modified = new UnixTimeUtc((long)modified);
                    else
                        item.modified = null;
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
            item.senderIdNoLengthCheck = (rdr[4] == DBNull.Value) ? null : (string)rdr[4];
            item.timestamp = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[5]);
            item.dataNoLengthCheck = (rdr[6] == DBNull.Value) ? null : (byte[])(rdr[6]);
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            item.created = (rdr[7] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[7]);
            item.modified = (rdr[8] == DBNull.Value) ? null : new UnixTimeUtc((long)rdr[8]);
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
                delete0Param1.ParameterName = "@identityId";
                delete0Command.Parameters.Add(delete0Param1);
                var delete0Param2 = delete0Command.CreateParameter();
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
            item.senderIdNoLengthCheck = (rdr[2] == DBNull.Value) ? null : (string)rdr[2];
            item.timestamp = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[3]);
            item.dataNoLengthCheck = (rdr[4] == DBNull.Value) ? null : (byte[])(rdr[4]);
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            item.created = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[5]);
            item.modified = (rdr[6] == DBNull.Value) ? null : new UnixTimeUtc((long)rdr[6]);
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
                get0Param1.ParameterName = "@identityId";
                get0Command.Parameters.Add(get0Param1);
                var get0Param2 = get0Command.CreateParameter();
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
                getPaging7Param1.ParameterName = "@created";
                getPaging7Command.Parameters.Add(getPaging7Param1);
                var getPaging7Param2 = getPaging7Command.CreateParameter();
                getPaging7Param2.ParameterName = "@rowId";
                getPaging7Command.Parameters.Add(getPaging7Param2);
                var getPaging7Param3 = getPaging7Command.CreateParameter();
                getPaging7Param3.ParameterName = "@count";
                getPaging7Command.Parameters.Add(getPaging7Param3);
                var getPaging7Param4 = getPaging7Command.CreateParameter();
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
