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
using Odin.Core.Util;

// THIS FILE IS AUTO GENERATED - DO NOT EDIT

namespace Odin.Core.Storage.Database.Identity.Table
{
    public class AppNotificationsRecord
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
                    if (value?.Length < 0) throw new Exception("Too short");
                    if (value?.Length > 65535) throw new Exception("Too long");
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
                    if (value?.Length < 0) throw new Exception("Too short");
                    if (value?.Length > 65000) throw new Exception("Too long");
                  _data = value;
               }
        }
        private UnixTimeUtcUnique _created;
        public UnixTimeUtcUnique created
        {
           get {
                   return _created;
               }
           set {
                  _created = value;
               }
        }
        private UnixTimeUtcUnique? _modified;
        public UnixTimeUtcUnique? modified
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
            {
                if (dropExisting)
                {
                   cmd.CommandText = "DROP TABLE IF EXISTS AppNotifications;";
                   await cmd.ExecuteNonQueryAsync();
                }
                cmd.CommandText =
                "CREATE TABLE IF NOT EXISTS AppNotifications("
                 +"identityId BLOB NOT NULL, "
                 +"notificationId BLOB NOT NULL UNIQUE, "
                 +"unread INT NOT NULL, "
                 +"senderId STRING , "
                 +"timestamp INT NOT NULL, "
                 +"data BLOB , "
                 +"created INT NOT NULL, "
                 +"modified INT  "
                 +", PRIMARY KEY (identityId,notificationId)"
                 +");"
                 +"CREATE INDEX IF NOT EXISTS Idx0TableAppNotificationsCRUD ON AppNotifications(identityId,created);"
                 ;
                 await cmd.ExecuteNonQueryAsync();
            }
        }

        protected virtual async Task<int> InsertAsync(AppNotificationsRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.notificationId.AssertGuidNotEmpty("Guid parameter notificationId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO AppNotifications (identityId,notificationId,unread,senderId,timestamp,data,created,modified) " +
                                             "VALUES (@identityId,@notificationId,@unread,@senderId,@timestamp,@data,@created,@modified)";
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
                var insertParam7 = insertCommand.CreateParameter();
                insertParam7.ParameterName = "@created";
                insertCommand.Parameters.Add(insertParam7);
                var insertParam8 = insertCommand.CreateParameter();
                insertParam8.ParameterName = "@modified";
                insertCommand.Parameters.Add(insertParam8);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.notificationId.ToByteArray();
                insertParam3.Value = item.unread;
                insertParam4.Value = item.senderId ?? (object)DBNull.Value;
                insertParam5.Value = item.timestamp.milliseconds;
                insertParam6.Value = item.data ?? (object)DBNull.Value;
                var now = UnixTimeUtcUnique.Now();
                insertParam7.Value = now.uniqueTime;
                item.modified = null;
                insertParam8.Value = DBNull.Value;
                var count = await insertCommand.ExecuteNonQueryAsync();
                if (count > 0)
                {
                     item.created = now;
                    _cache.AddOrUpdate("TableAppNotificationsCRUD", item.identityId.ToString()+item.notificationId.ToString(), item);
                }
                return count;
            }
        }

        protected virtual async Task<int> TryInsertAsync(AppNotificationsRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.notificationId.AssertGuidNotEmpty("Guid parameter notificationId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT OR IGNORE INTO AppNotifications (identityId,notificationId,unread,senderId,timestamp,data,created,modified) " +
                                             "VALUES (@identityId,@notificationId,@unread,@senderId,@timestamp,@data,@created,@modified)";
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
                var insertParam7 = insertCommand.CreateParameter();
                insertParam7.ParameterName = "@created";
                insertCommand.Parameters.Add(insertParam7);
                var insertParam8 = insertCommand.CreateParameter();
                insertParam8.ParameterName = "@modified";
                insertCommand.Parameters.Add(insertParam8);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.notificationId.ToByteArray();
                insertParam3.Value = item.unread;
                insertParam4.Value = item.senderId ?? (object)DBNull.Value;
                insertParam5.Value = item.timestamp.milliseconds;
                insertParam6.Value = item.data ?? (object)DBNull.Value;
                var now = UnixTimeUtcUnique.Now();
                insertParam7.Value = now.uniqueTime;
                item.modified = null;
                insertParam8.Value = DBNull.Value;
                var count = await insertCommand.ExecuteNonQueryAsync();
                if (count > 0)
                {
                    item.created = now;
                   _cache.AddOrUpdate("TableAppNotificationsCRUD", item.identityId.ToString()+item.notificationId.ToString(), item);
                }
                return count;
            }
        }

        protected virtual async Task<int> UpsertAsync(AppNotificationsRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.notificationId.AssertGuidNotEmpty("Guid parameter notificationId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                upsertCommand.CommandText = "INSERT INTO AppNotifications (identityId,notificationId,unread,senderId,timestamp,data,created) " +
                                             "VALUES (@identityId,@notificationId,@unread,@senderId,@timestamp,@data,@created)"+
                                             "ON CONFLICT (identityId,notificationId) DO UPDATE "+
                                             "SET unread = @unread,senderId = @senderId,timestamp = @timestamp,data = @data,modified = @modified "+
                                             "RETURNING created, modified;";
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
                var upsertParam7 = upsertCommand.CreateParameter();
                upsertParam7.ParameterName = "@created";
                upsertCommand.Parameters.Add(upsertParam7);
                var upsertParam8 = upsertCommand.CreateParameter();
                upsertParam8.ParameterName = "@modified";
                upsertCommand.Parameters.Add(upsertParam8);
                var now = UnixTimeUtcUnique.Now();
                upsertParam1.Value = item.identityId.ToByteArray();
                upsertParam2.Value = item.notificationId.ToByteArray();
                upsertParam3.Value = item.unread;
                upsertParam4.Value = item.senderId ?? (object)DBNull.Value;
                upsertParam5.Value = item.timestamp.milliseconds;
                upsertParam6.Value = item.data ?? (object)DBNull.Value;
                upsertParam7.Value = now.uniqueTime;
                upsertParam8.Value = now.uniqueTime;
                await using var rdr = await upsertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                   long created = rdr.GetInt64(0);
                   long? modified = rdr.IsDBNull(1) ? null : rdr.GetInt64(1);
                   item.created = new UnixTimeUtcUnique(created);
                   if (modified != null)
                      item.modified = new UnixTimeUtcUnique((long)modified);
                   else
                      item.modified = null;
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
                updateCommand.CommandText = "UPDATE AppNotifications " +
                                             "SET unread = @unread,senderId = @senderId,timestamp = @timestamp,data = @data,modified = @modified "+
                                             "WHERE (identityId = @identityId AND notificationId = @notificationId)";
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
                var updateParam7 = updateCommand.CreateParameter();
                updateParam7.ParameterName = "@created";
                updateCommand.Parameters.Add(updateParam7);
                var updateParam8 = updateCommand.CreateParameter();
                updateParam8.ParameterName = "@modified";
                updateCommand.Parameters.Add(updateParam8);
                var now = UnixTimeUtcUnique.Now();
                updateParam1.Value = item.identityId.ToByteArray();
                updateParam2.Value = item.notificationId.ToByteArray();
                updateParam3.Value = item.unread;
                updateParam4.Value = item.senderId ?? (object)DBNull.Value;
                updateParam5.Value = item.timestamp.milliseconds;
                updateParam6.Value = item.data ?? (object)DBNull.Value;
                updateParam7.Value = now.uniqueTime;
                updateParam8.Value = now.uniqueTime;
                var count = await updateCommand.ExecuteNonQueryAsync();
                if (count > 0)
                {
                     item.modified = now;
                    _cache.AddOrUpdate("TableAppNotificationsCRUD", item.identityId.ToString()+item.notificationId.ToString(), item);
                }
                return count;
            }
        }

        protected virtual async Task<int> GetCountDirtyAsync()
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getCountCommand = cn.CreateCommand();
            {
                 // TODO: this is SQLite specific
                getCountCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM AppNotifications; PRAGMA read_uncommitted = 0;";
                var count = await getCountCommand.ExecuteScalarAsync();
                if (count == null || count == DBNull.Value || !(count is int || count is long))
                    return -1;
                else
                    return Convert.ToInt32(count);
            }
        }

        public List<string> GetColumnNames()
        {
            var sl = new List<string>();
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

        // SELECT identityId,notificationId,unread,senderId,timestamp,data,created,modified
        protected AppNotificationsRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<AppNotificationsRecord>();
            byte[] tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new AppNotificationsRecord();
            item.identityId = rdr.IsDBNull(0) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[0]);
            item.notificationId = rdr.IsDBNull(1) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.unread = rdr.IsDBNull(2) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[2];
            item.senderId = rdr.IsDBNull(3) ? 
                null : (string)rdr[3];
            item.timestamp = rdr.IsDBNull(4) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[4]);
            item.data = rdr.IsDBNull(5) ? 
                null : (byte[])(rdr[5]);
            if (item.data?.Length > 65000)
                throw new Exception("Too much data in data...");
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            item.created = rdr.IsDBNull(6) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtcUnique((long)rdr[6]);
            item.modified = rdr.IsDBNull(7) ? 
                null : new UnixTimeUtcUnique((long)rdr[7]);
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

        protected AppNotificationsRecord ReadRecordFromReader0(DbDataReader rdr, Guid identityId,Guid notificationId)
        {
            var result = new List<AppNotificationsRecord>();
            byte[] tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new AppNotificationsRecord();
            item.identityId = identityId;
            item.notificationId = notificationId;

            item.unread = rdr.IsDBNull(0) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[0];

            item.senderId = rdr.IsDBNull(1) ? 
                null : (string)rdr[1];

            item.timestamp = rdr.IsDBNull(2) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[2]);

            item.data = rdr.IsDBNull(3) ? 
                null : (byte[])(rdr[3]);
            if (item.data?.Length > 65000)
                throw new Exception("Too much data in data...");
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");

            item.created = rdr.IsDBNull(4) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtcUnique((long)rdr[4]);

            item.modified = rdr.IsDBNull(5) ? 
                null : new UnixTimeUtcUnique((long)rdr[5]);
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
                get0Command.CommandText = "SELECT unread,senderId,timestamp,data,created,modified FROM AppNotifications " +
                                             "WHERE identityId = @identityId AND notificationId = @notificationId LIMIT 1;";
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
                        var r = ReadRecordFromReader0(rdr, identityId,notificationId);
                        _cache.AddOrUpdate("TableAppNotificationsCRUD", identityId.ToString()+notificationId.ToString(), r);
                        return r;
                    } // using
                } //
            } // using
        }

        protected virtual async Task<(List<AppNotificationsRecord>, UnixTimeUtcUnique? nextCursor)> PagingByCreatedAsync(int count, Guid identityId, UnixTimeUtcUnique? inCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");
            if (inCursor == null)
                inCursor = new UnixTimeUtcUnique(long.MaxValue);

            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getPaging7Command = cn.CreateCommand();
            {
                getPaging7Command.CommandText = "SELECT identityId,notificationId,unread,senderId,timestamp,data,created,modified FROM AppNotifications " +
                                            "WHERE (identityId = @identityId) AND created < @created ORDER BY created DESC LIMIT $_count;";
                var getPaging7Param1 = getPaging7Command.CreateParameter();
                getPaging7Param1.ParameterName = "@created";
                getPaging7Command.Parameters.Add(getPaging7Param1);
                var getPaging7Param2 = getPaging7Command.CreateParameter();
                getPaging7Param2.ParameterName = "$_count";
                getPaging7Command.Parameters.Add(getPaging7Param2);
                var getPaging7Param3 = getPaging7Command.CreateParameter();
                getPaging7Param3.ParameterName = "@identityId";
                getPaging7Command.Parameters.Add(getPaging7Param3);

                getPaging7Param1.Value = inCursor?.uniqueTime;
                getPaging7Param2.Value = count+1;
                getPaging7Param3.Value = identityId.ToByteArray();

                {
                    await using (var rdr = await getPaging7Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        var result = new List<AppNotificationsRecord>();
                        UnixTimeUtcUnique? nextCursor;
                        int n = 0;
                        while ((n < count) && await rdr.ReadAsync())
                        {
                            n++;
                            result.Add(ReadRecordFromReaderAll(rdr));
                        } // while
                        if ((n > 0) && await rdr.ReadAsync())
                        {
                                nextCursor = result[n - 1].created;
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
