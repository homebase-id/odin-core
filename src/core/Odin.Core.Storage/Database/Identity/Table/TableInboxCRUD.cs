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
    public record InboxRecord
    {
        public Int64 rowId { get; set; }
        public Guid identityId { get; set; }
        public Guid fileId { get; set; }
        public Guid boxId { get; set; }
        public Int32 priority { get; set; }
        public UnixTimeUtc timeStamp { get; set; }
        public byte[] value { get; set; }
        public Guid? popStamp { get; set; }
        public string correlationId { get; set; }
        public UnixTimeUtc created { get; set; }
        public UnixTimeUtc modified { get; set; }
        public void Validate()
        {
            identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            fileId.AssertGuidNotEmpty("Guid parameter fileId cannot be set to Empty GUID.");
            boxId.AssertGuidNotEmpty("Guid parameter boxId cannot be set to Empty GUID.");
            if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short value, was {value.Length} (min 0)");
            if (value?.Length > 65535) throw new OdinDatabaseValidationException($"Too long value, was {value.Length} (max 65535)");
            popStamp.AssertGuidNotEmpty("Guid parameter popStamp cannot be set to Empty GUID.");
            if (correlationId?.Length < 0) throw new OdinDatabaseValidationException($"Too short correlationId, was {correlationId.Length} (min 0)");
            if (correlationId?.Length > 64) throw new OdinDatabaseValidationException($"Too long correlationId, was {correlationId.Length} (max 64)");
        }
    } // End of record InboxRecord

    public abstract class TableInboxCRUD : TableBase
    {
        private ScopedIdentityConnectionFactory _scopedConnectionFactory { get; init; }
        public override string TableName { get; } = "Inbox";

        protected TableInboxCRUD(ScopedIdentityConnectionFactory scopedConnectionFactory)
        {
            _scopedConnectionFactory = scopedConnectionFactory;
        }


        public override async Task EnsureTableExistsAsync(bool dropExisting = false)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            if (dropExisting)
                await SqlHelper.DeleteTableAsync(cn, "Inbox");
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE Inbox IS '{ \"Version\": 0 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS Inbox( -- { \"Version\": 0 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"fileId BYTEA NOT NULL UNIQUE, "
                   +"boxId BYTEA NOT NULL, "
                   +"priority BIGINT NOT NULL, "
                   +"timeStamp BIGINT NOT NULL, "
                   +"value BYTEA , "
                   +"popStamp BYTEA , "
                   +"correlationId TEXT , "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +", UNIQUE(identityId,fileId)"
                   +$"){wori};"
                   +"CREATE INDEX IF NOT EXISTS Idx0Inbox ON Inbox(identityId,timeStamp);"
                   +"CREATE INDEX IF NOT EXISTS Idx1Inbox ON Inbox(identityId,boxId);"
                   +"CREATE INDEX IF NOT EXISTS Idx2Inbox ON Inbox(identityId,popStamp);"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "Inbox", createSql, commentSql);
        }

        protected virtual async Task<int> InsertAsync(InboxRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr = insertCommand.SqlNow();
                insertCommand.CommandText = "INSERT INTO Inbox (identityId,fileId,boxId,priority,timeStamp,value,popStamp,correlationId,created,modified) " +
                                           $"VALUES (@identityId,@fileId,@boxId,@priority,@timeStamp,@value,@popStamp,@correlationId,{sqlNowStr},{sqlNowStr})"+
                                            "RETURNING created,modified,rowId;";
                insertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                insertCommand.AddParameter("@fileId", DbType.Binary, item.fileId);
                insertCommand.AddParameter("@boxId", DbType.Binary, item.boxId);
                insertCommand.AddParameter("@priority", DbType.Int32, item.priority);
                insertCommand.AddParameter("@timeStamp", DbType.Int64, item.timeStamp.milliseconds);
                insertCommand.AddParameter("@value", DbType.Binary, item.value);
                insertCommand.AddParameter("@popStamp", DbType.Binary, item.popStamp);
                insertCommand.AddParameter("@correlationId", DbType.String, item.correlationId);
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

        protected virtual async Task<bool> TryInsertAsync(InboxRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr = insertCommand.SqlNow();
                insertCommand.CommandText = "INSERT INTO Inbox (identityId,fileId,boxId,priority,timeStamp,value,popStamp,correlationId,created,modified) " +
                                            $"VALUES (@identityId,@fileId,@boxId,@priority,@timeStamp,@value,@popStamp,@correlationId,{sqlNowStr},{sqlNowStr}) " +
                                            "ON CONFLICT DO NOTHING "+
                                            "RETURNING created,modified,rowId;";
                insertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                insertCommand.AddParameter("@fileId", DbType.Binary, item.fileId);
                insertCommand.AddParameter("@boxId", DbType.Binary, item.boxId);
                insertCommand.AddParameter("@priority", DbType.Int32, item.priority);
                insertCommand.AddParameter("@timeStamp", DbType.Int64, item.timeStamp.milliseconds);
                insertCommand.AddParameter("@value", DbType.Binary, item.value);
                insertCommand.AddParameter("@popStamp", DbType.Binary, item.popStamp);
                insertCommand.AddParameter("@correlationId", DbType.String, item.correlationId);
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

        protected virtual async Task<int> UpsertAsync(InboxRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                string sqlNowStr = upsertCommand.SqlNow();
                upsertCommand.CommandText = "INSERT INTO Inbox (identityId,fileId,boxId,priority,timeStamp,value,popStamp,correlationId,created,modified) " +
                                            $"VALUES (@identityId,@fileId,@boxId,@priority,@timeStamp,@value,@popStamp,@correlationId,{sqlNowStr},{sqlNowStr})"+
                                            "ON CONFLICT (identityId,fileId) DO UPDATE "+
                                            $"SET boxId = @boxId,priority = @priority,timeStamp = @timeStamp,value = @value,popStamp = @popStamp,correlationId = @correlationId,modified = {upsertCommand.SqlMax()}(Inbox.modified+1,{sqlNowStr}) "+
                                            "RETURNING created,modified,rowId;";
                upsertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                upsertCommand.AddParameter("@fileId", DbType.Binary, item.fileId);
                upsertCommand.AddParameter("@boxId", DbType.Binary, item.boxId);
                upsertCommand.AddParameter("@priority", DbType.Int32, item.priority);
                upsertCommand.AddParameter("@timeStamp", DbType.Int64, item.timeStamp.milliseconds);
                upsertCommand.AddParameter("@value", DbType.Binary, item.value);
                upsertCommand.AddParameter("@popStamp", DbType.Binary, item.popStamp);
                upsertCommand.AddParameter("@correlationId", DbType.String, item.correlationId);
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

        protected virtual async Task<int> UpdateAsync(InboxRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                string sqlNowStr = updateCommand.SqlNow();
                updateCommand.CommandText = "UPDATE Inbox " +
                                            $"SET boxId = @boxId,priority = @priority,timeStamp = @timeStamp,value = @value,popStamp = @popStamp,correlationId = @correlationId,modified = {updateCommand.SqlMax()}(Inbox.modified+1,{sqlNowStr}) "+
                                            "WHERE (identityId = @identityId AND fileId = @fileId) "+
                                            "RETURNING created,modified,rowId;";
                updateCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                updateCommand.AddParameter("@fileId", DbType.Binary, item.fileId);
                updateCommand.AddParameter("@boxId", DbType.Binary, item.boxId);
                updateCommand.AddParameter("@priority", DbType.Int32, item.priority);
                updateCommand.AddParameter("@timeStamp", DbType.Int64, item.timeStamp.milliseconds);
                updateCommand.AddParameter("@value", DbType.Binary, item.value);
                updateCommand.AddParameter("@popStamp", DbType.Binary, item.popStamp);
                updateCommand.AddParameter("@correlationId", DbType.String, item.correlationId);
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
                getCountCommand.CommandText = "SELECT COUNT(*) FROM Inbox;";
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
            sl.Add("fileId");
            sl.Add("boxId");
            sl.Add("priority");
            sl.Add("timeStamp");
            sl.Add("value");
            sl.Add("popStamp");
            sl.Add("correlationId");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        // SELECT rowId,identityId,fileId,boxId,priority,timeStamp,value,popStamp,correlationId,created,modified
        protected InboxRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<InboxRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new InboxRecord();
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.identityId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.fileId = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[2]);
            item.boxId = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[3]);
            item.priority = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[4];
            item.timeStamp = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[5]);
            item.value = (rdr[6] == DBNull.Value) ? null : (byte[])(rdr[6]);
            if (item.value?.Length < 0)
                throw new Exception("Too little data in value...");
            item.popStamp = (rdr[7] == DBNull.Value) ? null : new Guid((byte[])rdr[7]);
            item.correlationId = (rdr[8] == DBNull.Value) ? null : (string)rdr[8];
            item.created = (rdr[9] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[9]);
            item.modified = (rdr[10] == DBNull.Value) ? item.created : new UnixTimeUtc((long)rdr[10]); // HACK
            return item;
       }

        protected virtual async Task<int> DeleteAsync(Guid identityId,Guid fileId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM Inbox " +
                                             "WHERE identityId = @identityId AND fileId = @fileId";

                delete0Command.AddParameter("@identityId", DbType.Binary, identityId);
                delete0Command.AddParameter("@fileId", DbType.Binary, fileId);
                var count = await delete0Command.ExecuteNonQueryAsync();
                return count;
            }
        }

        protected virtual async Task<InboxRecord> PopAsync(Guid identityId,Guid fileId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var deleteCommand = cn.CreateCommand();
            {
                deleteCommand.CommandText = "DELETE FROM Inbox " +
                                             "WHERE identityId = @identityId AND fileId = @fileId " + 
                                             "RETURNING rowId,boxId,priority,timeStamp,value,popStamp,correlationId,created,modified";

                deleteCommand.AddParameter("@identityId", DbType.Binary, identityId);
                deleteCommand.AddParameter("@fileId", DbType.Binary, fileId);
                using (var rdr = await deleteCommand.ExecuteReaderAsync(CommandBehavior.SingleRow))
                {
                    if (await rdr.ReadAsync())
                    {
                       return ReadRecordFromReader0(rdr,identityId,fileId);
                    }
                    else
                    {
                       return null;
                    }
                }
            }
        }

        protected InboxRecord ReadRecordFromReader0(DbDataReader rdr,Guid identityId,Guid fileId)
        {
            var result = new List<InboxRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new InboxRecord();
            item.identityId = identityId;
            item.fileId = fileId;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.boxId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.priority = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[2];
            item.timeStamp = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[3]);
            item.value = (rdr[4] == DBNull.Value) ? null : (byte[])(rdr[4]);
            if (item.value?.Length < 0)
                throw new Exception("Too little data in value...");
            item.popStamp = (rdr[5] == DBNull.Value) ? null : new Guid((byte[])rdr[5]);
            item.correlationId = (rdr[6] == DBNull.Value) ? null : (string)rdr[6];
            item.created = (rdr[7] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[7]);
            item.modified = (rdr[8] == DBNull.Value) ? item.created : new UnixTimeUtc((long)rdr[8]); // HACK
            return item;
       }

        protected virtual async Task<InboxRecord> GetAsync(Guid identityId,Guid fileId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT rowId,boxId,priority,timeStamp,value,popStamp,correlationId,created,modified FROM Inbox " +
                                             "WHERE identityId = @identityId AND fileId = @fileId LIMIT 1;"+
                                             ";";

                get0Command.AddParameter("@identityId", DbType.Binary, identityId);
                get0Command.AddParameter("@fileId", DbType.Binary, fileId);
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr,identityId,fileId);
                        return r;
                    } // using
                } //
            } // using
        }

        protected virtual async Task<(List<InboxRecord>, Int64? nextCursor)> PagingByRowIdAsync(int count, Int64? inCursor)
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
                getPaging0Command.CommandText = "SELECT rowId,identityId,fileId,boxId,priority,timeStamp,value,popStamp,correlationId,created,modified FROM Inbox " +
                                            "WHERE rowId > @rowId  ORDER BY rowId ASC  LIMIT @count;";

                getPaging0Command.AddParameter("@rowId", DbType.Int64, inCursor);
                getPaging0Command.AddParameter("@count", DbType.Int64, count+1);

                {
                    await using (var rdr = await getPaging0Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        var result = new List<InboxRecord>();
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
