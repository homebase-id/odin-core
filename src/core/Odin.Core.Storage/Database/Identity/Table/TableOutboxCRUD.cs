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
               rowid = "rowid BIGSERIAL PRIMARY KEY,";
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
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.DbType = DbType.Binary;
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.DbType = DbType.Binary;
                insertParam2.ParameterName = "@driveId";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.DbType = DbType.Binary;
                insertParam3.ParameterName = "@fileId";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.DbType = DbType.String;
                insertParam4.ParameterName = "@recipient";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.DbType = DbType.Int32;
                insertParam5.ParameterName = "@type";
                insertCommand.Parameters.Add(insertParam5);
                var insertParam6 = insertCommand.CreateParameter();
                insertParam6.DbType = DbType.Int32;
                insertParam6.ParameterName = "@priority";
                insertCommand.Parameters.Add(insertParam6);
                var insertParam7 = insertCommand.CreateParameter();
                insertParam7.DbType = DbType.Binary;
                insertParam7.ParameterName = "@dependencyFileId";
                insertCommand.Parameters.Add(insertParam7);
                var insertParam8 = insertCommand.CreateParameter();
                insertParam8.DbType = DbType.Int32;
                insertParam8.ParameterName = "@checkOutCount";
                insertCommand.Parameters.Add(insertParam8);
                var insertParam9 = insertCommand.CreateParameter();
                insertParam9.DbType = DbType.Int64;
                insertParam9.ParameterName = "@nextRunTime";
                insertCommand.Parameters.Add(insertParam9);
                var insertParam10 = insertCommand.CreateParameter();
                insertParam10.DbType = DbType.Binary;
                insertParam10.ParameterName = "@value";
                insertCommand.Parameters.Add(insertParam10);
                var insertParam11 = insertCommand.CreateParameter();
                insertParam11.DbType = DbType.Binary;
                insertParam11.ParameterName = "@checkOutStamp";
                insertCommand.Parameters.Add(insertParam11);
                var insertParam12 = insertCommand.CreateParameter();
                insertParam12.DbType = DbType.String;
                insertParam12.ParameterName = "@correlationId";
                insertCommand.Parameters.Add(insertParam12);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.driveId.ToByteArray();
                insertParam3.Value = item.fileId.ToByteArray();
                insertParam4.Value = item.recipient;
                insertParam5.Value = item.type;
                insertParam6.Value = item.priority;
                insertParam7.Value = item.dependencyFileId?.ToByteArray() ?? (object)DBNull.Value;
                insertParam8.Value = item.checkOutCount;
                insertParam9.Value = item.nextRunTime.milliseconds;
                insertParam10.Value = item.value ?? (object)DBNull.Value;
                insertParam11.Value = item.checkOutStamp?.ToByteArray() ?? (object)DBNull.Value;
                insertParam12.Value = item.correlationId ?? (object)DBNull.Value;
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
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.DbType = DbType.Binary;
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.DbType = DbType.Binary;
                insertParam2.ParameterName = "@driveId";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.DbType = DbType.Binary;
                insertParam3.ParameterName = "@fileId";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.DbType = DbType.String;
                insertParam4.ParameterName = "@recipient";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.DbType = DbType.Int32;
                insertParam5.ParameterName = "@type";
                insertCommand.Parameters.Add(insertParam5);
                var insertParam6 = insertCommand.CreateParameter();
                insertParam6.DbType = DbType.Int32;
                insertParam6.ParameterName = "@priority";
                insertCommand.Parameters.Add(insertParam6);
                var insertParam7 = insertCommand.CreateParameter();
                insertParam7.DbType = DbType.Binary;
                insertParam7.ParameterName = "@dependencyFileId";
                insertCommand.Parameters.Add(insertParam7);
                var insertParam8 = insertCommand.CreateParameter();
                insertParam8.DbType = DbType.Int32;
                insertParam8.ParameterName = "@checkOutCount";
                insertCommand.Parameters.Add(insertParam8);
                var insertParam9 = insertCommand.CreateParameter();
                insertParam9.DbType = DbType.Int64;
                insertParam9.ParameterName = "@nextRunTime";
                insertCommand.Parameters.Add(insertParam9);
                var insertParam10 = insertCommand.CreateParameter();
                insertParam10.DbType = DbType.Binary;
                insertParam10.ParameterName = "@value";
                insertCommand.Parameters.Add(insertParam10);
                var insertParam11 = insertCommand.CreateParameter();
                insertParam11.DbType = DbType.Binary;
                insertParam11.ParameterName = "@checkOutStamp";
                insertCommand.Parameters.Add(insertParam11);
                var insertParam12 = insertCommand.CreateParameter();
                insertParam12.DbType = DbType.String;
                insertParam12.ParameterName = "@correlationId";
                insertCommand.Parameters.Add(insertParam12);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.driveId.ToByteArray();
                insertParam3.Value = item.fileId.ToByteArray();
                insertParam4.Value = item.recipient;
                insertParam5.Value = item.type;
                insertParam6.Value = item.priority;
                insertParam7.Value = item.dependencyFileId?.ToByteArray() ?? (object)DBNull.Value;
                insertParam8.Value = item.checkOutCount;
                insertParam9.Value = item.nextRunTime.milliseconds;
                insertParam10.Value = item.value ?? (object)DBNull.Value;
                insertParam11.Value = item.checkOutStamp?.ToByteArray() ?? (object)DBNull.Value;
                insertParam12.Value = item.correlationId ?? (object)DBNull.Value;
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
                var upsertParam1 = upsertCommand.CreateParameter();
                upsertParam1.DbType = DbType.Binary;
                upsertParam1.ParameterName = "@identityId";
                upsertCommand.Parameters.Add(upsertParam1);
                var upsertParam2 = upsertCommand.CreateParameter();
                upsertParam2.DbType = DbType.Binary;
                upsertParam2.ParameterName = "@driveId";
                upsertCommand.Parameters.Add(upsertParam2);
                var upsertParam3 = upsertCommand.CreateParameter();
                upsertParam3.DbType = DbType.Binary;
                upsertParam3.ParameterName = "@fileId";
                upsertCommand.Parameters.Add(upsertParam3);
                var upsertParam4 = upsertCommand.CreateParameter();
                upsertParam4.DbType = DbType.String;
                upsertParam4.ParameterName = "@recipient";
                upsertCommand.Parameters.Add(upsertParam4);
                var upsertParam5 = upsertCommand.CreateParameter();
                upsertParam5.DbType = DbType.Int32;
                upsertParam5.ParameterName = "@type";
                upsertCommand.Parameters.Add(upsertParam5);
                var upsertParam6 = upsertCommand.CreateParameter();
                upsertParam6.DbType = DbType.Int32;
                upsertParam6.ParameterName = "@priority";
                upsertCommand.Parameters.Add(upsertParam6);
                var upsertParam7 = upsertCommand.CreateParameter();
                upsertParam7.DbType = DbType.Binary;
                upsertParam7.ParameterName = "@dependencyFileId";
                upsertCommand.Parameters.Add(upsertParam7);
                var upsertParam8 = upsertCommand.CreateParameter();
                upsertParam8.DbType = DbType.Int32;
                upsertParam8.ParameterName = "@checkOutCount";
                upsertCommand.Parameters.Add(upsertParam8);
                var upsertParam9 = upsertCommand.CreateParameter();
                upsertParam9.DbType = DbType.Int64;
                upsertParam9.ParameterName = "@nextRunTime";
                upsertCommand.Parameters.Add(upsertParam9);
                var upsertParam10 = upsertCommand.CreateParameter();
                upsertParam10.DbType = DbType.Binary;
                upsertParam10.ParameterName = "@value";
                upsertCommand.Parameters.Add(upsertParam10);
                var upsertParam11 = upsertCommand.CreateParameter();
                upsertParam11.DbType = DbType.Binary;
                upsertParam11.ParameterName = "@checkOutStamp";
                upsertCommand.Parameters.Add(upsertParam11);
                var upsertParam12 = upsertCommand.CreateParameter();
                upsertParam12.DbType = DbType.String;
                upsertParam12.ParameterName = "@correlationId";
                upsertCommand.Parameters.Add(upsertParam12);
                upsertParam1.Value = item.identityId.ToByteArray();
                upsertParam2.Value = item.driveId.ToByteArray();
                upsertParam3.Value = item.fileId.ToByteArray();
                upsertParam4.Value = item.recipient;
                upsertParam5.Value = item.type;
                upsertParam6.Value = item.priority;
                upsertParam7.Value = item.dependencyFileId?.ToByteArray() ?? (object)DBNull.Value;
                upsertParam8.Value = item.checkOutCount;
                upsertParam9.Value = item.nextRunTime.milliseconds;
                upsertParam10.Value = item.value ?? (object)DBNull.Value;
                upsertParam11.Value = item.checkOutStamp?.ToByteArray() ?? (object)DBNull.Value;
                upsertParam12.Value = item.correlationId ?? (object)DBNull.Value;
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
                var updateParam1 = updateCommand.CreateParameter();
                updateParam1.DbType = DbType.Binary;
                updateParam1.ParameterName = "@identityId";
                updateCommand.Parameters.Add(updateParam1);
                var updateParam2 = updateCommand.CreateParameter();
                updateParam2.DbType = DbType.Binary;
                updateParam2.ParameterName = "@driveId";
                updateCommand.Parameters.Add(updateParam2);
                var updateParam3 = updateCommand.CreateParameter();
                updateParam3.DbType = DbType.Binary;
                updateParam3.ParameterName = "@fileId";
                updateCommand.Parameters.Add(updateParam3);
                var updateParam4 = updateCommand.CreateParameter();
                updateParam4.DbType = DbType.String;
                updateParam4.ParameterName = "@recipient";
                updateCommand.Parameters.Add(updateParam4);
                var updateParam5 = updateCommand.CreateParameter();
                updateParam5.DbType = DbType.Int32;
                updateParam5.ParameterName = "@type";
                updateCommand.Parameters.Add(updateParam5);
                var updateParam6 = updateCommand.CreateParameter();
                updateParam6.DbType = DbType.Int32;
                updateParam6.ParameterName = "@priority";
                updateCommand.Parameters.Add(updateParam6);
                var updateParam7 = updateCommand.CreateParameter();
                updateParam7.DbType = DbType.Binary;
                updateParam7.ParameterName = "@dependencyFileId";
                updateCommand.Parameters.Add(updateParam7);
                var updateParam8 = updateCommand.CreateParameter();
                updateParam8.DbType = DbType.Int32;
                updateParam8.ParameterName = "@checkOutCount";
                updateCommand.Parameters.Add(updateParam8);
                var updateParam9 = updateCommand.CreateParameter();
                updateParam9.DbType = DbType.Int64;
                updateParam9.ParameterName = "@nextRunTime";
                updateCommand.Parameters.Add(updateParam9);
                var updateParam10 = updateCommand.CreateParameter();
                updateParam10.DbType = DbType.Binary;
                updateParam10.ParameterName = "@value";
                updateCommand.Parameters.Add(updateParam10);
                var updateParam11 = updateCommand.CreateParameter();
                updateParam11.DbType = DbType.Binary;
                updateParam11.ParameterName = "@checkOutStamp";
                updateCommand.Parameters.Add(updateParam11);
                var updateParam12 = updateCommand.CreateParameter();
                updateParam12.DbType = DbType.String;
                updateParam12.ParameterName = "@correlationId";
                updateCommand.Parameters.Add(updateParam12);
                updateParam1.Value = item.identityId.ToByteArray();
                updateParam2.Value = item.driveId.ToByteArray();
                updateParam3.Value = item.fileId.ToByteArray();
                updateParam4.Value = item.recipient;
                updateParam5.Value = item.type;
                updateParam6.Value = item.priority;
                updateParam7.Value = item.dependencyFileId?.ToByteArray() ?? (object)DBNull.Value;
                updateParam8.Value = item.checkOutCount;
                updateParam9.Value = item.nextRunTime.milliseconds;
                updateParam10.Value = item.value ?? (object)DBNull.Value;
                updateParam11.Value = item.checkOutStamp?.ToByteArray() ?? (object)DBNull.Value;
                updateParam12.Value = item.correlationId ?? (object)DBNull.Value;
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
            item.modified = (rdr[14] == DBNull.Value) ? item.created : new UnixTimeUtc((long)rdr[14]); // HACK
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
                var delete0Param1 = delete0Command.CreateParameter();
                delete0Param1.DbType = DbType.Binary;
                delete0Param1.ParameterName = "@identityId";
                delete0Command.Parameters.Add(delete0Param1);
                var delete0Param2 = delete0Command.CreateParameter();
                delete0Param2.DbType = DbType.Binary;
                delete0Param2.ParameterName = "@driveId";
                delete0Command.Parameters.Add(delete0Param2);
                var delete0Param3 = delete0Command.CreateParameter();
                delete0Param3.DbType = DbType.Binary;
                delete0Param3.ParameterName = "@fileId";
                delete0Command.Parameters.Add(delete0Param3);
                var delete0Param4 = delete0Command.CreateParameter();
                delete0Param4.DbType = DbType.String;
                delete0Param4.ParameterName = "@recipient";
                delete0Command.Parameters.Add(delete0Param4);

                delete0Param1.Value = identityId.ToByteArray();
                delete0Param2.Value = driveId.ToByteArray();
                delete0Param3.Value = fileId.ToByteArray();
                delete0Param4.Value = recipient;
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
                var deleteParam1 = deleteCommand.CreateParameter();
                deleteParam1.DbType = DbType.Binary;
                deleteParam1.ParameterName = "@identityId";
                deleteCommand.Parameters.Add(deleteParam1);
                var deleteParam2 = deleteCommand.CreateParameter();
                deleteParam2.DbType = DbType.Binary;
                deleteParam2.ParameterName = "@driveId";
                deleteCommand.Parameters.Add(deleteParam2);
                var deleteParam3 = deleteCommand.CreateParameter();
                deleteParam3.DbType = DbType.Binary;
                deleteParam3.ParameterName = "@fileId";
                deleteCommand.Parameters.Add(deleteParam3);
                var deleteParam4 = deleteCommand.CreateParameter();
                deleteParam4.DbType = DbType.String;
                deleteParam4.ParameterName = "@recipient";
                deleteCommand.Parameters.Add(deleteParam4);

                deleteParam1.Value = identityId.ToByteArray();
                deleteParam2.Value = driveId.ToByteArray();
                deleteParam3.Value = fileId.ToByteArray();
                deleteParam4.Value = recipient;
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
            item.modified = (rdr[10] == DBNull.Value) ? item.created : new UnixTimeUtc((long)rdr[10]); // HACK
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
                                             "WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId AND recipient = @recipient LIMIT 1;"+
                                             ";";
                var get0Param1 = get0Command.CreateParameter();
                get0Param1.DbType = DbType.Binary;
                get0Param1.ParameterName = "@identityId";
                get0Command.Parameters.Add(get0Param1);
                var get0Param2 = get0Command.CreateParameter();
                get0Param2.DbType = DbType.Binary;
                get0Param2.ParameterName = "@driveId";
                get0Command.Parameters.Add(get0Param2);
                var get0Param3 = get0Command.CreateParameter();
                get0Param3.DbType = DbType.Binary;
                get0Param3.ParameterName = "@fileId";
                get0Command.Parameters.Add(get0Param3);
                var get0Param4 = get0Command.CreateParameter();
                get0Param4.DbType = DbType.String;
                get0Param4.ParameterName = "@recipient";
                get0Command.Parameters.Add(get0Param4);

                get0Param1.Value = identityId.ToByteArray();
                get0Param2.Value = driveId.ToByteArray();
                get0Param3.Value = fileId.ToByteArray();
                get0Param4.Value = recipient;
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
            item.modified = (rdr[11] == DBNull.Value) ? item.created : new UnixTimeUtc((long)rdr[11]); // HACK
            return item;
       }

        protected virtual async Task<List<OutboxRecord>> GetAsync(Guid identityId,Guid driveId,Guid fileId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get1Command = cn.CreateCommand();
            {
                get1Command.CommandText = "SELECT rowId,recipient,type,priority,dependencyFileId,checkOutCount,nextRunTime,value,checkOutStamp,correlationId,created,modified FROM Outbox " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId;"+
                                             ";";
                var get1Param1 = get1Command.CreateParameter();
                get1Param1.DbType = DbType.Binary;
                get1Param1.ParameterName = "@identityId";
                get1Command.Parameters.Add(get1Param1);
                var get1Param2 = get1Command.CreateParameter();
                get1Param2.DbType = DbType.Binary;
                get1Param2.ParameterName = "@driveId";
                get1Command.Parameters.Add(get1Param2);
                var get1Param3 = get1Command.CreateParameter();
                get1Param3.DbType = DbType.Binary;
                get1Param3.ParameterName = "@fileId";
                get1Command.Parameters.Add(get1Param3);

                get1Param1.Value = identityId.ToByteArray();
                get1Param2.Value = driveId.ToByteArray();
                get1Param3.Value = fileId.ToByteArray();
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
