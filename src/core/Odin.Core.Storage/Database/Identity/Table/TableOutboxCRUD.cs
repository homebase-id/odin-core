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
    public record OutboxRecord
    {
        public Int64 rowId { get; set; }
        public Guid identityId { get; set; }
        public Guid driveId { get; set; }
        public Guid fileId { get; set; }
        public string recipient { get; set; }
        public Int32 type { get; set; }
        public Int32 priority { get; set; }
        public Guid? dependencyFileId { get; set; }
        public Int32 checkOutCount { get; set; }
        public UnixTimeUtc nextRunTime { get; set; }
        public byte[] value { get; set; }
        public Guid? checkOutStamp { get; set; }
        public string correlationId { get; set; }
        public UnixTimeUtc created { get; set; }
        public UnixTimeUtc modified { get; set; }
        public void Validate()
        {
            identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            driveId.AssertGuidNotEmpty("Guid parameter driveId cannot be set to Empty GUID.");
            fileId.AssertGuidNotEmpty("Guid parameter fileId cannot be set to Empty GUID.");
            if (recipient == null) throw new OdinDatabaseValidationException("Cannot be null recipient");
            if (recipient?.Length < 0) throw new OdinDatabaseValidationException($"Too short recipient, was {recipient.Length} (min 0)");
            if (recipient?.Length > 256) throw new OdinDatabaseValidationException($"Too long recipient, was {recipient.Length} (max 256)");
            dependencyFileId.AssertGuidNotEmpty("Guid parameter dependencyFileId cannot be set to Empty GUID.");
            if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short value, was {value.Length} (min 0)");
            if (value?.Length > 65535) throw new OdinDatabaseValidationException($"Too long value, was {value.Length} (max 65535)");
            checkOutStamp.AssertGuidNotEmpty("Guid parameter checkOutStamp cannot be set to Empty GUID.");
            if (correlationId?.Length < 0) throw new OdinDatabaseValidationException($"Too short correlationId, was {correlationId.Length} (min 0)");
            if (correlationId?.Length > 64) throw new OdinDatabaseValidationException($"Too long correlationId, was {correlationId.Length} (max 64)");
        }
    } // End of record OutboxRecord

    public abstract class TableOutboxCRUD : TableBase
    {
        private ScopedIdentityConnectionFactory _scopedConnectionFactory { get; init; }
        public override string TableName { get; } = "Outbox";

        protected TableOutboxCRUD(ScopedIdentityConnectionFactory scopedConnectionFactory)
        {
            _scopedConnectionFactory = scopedConnectionFactory;
        }


        public override async Task EnsureTableExistsAsync(bool dropExisting = false)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            if (dropExisting)
                await SqlHelper.DeleteTableAsync(cn, "Outbox");
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE Outbox IS '{ \"Version\": 0 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS Outbox( -- { \"Version\": 0 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"driveId BYTEA NOT NULL, "
                   +"fileId BYTEA NOT NULL, "
                   +"recipient TEXT NOT NULL, "
                   +"type BIGINT NOT NULL, "
                   +"priority BIGINT NOT NULL, "
                   +"dependencyFileId BYTEA , "
                   +"checkOutCount BIGINT NOT NULL, "
                   +"nextRunTime BIGINT NOT NULL, "
                   +"value BYTEA , "
                   +"checkOutStamp BYTEA , "
                   +"correlationId TEXT , "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +", UNIQUE(identityId,driveId,fileId,recipient)"
                   +$"){wori};"
                   +"CREATE INDEX IF NOT EXISTS Idx0Outbox ON Outbox(identityId,nextRunTime);"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "Outbox", createSql, commentSql);
        }

        protected virtual async Task<int> InsertAsync(OutboxRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr = insertCommand.SqlNow();
                insertCommand.CommandText = "INSERT INTO Outbox (identityId,driveId,fileId,recipient,type,priority,dependencyFileId,checkOutCount,nextRunTime,value,checkOutStamp,correlationId,created,modified) " +
                                           $"VALUES (@identityId,@driveId,@fileId,@recipient,@type,@priority,@dependencyFileId,@checkOutCount,@nextRunTime,@value,@checkOutStamp,@correlationId,{sqlNowStr},{sqlNowStr})"+
                                            "RETURNING created,modified,rowId;";
                insertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                insertCommand.AddParameter("@driveId", DbType.Binary, item.driveId);
                insertCommand.AddParameter("@fileId", DbType.Binary, item.fileId);
                insertCommand.AddParameter("@recipient", DbType.String, item.recipient);
                insertCommand.AddParameter("@type", DbType.Int32, item.type);
                insertCommand.AddParameter("@priority", DbType.Int32, item.priority);
                insertCommand.AddParameter("@dependencyFileId", DbType.Binary, item.dependencyFileId);
                insertCommand.AddParameter("@checkOutCount", DbType.Int32, item.checkOutCount);
                insertCommand.AddParameter("@nextRunTime", DbType.Int64, item.nextRunTime.milliseconds);
                insertCommand.AddParameter("@value", DbType.Binary, item.value);
                insertCommand.AddParameter("@checkOutStamp", DbType.Binary, item.checkOutStamp);
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

        protected virtual async Task<bool> TryInsertAsync(OutboxRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr = insertCommand.SqlNow();
                insertCommand.CommandText = "INSERT INTO Outbox (identityId,driveId,fileId,recipient,type,priority,dependencyFileId,checkOutCount,nextRunTime,value,checkOutStamp,correlationId,created,modified) " +
                                            $"VALUES (@identityId,@driveId,@fileId,@recipient,@type,@priority,@dependencyFileId,@checkOutCount,@nextRunTime,@value,@checkOutStamp,@correlationId,{sqlNowStr},{sqlNowStr}) " +
                                            "ON CONFLICT DO NOTHING "+
                                            "RETURNING created,modified,rowId;";
                insertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                insertCommand.AddParameter("@driveId", DbType.Binary, item.driveId);
                insertCommand.AddParameter("@fileId", DbType.Binary, item.fileId);
                insertCommand.AddParameter("@recipient", DbType.String, item.recipient);
                insertCommand.AddParameter("@type", DbType.Int32, item.type);
                insertCommand.AddParameter("@priority", DbType.Int32, item.priority);
                insertCommand.AddParameter("@dependencyFileId", DbType.Binary, item.dependencyFileId);
                insertCommand.AddParameter("@checkOutCount", DbType.Int32, item.checkOutCount);
                insertCommand.AddParameter("@nextRunTime", DbType.Int64, item.nextRunTime.milliseconds);
                insertCommand.AddParameter("@value", DbType.Binary, item.value);
                insertCommand.AddParameter("@checkOutStamp", DbType.Binary, item.checkOutStamp);
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

        protected virtual async Task<int> UpsertAsync(OutboxRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                string sqlNowStr = upsertCommand.SqlNow();
                upsertCommand.CommandText = "INSERT INTO Outbox (identityId,driveId,fileId,recipient,type,priority,dependencyFileId,checkOutCount,nextRunTime,value,checkOutStamp,correlationId,created,modified) " +
                                            $"VALUES (@identityId,@driveId,@fileId,@recipient,@type,@priority,@dependencyFileId,@checkOutCount,@nextRunTime,@value,@checkOutStamp,@correlationId,{sqlNowStr},{sqlNowStr})"+
                                            "ON CONFLICT (identityId,driveId,fileId,recipient) DO UPDATE "+
                                            $"SET type = @type,priority = @priority,dependencyFileId = @dependencyFileId,checkOutCount = @checkOutCount,nextRunTime = @nextRunTime,value = @value,checkOutStamp = @checkOutStamp,correlationId = @correlationId,modified = {upsertCommand.SqlMax()}(Outbox.modified+1,{sqlNowStr}) "+
                                            "RETURNING created,modified,rowId;";
                upsertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                upsertCommand.AddParameter("@driveId", DbType.Binary, item.driveId);
                upsertCommand.AddParameter("@fileId", DbType.Binary, item.fileId);
                upsertCommand.AddParameter("@recipient", DbType.String, item.recipient);
                upsertCommand.AddParameter("@type", DbType.Int32, item.type);
                upsertCommand.AddParameter("@priority", DbType.Int32, item.priority);
                upsertCommand.AddParameter("@dependencyFileId", DbType.Binary, item.dependencyFileId);
                upsertCommand.AddParameter("@checkOutCount", DbType.Int32, item.checkOutCount);
                upsertCommand.AddParameter("@nextRunTime", DbType.Int64, item.nextRunTime.milliseconds);
                upsertCommand.AddParameter("@value", DbType.Binary, item.value);
                upsertCommand.AddParameter("@checkOutStamp", DbType.Binary, item.checkOutStamp);
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

        protected virtual async Task<int> UpdateAsync(OutboxRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                string sqlNowStr = updateCommand.SqlNow();
                updateCommand.CommandText = "UPDATE Outbox " +
                                            $"SET type = @type,priority = @priority,dependencyFileId = @dependencyFileId,checkOutCount = @checkOutCount,nextRunTime = @nextRunTime,value = @value,checkOutStamp = @checkOutStamp,correlationId = @correlationId,modified = {updateCommand.SqlMax()}(Outbox.modified+1,{sqlNowStr}) "+
                                            "WHERE (identityId = @identityId AND driveId = @driveId AND fileId = @fileId AND recipient = @recipient) "+
                                            "RETURNING created,modified,rowId;";
                updateCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                updateCommand.AddParameter("@driveId", DbType.Binary, item.driveId);
                updateCommand.AddParameter("@fileId", DbType.Binary, item.fileId);
                updateCommand.AddParameter("@recipient", DbType.String, item.recipient);
                updateCommand.AddParameter("@type", DbType.Int32, item.type);
                updateCommand.AddParameter("@priority", DbType.Int32, item.priority);
                updateCommand.AddParameter("@dependencyFileId", DbType.Binary, item.dependencyFileId);
                updateCommand.AddParameter("@checkOutCount", DbType.Int32, item.checkOutCount);
                updateCommand.AddParameter("@nextRunTime", DbType.Int64, item.nextRunTime.milliseconds);
                updateCommand.AddParameter("@value", DbType.Binary, item.value);
                updateCommand.AddParameter("@checkOutStamp", DbType.Binary, item.checkOutStamp);
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
                getCountCommand.CommandText = "SELECT COUNT(*) FROM Outbox;";
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
            sl.Add("driveId");
            sl.Add("fileId");
            sl.Add("recipient");
            sl.Add("type");
            sl.Add("priority");
            sl.Add("dependencyFileId");
            sl.Add("checkOutCount");
            sl.Add("nextRunTime");
            sl.Add("value");
            sl.Add("checkOutStamp");
            sl.Add("correlationId");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        // SELECT rowId,identityId,driveId,fileId,recipient,type,priority,dependencyFileId,checkOutCount,nextRunTime,value,checkOutStamp,correlationId,created,modified
        protected OutboxRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<OutboxRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new OutboxRecord();
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.identityId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.driveId = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[2]);
            item.fileId = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[3]);
            item.recipient = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[4];
            item.type = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[5];
            item.priority = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[6];
            item.dependencyFileId = (rdr[7] == DBNull.Value) ? null : new Guid((byte[])rdr[7]);
            item.checkOutCount = (rdr[8] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[8];
            item.nextRunTime = (rdr[9] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[9]);
            item.value = (rdr[10] == DBNull.Value) ? null : (byte[])(rdr[10]);
            if (item.value?.Length < 0)
                throw new Exception("Too little data in value...");
            item.checkOutStamp = (rdr[11] == DBNull.Value) ? null : new Guid((byte[])rdr[11]);
            item.correlationId = (rdr[12] == DBNull.Value) ? null : (string)rdr[12];
            item.created = (rdr[13] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[13]);
            item.modified = (rdr[14] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[14]);
            return item;
       }

        protected virtual async Task<int> DeleteAsync(Guid identityId,Guid driveId,Guid fileId,string recipient)
        {
            if (recipient == null) throw new OdinDatabaseValidationException("Cannot be null recipient");
            if (recipient?.Length < 0) throw new OdinDatabaseValidationException($"Too short recipient, was {recipient.Length} (min 0)");
            if (recipient?.Length > 256) throw new OdinDatabaseValidationException($"Too long recipient, was {recipient.Length} (max 256)");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM Outbox " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId AND recipient = @recipient";

                delete0Command.AddParameter("@identityId", DbType.Binary, identityId);
                delete0Command.AddParameter("@driveId", DbType.Binary, driveId);
                delete0Command.AddParameter("@fileId", DbType.Binary, fileId);
                delete0Command.AddParameter("@recipient", DbType.String, recipient);
                var count = await delete0Command.ExecuteNonQueryAsync();
                return count;
            }
        }

        protected virtual async Task<OutboxRecord> PopAsync(Guid identityId,Guid driveId,Guid fileId,string recipient)
        {
            if (recipient == null) throw new OdinDatabaseValidationException("Cannot be null recipient");
            if (recipient?.Length < 0) throw new OdinDatabaseValidationException($"Too short recipient, was {recipient.Length} (min 0)");
            if (recipient?.Length > 256) throw new OdinDatabaseValidationException($"Too long recipient, was {recipient.Length} (max 256)");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var deleteCommand = cn.CreateCommand();
            {
                deleteCommand.CommandText = "DELETE FROM Outbox " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId AND recipient = @recipient " + 
                                             "RETURNING rowId,type,priority,dependencyFileId,checkOutCount,nextRunTime,value,checkOutStamp,correlationId,created,modified";

                deleteCommand.AddParameter("@identityId", DbType.Binary, identityId);
                deleteCommand.AddParameter("@driveId", DbType.Binary, driveId);
                deleteCommand.AddParameter("@fileId", DbType.Binary, fileId);
                deleteCommand.AddParameter("@recipient", DbType.String, recipient);
                using (var rdr = await deleteCommand.ExecuteReaderAsync(CommandBehavior.SingleRow))
                {
                    if (await rdr.ReadAsync())
                    {
                       return ReadRecordFromReader0(rdr,identityId,driveId,fileId,recipient);
                    }
                    else
                    {
                       return null;
                    }
                }
            }
        }

        protected OutboxRecord ReadRecordFromReader0(DbDataReader rdr,Guid identityId,Guid driveId,Guid fileId,string recipient)
        {
            if (recipient == null) throw new OdinDatabaseValidationException("Cannot be null recipient");
            if (recipient?.Length < 0) throw new OdinDatabaseValidationException($"Too short recipient, was {recipient.Length} (min 0)");
            if (recipient?.Length > 256) throw new OdinDatabaseValidationException($"Too long recipient, was {recipient.Length} (max 256)");
            var result = new List<OutboxRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new OutboxRecord();
            item.identityId = identityId;
            item.driveId = driveId;
            item.fileId = fileId;
            item.recipient = recipient;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.type = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[1];
            item.priority = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[2];
            item.dependencyFileId = (rdr[3] == DBNull.Value) ? null : new Guid((byte[])rdr[3]);
            item.checkOutCount = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[4];
            item.nextRunTime = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[5]);
            item.value = (rdr[6] == DBNull.Value) ? null : (byte[])(rdr[6]);
            if (item.value?.Length < 0)
                throw new Exception("Too little data in value...");
            item.checkOutStamp = (rdr[7] == DBNull.Value) ? null : new Guid((byte[])rdr[7]);
            item.correlationId = (rdr[8] == DBNull.Value) ? null : (string)rdr[8];
            item.created = (rdr[9] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[9]);
            item.modified = (rdr[10] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[10]);
            return item;
       }

        protected virtual async Task<OutboxRecord> GetAsync(Guid identityId,Guid driveId,Guid fileId,string recipient)
        {
            if (recipient == null) throw new OdinDatabaseValidationException("Cannot be null recipient");
            if (recipient?.Length < 0) throw new OdinDatabaseValidationException($"Too short recipient, was {recipient.Length} (min 0)");
            if (recipient?.Length > 256) throw new OdinDatabaseValidationException($"Too long recipient, was {recipient.Length} (max 256)");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT rowId,type,priority,dependencyFileId,checkOutCount,nextRunTime,value,checkOutStamp,correlationId,created,modified FROM Outbox " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId AND recipient = @recipient LIMIT 1 "+
                                             ";";

                get0Command.AddParameter("@identityId", DbType.Binary, identityId);
                get0Command.AddParameter("@driveId", DbType.Binary, driveId);
                get0Command.AddParameter("@fileId", DbType.Binary, fileId);
                get0Command.AddParameter("@recipient", DbType.String, recipient);
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr,identityId,driveId,fileId,recipient);
                        return r;
                    } // using
                } //
            } // using
        }

        protected OutboxRecord ReadRecordFromReader1(DbDataReader rdr,Guid identityId,Guid driveId,Guid fileId)
        {
            var result = new List<OutboxRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new OutboxRecord();
            item.identityId = identityId;
            item.driveId = driveId;
            item.fileId = fileId;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.recipient = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[1];
            item.type = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[2];
            item.priority = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[3];
            item.dependencyFileId = (rdr[4] == DBNull.Value) ? null : new Guid((byte[])rdr[4]);
            item.checkOutCount = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[5];
            item.nextRunTime = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[6]);
            item.value = (rdr[7] == DBNull.Value) ? null : (byte[])(rdr[7]);
            if (item.value?.Length < 0)
                throw new Exception("Too little data in value...");
            item.checkOutStamp = (rdr[8] == DBNull.Value) ? null : new Guid((byte[])rdr[8]);
            item.correlationId = (rdr[9] == DBNull.Value) ? null : (string)rdr[9];
            item.created = (rdr[10] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[10]);
            item.modified = (rdr[11] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[11]);
            return item;
       }

        protected virtual async Task<List<OutboxRecord>> GetAsync(Guid identityId,Guid driveId,Guid fileId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get1Command = cn.CreateCommand();
            {
                get1Command.CommandText = "SELECT rowId,recipient,type,priority,dependencyFileId,checkOutCount,nextRunTime,value,checkOutStamp,correlationId,created,modified FROM Outbox " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId "+
                                             ";";

                get1Command.AddParameter("@identityId", DbType.Binary, identityId);
                get1Command.AddParameter("@driveId", DbType.Binary, driveId);
                get1Command.AddParameter("@fileId", DbType.Binary, fileId);
                {
                    using (var rdr = await get1Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return new List<OutboxRecord>();
                        }
                        var result = new List<OutboxRecord>();
                        while (true)
                        {
                            result.Add(ReadRecordFromReader1(rdr,identityId,driveId,fileId));
                            if (!await rdr.ReadAsync())
                                break;
                        }
                        return result;
                    } // using
                } //
            } // using
        }

        protected virtual async Task<(List<OutboxRecord>, Int64? nextCursor)> PagingByRowIdAsync(int count, Int64? inCursor)
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
                getPaging0Command.CommandText = "SELECT rowId,identityId,driveId,fileId,recipient,type,priority,dependencyFileId,checkOutCount,nextRunTime,value,checkOutStamp,correlationId,created,modified FROM Outbox " +
                                            "WHERE rowId > @rowId  ORDER BY rowId ASC  LIMIT @count;";

                getPaging0Command.AddParameter("@rowId", DbType.Int64, inCursor);
                getPaging0Command.AddParameter("@count", DbType.Int64, count+1);

                {
                    await using (var rdr = await getPaging0Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        var result = new List<OutboxRecord>();
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
