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

    public abstract class TableInboxCRUD
    {
        private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory;

        protected TableInboxCRUD(CacheHelper cache, ScopedIdentityConnectionFactory scopedConnectionFactory)
        {
            _scopedConnectionFactory = scopedConnectionFactory;
        }


        public virtual async Task EnsureTableExistsAsync(bool dropExisting = false)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            if (dropExisting)
                await SqlHelper.DeleteTableAsync(cn, "Inbox");
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowid BIGSERIAL PRIMARY KEY,";
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
                   +"CREATE INDEX Idx0Inbox ON Inbox(identityId,timeStamp);"
                   +"CREATE INDEX Idx1Inbox ON Inbox(identityId,boxId);"
                   +"CREATE INDEX Idx2Inbox ON Inbox(identityId,popStamp);"
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
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.DbType = DbType.Binary;
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.DbType = DbType.Binary;
                insertParam2.ParameterName = "@fileId";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.DbType = DbType.Binary;
                insertParam3.ParameterName = "@boxId";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.DbType = DbType.Int32;
                insertParam4.ParameterName = "@priority";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.DbType = DbType.Int64;
                insertParam5.ParameterName = "@timeStamp";
                insertCommand.Parameters.Add(insertParam5);
                var insertParam6 = insertCommand.CreateParameter();
                insertParam6.DbType = DbType.Binary;
                insertParam6.ParameterName = "@value";
                insertCommand.Parameters.Add(insertParam6);
                var insertParam7 = insertCommand.CreateParameter();
                insertParam7.DbType = DbType.Binary;
                insertParam7.ParameterName = "@popStamp";
                insertCommand.Parameters.Add(insertParam7);
                var insertParam8 = insertCommand.CreateParameter();
                insertParam8.DbType = DbType.String;
                insertParam8.ParameterName = "@correlationId";
                insertCommand.Parameters.Add(insertParam8);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.fileId.ToByteArray();
                insertParam3.Value = item.boxId.ToByteArray();
                insertParam4.Value = item.priority;
                insertParam5.Value = item.timeStamp.milliseconds;
                insertParam6.Value = item.value ?? (object)DBNull.Value;
                insertParam7.Value = item.popStamp?.ToByteArray() ?? (object)DBNull.Value;
                insertParam8.Value = item.correlationId ?? (object)DBNull.Value;
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
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.DbType = DbType.Binary;
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.DbType = DbType.Binary;
                insertParam2.ParameterName = "@fileId";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.DbType = DbType.Binary;
                insertParam3.ParameterName = "@boxId";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.DbType = DbType.Int32;
                insertParam4.ParameterName = "@priority";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.DbType = DbType.Int64;
                insertParam5.ParameterName = "@timeStamp";
                insertCommand.Parameters.Add(insertParam5);
                var insertParam6 = insertCommand.CreateParameter();
                insertParam6.DbType = DbType.Binary;
                insertParam6.ParameterName = "@value";
                insertCommand.Parameters.Add(insertParam6);
                var insertParam7 = insertCommand.CreateParameter();
                insertParam7.DbType = DbType.Binary;
                insertParam7.ParameterName = "@popStamp";
                insertCommand.Parameters.Add(insertParam7);
                var insertParam8 = insertCommand.CreateParameter();
                insertParam8.DbType = DbType.String;
                insertParam8.ParameterName = "@correlationId";
                insertCommand.Parameters.Add(insertParam8);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.fileId.ToByteArray();
                insertParam3.Value = item.boxId.ToByteArray();
                insertParam4.Value = item.priority;
                insertParam5.Value = item.timeStamp.milliseconds;
                insertParam6.Value = item.value ?? (object)DBNull.Value;
                insertParam7.Value = item.popStamp?.ToByteArray() ?? (object)DBNull.Value;
                insertParam8.Value = item.correlationId ?? (object)DBNull.Value;
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
                var upsertParam1 = upsertCommand.CreateParameter();
                upsertParam1.DbType = DbType.Binary;
                upsertParam1.ParameterName = "@identityId";
                upsertCommand.Parameters.Add(upsertParam1);
                var upsertParam2 = upsertCommand.CreateParameter();
                upsertParam2.DbType = DbType.Binary;
                upsertParam2.ParameterName = "@fileId";
                upsertCommand.Parameters.Add(upsertParam2);
                var upsertParam3 = upsertCommand.CreateParameter();
                upsertParam3.DbType = DbType.Binary;
                upsertParam3.ParameterName = "@boxId";
                upsertCommand.Parameters.Add(upsertParam3);
                var upsertParam4 = upsertCommand.CreateParameter();
                upsertParam4.DbType = DbType.Int32;
                upsertParam4.ParameterName = "@priority";
                upsertCommand.Parameters.Add(upsertParam4);
                var upsertParam5 = upsertCommand.CreateParameter();
                upsertParam5.DbType = DbType.Int64;
                upsertParam5.ParameterName = "@timeStamp";
                upsertCommand.Parameters.Add(upsertParam5);
                var upsertParam6 = upsertCommand.CreateParameter();
                upsertParam6.DbType = DbType.Binary;
                upsertParam6.ParameterName = "@value";
                upsertCommand.Parameters.Add(upsertParam6);
                var upsertParam7 = upsertCommand.CreateParameter();
                upsertParam7.DbType = DbType.Binary;
                upsertParam7.ParameterName = "@popStamp";
                upsertCommand.Parameters.Add(upsertParam7);
                var upsertParam8 = upsertCommand.CreateParameter();
                upsertParam8.DbType = DbType.String;
                upsertParam8.ParameterName = "@correlationId";
                upsertCommand.Parameters.Add(upsertParam8);
                upsertParam1.Value = item.identityId.ToByteArray();
                upsertParam2.Value = item.fileId.ToByteArray();
                upsertParam3.Value = item.boxId.ToByteArray();
                upsertParam4.Value = item.priority;
                upsertParam5.Value = item.timeStamp.milliseconds;
                upsertParam6.Value = item.value ?? (object)DBNull.Value;
                upsertParam7.Value = item.popStamp?.ToByteArray() ?? (object)DBNull.Value;
                upsertParam8.Value = item.correlationId ?? (object)DBNull.Value;
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
                var updateParam1 = updateCommand.CreateParameter();
                updateParam1.DbType = DbType.Binary;
                updateParam1.ParameterName = "@identityId";
                updateCommand.Parameters.Add(updateParam1);
                var updateParam2 = updateCommand.CreateParameter();
                updateParam2.DbType = DbType.Binary;
                updateParam2.ParameterName = "@fileId";
                updateCommand.Parameters.Add(updateParam2);
                var updateParam3 = updateCommand.CreateParameter();
                updateParam3.DbType = DbType.Binary;
                updateParam3.ParameterName = "@boxId";
                updateCommand.Parameters.Add(updateParam3);
                var updateParam4 = updateCommand.CreateParameter();
                updateParam4.DbType = DbType.Int32;
                updateParam4.ParameterName = "@priority";
                updateCommand.Parameters.Add(updateParam4);
                var updateParam5 = updateCommand.CreateParameter();
                updateParam5.DbType = DbType.Int64;
                updateParam5.ParameterName = "@timeStamp";
                updateCommand.Parameters.Add(updateParam5);
                var updateParam6 = updateCommand.CreateParameter();
                updateParam6.DbType = DbType.Binary;
                updateParam6.ParameterName = "@value";
                updateCommand.Parameters.Add(updateParam6);
                var updateParam7 = updateCommand.CreateParameter();
                updateParam7.DbType = DbType.Binary;
                updateParam7.ParameterName = "@popStamp";
                updateCommand.Parameters.Add(updateParam7);
                var updateParam8 = updateCommand.CreateParameter();
                updateParam8.DbType = DbType.String;
                updateParam8.ParameterName = "@correlationId";
                updateCommand.Parameters.Add(updateParam8);
                updateParam1.Value = item.identityId.ToByteArray();
                updateParam2.Value = item.fileId.ToByteArray();
                updateParam3.Value = item.boxId.ToByteArray();
                updateParam4.Value = item.priority;
                updateParam5.Value = item.timeStamp.milliseconds;
                updateParam6.Value = item.value ?? (object)DBNull.Value;
                updateParam7.Value = item.popStamp?.ToByteArray() ?? (object)DBNull.Value;
                updateParam8.Value = item.correlationId ?? (object)DBNull.Value;
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

        protected virtual async Task<int> GetCountAsync()
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

        public static List<string> GetColumnNames()
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
                var delete0Param1 = delete0Command.CreateParameter();
                delete0Param1.DbType = DbType.Binary;
                delete0Param1.ParameterName = "@identityId";
                delete0Command.Parameters.Add(delete0Param1);
                var delete0Param2 = delete0Command.CreateParameter();
                delete0Param2.DbType = DbType.Binary;
                delete0Param2.ParameterName = "@fileId";
                delete0Command.Parameters.Add(delete0Param2);

                delete0Param1.Value = identityId.ToByteArray();
                delete0Param2.Value = fileId.ToByteArray();
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
                var deleteParam1 = deleteCommand.CreateParameter();
                deleteParam1.DbType = DbType.Binary;
                deleteParam1.ParameterName = "@identityId";
                deleteCommand.Parameters.Add(deleteParam1);
                var deleteParam2 = deleteCommand.CreateParameter();
                deleteParam2.DbType = DbType.Binary;
                deleteParam2.ParameterName = "@fileId";
                deleteCommand.Parameters.Add(deleteParam2);

                deleteParam1.Value = identityId.ToByteArray();
                deleteParam2.Value = fileId.ToByteArray();
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
                var get0Param1 = get0Command.CreateParameter();
                get0Param1.DbType = DbType.Binary;
                get0Param1.ParameterName = "@identityId";
                get0Command.Parameters.Add(get0Param1);
                var get0Param2 = get0Command.CreateParameter();
                get0Param2.DbType = DbType.Binary;
                get0Param2.ParameterName = "@fileId";
                get0Command.Parameters.Add(get0Param2);

                get0Param1.Value = identityId.ToByteArray();
                get0Param2.Value = fileId.ToByteArray();
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
