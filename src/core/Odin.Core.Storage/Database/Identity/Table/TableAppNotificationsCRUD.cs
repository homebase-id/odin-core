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

    public abstract class TableAppNotificationsCRUD : TableBase
    {
        private ScopedIdentityConnectionFactory _scopedConnectionFactory { get; init; }
        public override string TableName { get; } = "AppNotifications";

        protected TableAppNotificationsCRUD(ScopedIdentityConnectionFactory scopedConnectionFactory)
        {
            _scopedConnectionFactory = scopedConnectionFactory;
        }


        public override async Task EnsureTableExistsAsync(bool dropExisting = false)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            if (dropExisting)
                await SqlHelper.DeleteTableAsync(cn, "AppNotifications");
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
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
            await SqlHelper.CreateTableWithCommentAsync(cn, "AppNotifications", createSql, commentSql);
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
                insertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                insertCommand.AddParameter("@notificationId", DbType.Binary, item.notificationId);
                insertCommand.AddParameter("@unread", DbType.Int32, item.unread);
                insertCommand.AddParameter("@senderId", DbType.String, item.senderId);
                insertCommand.AddParameter("@timestamp", DbType.Int64, item.timestamp.milliseconds);
                insertCommand.AddParameter("@data", DbType.Binary, item.data);
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    item.created = new UnixTimeUtc(created);
                    long modified = (long) rdr[1];
                    item.modified = new UnixTimeUtc((long)modified);
                    item.rowId = (long) rdr[2];
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
                insertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                insertCommand.AddParameter("@notificationId", DbType.Binary, item.notificationId);
                insertCommand.AddParameter("@unread", DbType.Int32, item.unread);
                insertCommand.AddParameter("@senderId", DbType.String, item.senderId);
                insertCommand.AddParameter("@timestamp", DbType.Int64, item.timestamp.milliseconds);
                insertCommand.AddParameter("@data", DbType.Binary, item.data);
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    item.created = new UnixTimeUtc(created);
                    long modified = (long) rdr[1];
                    item.modified = new UnixTimeUtc((long)modified);
                    item.rowId = (long) rdr[2];
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
                upsertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                upsertCommand.AddParameter("@notificationId", DbType.Binary, item.notificationId);
                upsertCommand.AddParameter("@unread", DbType.Int32, item.unread);
                upsertCommand.AddParameter("@senderId", DbType.String, item.senderId);
                upsertCommand.AddParameter("@timestamp", DbType.Int64, item.timestamp.milliseconds);
                upsertCommand.AddParameter("@data", DbType.Binary, item.data);
                await using var rdr = await upsertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    item.created = new UnixTimeUtc(created);
                    long modified = (long) rdr[1];
                    item.modified = new UnixTimeUtc((long)modified);
                    item.rowId = (long) rdr[2];
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
                updateCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                updateCommand.AddParameter("@notificationId", DbType.Binary, item.notificationId);
                updateCommand.AddParameter("@unread", DbType.Int32, item.unread);
                updateCommand.AddParameter("@senderId", DbType.String, item.senderId);
                updateCommand.AddParameter("@timestamp", DbType.Int64, item.timestamp.milliseconds);
                updateCommand.AddParameter("@data", DbType.Binary, item.data);
                await using var rdr = await updateCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    item.created = new UnixTimeUtc(created);
                    long modified = (long) rdr[1];
                    item.modified = new UnixTimeUtc((long)modified);
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
                getCountCommand.CommandText = "SELECT COUNT(*) FROM AppNotifications;";
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
            item.modified = (rdr[8] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[8]);
            return item;
       }

        protected virtual async Task<int> DeleteAsync(Guid identityId,Guid notificationId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM AppNotifications " +
                                             "WHERE identityId = @identityId AND notificationId = @notificationId";

                delete0Command.AddParameter("@identityId", DbType.Binary, identityId);
                delete0Command.AddParameter("@notificationId", DbType.Binary, notificationId);
                var count = await delete0Command.ExecuteNonQueryAsync();
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

                deleteCommand.AddParameter("@identityId", DbType.Binary, identityId);
                deleteCommand.AddParameter("@notificationId", DbType.Binary, notificationId);
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
            item.modified = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[6]);
            return item;
       }

        protected virtual async Task<AppNotificationsRecord> GetAsync(Guid identityId,Guid notificationId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT rowId,unread,senderId,timestamp,data,created,modified FROM AppNotifications " +
                                             "WHERE identityId = @identityId AND notificationId = @notificationId LIMIT 1 "+
                                             ";";

                get0Command.AddParameter("@identityId", DbType.Binary, identityId);
                get0Command.AddParameter("@notificationId", DbType.Binary, notificationId);
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr,identityId,notificationId);
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

                getPaging7Command.AddParameter("@created", DbType.Int64, inCursor?.milliseconds);
                getPaging7Command.AddParameter("@rowId", DbType.Int64, rowid);
                getPaging7Command.AddParameter("@count", DbType.Int64, count+1);
                getPaging7Command.AddParameter("@identityId", DbType.Binary, identityId);

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

                getPaging0Command.AddParameter("@rowId", DbType.Int64, inCursor);
                getPaging0Command.AddParameter("@count", DbType.Int64, count+1);

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
