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
    public class OutboxRecord
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
        private Guid _fileId;
        public Guid fileId
        {
           get {
                   return _fileId;
               }
           set {
                  _fileId = value;
               }
        }
        private string _recipient;
        public string recipient
        {
           get {
                   return _recipient;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 0) throw new Exception("Too short");
                    if (value?.Length > 65535) throw new Exception("Too long");
                  _recipient = value;
               }
        }
        internal string recipientNoLengthCheck
        {
           get {
                   return _recipient;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 0) throw new Exception("Too short");
                  _recipient = value;
               }
        }
        private Int32 _type;
        public Int32 type
        {
           get {
                   return _type;
               }
           set {
                  _type = value;
               }
        }
        private Int32 _priority;
        public Int32 priority
        {
           get {
                   return _priority;
               }
           set {
                  _priority = value;
               }
        }
        private Guid? _dependencyFileId;
        public Guid? dependencyFileId
        {
           get {
                   return _dependencyFileId;
               }
           set {
                  _dependencyFileId = value;
               }
        }
        private Int32 _checkOutCount;
        public Int32 checkOutCount
        {
           get {
                   return _checkOutCount;
               }
           set {
                  _checkOutCount = value;
               }
        }
        private UnixTimeUtc _nextRunTime;
        public UnixTimeUtc nextRunTime
        {
           get {
                   return _nextRunTime;
               }
           set {
                  _nextRunTime = value;
               }
        }
        private byte[] _value;
        public byte[] value
        {
           get {
                   return _value;
               }
           set {
                    if (value?.Length < 0) throw new Exception("Too short");
                    if (value?.Length > 65535) throw new Exception("Too long");
                  _value = value;
               }
        }
        internal byte[] valueNoLengthCheck
        {
           get {
                   return _value;
               }
           set {
                    if (value?.Length < 0) throw new Exception("Too short");
                  _value = value;
               }
        }
        private Guid? _checkOutStamp;
        public Guid? checkOutStamp
        {
           get {
                   return _checkOutStamp;
               }
           set {
                  _checkOutStamp = value;
               }
        }
        private string _correlationId;
        public string correlationId
        {
           get {
                   return _correlationId;
               }
           set {
                    if (value?.Length < 0) throw new Exception("Too short");
                    if (value?.Length > 64) throw new Exception("Too long");
                  _correlationId = value;
               }
        }
        internal string correlationIdNoLengthCheck
        {
           get {
                   return _correlationId;
               }
           set {
                    if (value?.Length < 0) throw new Exception("Too short");
                  _correlationId = value;
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
    } // End of class OutboxRecord

    public abstract class TableOutboxCRUD
    {
        private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory;

        protected TableOutboxCRUD(CacheHelper cache, ScopedIdentityConnectionFactory scopedConnectionFactory)
        {
            _scopedConnectionFactory = scopedConnectionFactory;
        }


        public virtual async Task EnsureTableExistsAsync(bool dropExisting = false)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var cmd = cn.CreateCommand();
            if (dropExisting)
            {
                cmd.CommandText = "DROP TABLE IF EXISTS outbox;";
                await cmd.ExecuteNonQueryAsync();
            }
            var rowid = "";
            if (_scopedConnectionFactory.DatabaseType == DatabaseType.Postgres)
            {
                   rowid = ", rowid BIGSERIAL NOT NULL UNIQUE ";
            }
            cmd.CommandText =
                "CREATE TABLE IF NOT EXISTS outbox("
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
                   +"modified BIGINT  "
                   + rowid
                   +", PRIMARY KEY (identityId,driveId,fileId,recipient)"
                   +");"
                   +"CREATE INDEX IF NOT EXISTS Idx0TableOutboxCRUD ON outbox(identityId,nextRunTime);"
                   ;
            await cmd.ExecuteNonQueryAsync();
        }

        protected virtual async Task<int> InsertAsync(OutboxRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.driveId.AssertGuidNotEmpty("Guid parameter driveId cannot be set to Empty GUID.");
            item.fileId.AssertGuidNotEmpty("Guid parameter fileId cannot be set to Empty GUID.");
            item.dependencyFileId.AssertGuidNotEmpty("Guid parameter dependencyFileId cannot be set to Empty GUID.");
            item.checkOutStamp.AssertGuidNotEmpty("Guid parameter checkOutStamp cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO outbox (identityId,driveId,fileId,recipient,type,priority,dependencyFileId,checkOutCount,nextRunTime,value,checkOutStamp,correlationId,created,modified) " +
                                             "VALUES (@identityId,@driveId,@fileId,@recipient,@type,@priority,@dependencyFileId,@checkOutCount,@nextRunTime,@value,@checkOutStamp,@correlationId,@created,@modified)";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.ParameterName = "@driveId";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.ParameterName = "@fileId";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.ParameterName = "@recipient";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.ParameterName = "@type";
                insertCommand.Parameters.Add(insertParam5);
                var insertParam6 = insertCommand.CreateParameter();
                insertParam6.ParameterName = "@priority";
                insertCommand.Parameters.Add(insertParam6);
                var insertParam7 = insertCommand.CreateParameter();
                insertParam7.ParameterName = "@dependencyFileId";
                insertCommand.Parameters.Add(insertParam7);
                var insertParam8 = insertCommand.CreateParameter();
                insertParam8.ParameterName = "@checkOutCount";
                insertCommand.Parameters.Add(insertParam8);
                var insertParam9 = insertCommand.CreateParameter();
                insertParam9.ParameterName = "@nextRunTime";
                insertCommand.Parameters.Add(insertParam9);
                var insertParam10 = insertCommand.CreateParameter();
                insertParam10.ParameterName = "@value";
                insertCommand.Parameters.Add(insertParam10);
                var insertParam11 = insertCommand.CreateParameter();
                insertParam11.ParameterName = "@checkOutStamp";
                insertCommand.Parameters.Add(insertParam11);
                var insertParam12 = insertCommand.CreateParameter();
                insertParam12.ParameterName = "@correlationId";
                insertCommand.Parameters.Add(insertParam12);
                var insertParam13 = insertCommand.CreateParameter();
                insertParam13.ParameterName = "@created";
                insertCommand.Parameters.Add(insertParam13);
                var insertParam14 = insertCommand.CreateParameter();
                insertParam14.ParameterName = "@modified";
                insertCommand.Parameters.Add(insertParam14);
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
                var now = UnixTimeUtc.Now();
                insertParam13.Value = now.milliseconds;
                item.modified = null;
                insertParam14.Value = DBNull.Value;
                var count = await insertCommand.ExecuteNonQueryAsync();
                if (count > 0)
                {
                     item.created = now;
                }
                return count;
            }
        }

        protected virtual async Task<bool> TryInsertAsync(OutboxRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.driveId.AssertGuidNotEmpty("Guid parameter driveId cannot be set to Empty GUID.");
            item.fileId.AssertGuidNotEmpty("Guid parameter fileId cannot be set to Empty GUID.");
            item.dependencyFileId.AssertGuidNotEmpty("Guid parameter dependencyFileId cannot be set to Empty GUID.");
            item.checkOutStamp.AssertGuidNotEmpty("Guid parameter checkOutStamp cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO outbox (identityId,driveId,fileId,recipient,type,priority,dependencyFileId,checkOutCount,nextRunTime,value,checkOutStamp,correlationId,created,modified) " +
                                             "VALUES (@identityId,@driveId,@fileId,@recipient,@type,@priority,@dependencyFileId,@checkOutCount,@nextRunTime,@value,@checkOutStamp,@correlationId,@created,@modified) " +
                                             "ON CONFLICT DO NOTHING";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.ParameterName = "@driveId";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.ParameterName = "@fileId";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.ParameterName = "@recipient";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.ParameterName = "@type";
                insertCommand.Parameters.Add(insertParam5);
                var insertParam6 = insertCommand.CreateParameter();
                insertParam6.ParameterName = "@priority";
                insertCommand.Parameters.Add(insertParam6);
                var insertParam7 = insertCommand.CreateParameter();
                insertParam7.ParameterName = "@dependencyFileId";
                insertCommand.Parameters.Add(insertParam7);
                var insertParam8 = insertCommand.CreateParameter();
                insertParam8.ParameterName = "@checkOutCount";
                insertCommand.Parameters.Add(insertParam8);
                var insertParam9 = insertCommand.CreateParameter();
                insertParam9.ParameterName = "@nextRunTime";
                insertCommand.Parameters.Add(insertParam9);
                var insertParam10 = insertCommand.CreateParameter();
                insertParam10.ParameterName = "@value";
                insertCommand.Parameters.Add(insertParam10);
                var insertParam11 = insertCommand.CreateParameter();
                insertParam11.ParameterName = "@checkOutStamp";
                insertCommand.Parameters.Add(insertParam11);
                var insertParam12 = insertCommand.CreateParameter();
                insertParam12.ParameterName = "@correlationId";
                insertCommand.Parameters.Add(insertParam12);
                var insertParam13 = insertCommand.CreateParameter();
                insertParam13.ParameterName = "@created";
                insertCommand.Parameters.Add(insertParam13);
                var insertParam14 = insertCommand.CreateParameter();
                insertParam14.ParameterName = "@modified";
                insertCommand.Parameters.Add(insertParam14);
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
                var now = UnixTimeUtc.Now();
                insertParam13.Value = now.milliseconds;
                item.modified = null;
                insertParam14.Value = DBNull.Value;
                var count = await insertCommand.ExecuteNonQueryAsync();
                if (count > 0)
                {
                    item.created = now;
                }
                return count > 0;
            }
        }

        protected virtual async Task<int> UpsertAsync(OutboxRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.driveId.AssertGuidNotEmpty("Guid parameter driveId cannot be set to Empty GUID.");
            item.fileId.AssertGuidNotEmpty("Guid parameter fileId cannot be set to Empty GUID.");
            item.dependencyFileId.AssertGuidNotEmpty("Guid parameter dependencyFileId cannot be set to Empty GUID.");
            item.checkOutStamp.AssertGuidNotEmpty("Guid parameter checkOutStamp cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                upsertCommand.CommandText = "INSERT INTO outbox (identityId,driveId,fileId,recipient,type,priority,dependencyFileId,checkOutCount,nextRunTime,value,checkOutStamp,correlationId,created) " +
                                             "VALUES (@identityId,@driveId,@fileId,@recipient,@type,@priority,@dependencyFileId,@checkOutCount,@nextRunTime,@value,@checkOutStamp,@correlationId,@created)"+
                                             "ON CONFLICT (identityId,driveId,fileId,recipient) DO UPDATE "+
                                             "SET type = @type,priority = @priority,dependencyFileId = @dependencyFileId,checkOutCount = @checkOutCount,nextRunTime = @nextRunTime,value = @value,checkOutStamp = @checkOutStamp,correlationId = @correlationId,modified = @modified "+
                                             "RETURNING created, modified;";
                var upsertParam1 = upsertCommand.CreateParameter();
                upsertParam1.ParameterName = "@identityId";
                upsertCommand.Parameters.Add(upsertParam1);
                var upsertParam2 = upsertCommand.CreateParameter();
                upsertParam2.ParameterName = "@driveId";
                upsertCommand.Parameters.Add(upsertParam2);
                var upsertParam3 = upsertCommand.CreateParameter();
                upsertParam3.ParameterName = "@fileId";
                upsertCommand.Parameters.Add(upsertParam3);
                var upsertParam4 = upsertCommand.CreateParameter();
                upsertParam4.ParameterName = "@recipient";
                upsertCommand.Parameters.Add(upsertParam4);
                var upsertParam5 = upsertCommand.CreateParameter();
                upsertParam5.ParameterName = "@type";
                upsertCommand.Parameters.Add(upsertParam5);
                var upsertParam6 = upsertCommand.CreateParameter();
                upsertParam6.ParameterName = "@priority";
                upsertCommand.Parameters.Add(upsertParam6);
                var upsertParam7 = upsertCommand.CreateParameter();
                upsertParam7.ParameterName = "@dependencyFileId";
                upsertCommand.Parameters.Add(upsertParam7);
                var upsertParam8 = upsertCommand.CreateParameter();
                upsertParam8.ParameterName = "@checkOutCount";
                upsertCommand.Parameters.Add(upsertParam8);
                var upsertParam9 = upsertCommand.CreateParameter();
                upsertParam9.ParameterName = "@nextRunTime";
                upsertCommand.Parameters.Add(upsertParam9);
                var upsertParam10 = upsertCommand.CreateParameter();
                upsertParam10.ParameterName = "@value";
                upsertCommand.Parameters.Add(upsertParam10);
                var upsertParam11 = upsertCommand.CreateParameter();
                upsertParam11.ParameterName = "@checkOutStamp";
                upsertCommand.Parameters.Add(upsertParam11);
                var upsertParam12 = upsertCommand.CreateParameter();
                upsertParam12.ParameterName = "@correlationId";
                upsertCommand.Parameters.Add(upsertParam12);
                var upsertParam13 = upsertCommand.CreateParameter();
                upsertParam13.ParameterName = "@created";
                upsertCommand.Parameters.Add(upsertParam13);
                var upsertParam14 = upsertCommand.CreateParameter();
                upsertParam14.ParameterName = "@modified";
                upsertCommand.Parameters.Add(upsertParam14);
                var now = UnixTimeUtc.Now();
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
                upsertParam13.Value = now.milliseconds;
                upsertParam14.Value = now.milliseconds;
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
                   return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<int> UpdateAsync(OutboxRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.driveId.AssertGuidNotEmpty("Guid parameter driveId cannot be set to Empty GUID.");
            item.fileId.AssertGuidNotEmpty("Guid parameter fileId cannot be set to Empty GUID.");
            item.dependencyFileId.AssertGuidNotEmpty("Guid parameter dependencyFileId cannot be set to Empty GUID.");
            item.checkOutStamp.AssertGuidNotEmpty("Guid parameter checkOutStamp cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                updateCommand.CommandText = "UPDATE outbox " +
                                             "SET type = @type,priority = @priority,dependencyFileId = @dependencyFileId,checkOutCount = @checkOutCount,nextRunTime = @nextRunTime,value = @value,checkOutStamp = @checkOutStamp,correlationId = @correlationId,modified = @modified "+
                                             "WHERE (identityId = @identityId AND driveId = @driveId AND fileId = @fileId AND recipient = @recipient)";
                var updateParam1 = updateCommand.CreateParameter();
                updateParam1.ParameterName = "@identityId";
                updateCommand.Parameters.Add(updateParam1);
                var updateParam2 = updateCommand.CreateParameter();
                updateParam2.ParameterName = "@driveId";
                updateCommand.Parameters.Add(updateParam2);
                var updateParam3 = updateCommand.CreateParameter();
                updateParam3.ParameterName = "@fileId";
                updateCommand.Parameters.Add(updateParam3);
                var updateParam4 = updateCommand.CreateParameter();
                updateParam4.ParameterName = "@recipient";
                updateCommand.Parameters.Add(updateParam4);
                var updateParam5 = updateCommand.CreateParameter();
                updateParam5.ParameterName = "@type";
                updateCommand.Parameters.Add(updateParam5);
                var updateParam6 = updateCommand.CreateParameter();
                updateParam6.ParameterName = "@priority";
                updateCommand.Parameters.Add(updateParam6);
                var updateParam7 = updateCommand.CreateParameter();
                updateParam7.ParameterName = "@dependencyFileId";
                updateCommand.Parameters.Add(updateParam7);
                var updateParam8 = updateCommand.CreateParameter();
                updateParam8.ParameterName = "@checkOutCount";
                updateCommand.Parameters.Add(updateParam8);
                var updateParam9 = updateCommand.CreateParameter();
                updateParam9.ParameterName = "@nextRunTime";
                updateCommand.Parameters.Add(updateParam9);
                var updateParam10 = updateCommand.CreateParameter();
                updateParam10.ParameterName = "@value";
                updateCommand.Parameters.Add(updateParam10);
                var updateParam11 = updateCommand.CreateParameter();
                updateParam11.ParameterName = "@checkOutStamp";
                updateCommand.Parameters.Add(updateParam11);
                var updateParam12 = updateCommand.CreateParameter();
                updateParam12.ParameterName = "@correlationId";
                updateCommand.Parameters.Add(updateParam12);
                var updateParam13 = updateCommand.CreateParameter();
                updateParam13.ParameterName = "@created";
                updateCommand.Parameters.Add(updateParam13);
                var updateParam14 = updateCommand.CreateParameter();
                updateParam14.ParameterName = "@modified";
                updateCommand.Parameters.Add(updateParam14);
                var now = UnixTimeUtc.Now();
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
                updateParam13.Value = now.milliseconds;
                updateParam14.Value = now.milliseconds;
                var count = await updateCommand.ExecuteNonQueryAsync();
                if (count > 0)
                {
                     item.modified = now;
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
                getCountCommand.CommandText = "SELECT COUNT(*) FROM outbox;";
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
            item.recipientNoLengthCheck = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[4];
            item.type = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[5];
            item.priority = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[6];
            item.dependencyFileId = (rdr[7] == DBNull.Value) ? null : new Guid((byte[])rdr[7]);
            item.checkOutCount = (rdr[8] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[8];
            item.nextRunTime = (rdr[9] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[9]);
            item.valueNoLengthCheck = (rdr[10] == DBNull.Value) ? null : (byte[])(rdr[10]);
            if (item.value?.Length < 0)
                throw new Exception("Too little data in value...");
            item.checkOutStamp = (rdr[11] == DBNull.Value) ? null : new Guid((byte[])rdr[11]);
            item.correlationIdNoLengthCheck = (rdr[12] == DBNull.Value) ? null : (string)rdr[12];
            item.created = (rdr[13] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[13]);
            item.modified = (rdr[14] == DBNull.Value) ? null : new UnixTimeUtc((long)rdr[14]);
            return item;
       }

        protected virtual async Task<int> DeleteAsync(Guid identityId,Guid driveId,Guid fileId,string recipient)
        {
            if (recipient == null) throw new Exception("Cannot be null");
            if (recipient?.Length < 0) throw new Exception("Too short");
            if (recipient?.Length > 65535) throw new Exception("Too long");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM outbox " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId AND recipient = @recipient";
                var delete0Param1 = delete0Command.CreateParameter();
                delete0Param1.ParameterName = "@identityId";
                delete0Command.Parameters.Add(delete0Param1);
                var delete0Param2 = delete0Command.CreateParameter();
                delete0Param2.ParameterName = "@driveId";
                delete0Command.Parameters.Add(delete0Param2);
                var delete0Param3 = delete0Command.CreateParameter();
                delete0Param3.ParameterName = "@fileId";
                delete0Command.Parameters.Add(delete0Param3);
                var delete0Param4 = delete0Command.CreateParameter();
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

        protected OutboxRecord ReadRecordFromReader0(DbDataReader rdr, Guid identityId,Guid driveId,Guid fileId)
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
            item.recipientNoLengthCheck = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[1];
            item.type = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[2];
            item.priority = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[3];
            item.dependencyFileId = (rdr[4] == DBNull.Value) ? null : new Guid((byte[])rdr[4]);
            item.checkOutCount = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[5];
            item.nextRunTime = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[6]);
            item.valueNoLengthCheck = (rdr[7] == DBNull.Value) ? null : (byte[])(rdr[7]);
            if (item.value?.Length < 0)
                throw new Exception("Too little data in value...");
            item.checkOutStamp = (rdr[8] == DBNull.Value) ? null : new Guid((byte[])rdr[8]);
            item.correlationIdNoLengthCheck = (rdr[9] == DBNull.Value) ? null : (string)rdr[9];
            item.created = (rdr[10] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[10]);
            item.modified = (rdr[11] == DBNull.Value) ? null : new UnixTimeUtc((long)rdr[11]);
            return item;
       }

        protected virtual async Task<List<OutboxRecord>> GetAsync(Guid identityId,Guid driveId,Guid fileId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT rowId,recipient,type,priority,dependencyFileId,checkOutCount,nextRunTime,value,checkOutStamp,correlationId,created,modified FROM outbox " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId;";
                var get0Param1 = get0Command.CreateParameter();
                get0Param1.ParameterName = "@identityId";
                get0Command.Parameters.Add(get0Param1);
                var get0Param2 = get0Command.CreateParameter();
                get0Param2.ParameterName = "@driveId";
                get0Command.Parameters.Add(get0Param2);
                var get0Param3 = get0Command.CreateParameter();
                get0Param3.ParameterName = "@fileId";
                get0Command.Parameters.Add(get0Param3);

                get0Param1.Value = identityId.ToByteArray();
                get0Param2.Value = driveId.ToByteArray();
                get0Param3.Value = fileId.ToByteArray();
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return new List<OutboxRecord>();
                        }
                        var result = new List<OutboxRecord>();
                        while (true)
                        {
                            result.Add(ReadRecordFromReader0(rdr, identityId,driveId,fileId));
                            if (!await rdr.ReadAsync())
                                break;
                        }
                        return result;
                    } // using
                } //
            } // using
        }

        protected OutboxRecord ReadRecordFromReader1(DbDataReader rdr, Guid identityId,Guid driveId,Guid fileId,string recipient)
        {
            if (recipient == null) throw new Exception("Cannot be null");
            if (recipient?.Length < 0) throw new Exception("Too short");
            if (recipient?.Length > 65535) throw new Exception("Too long");
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
            item.valueNoLengthCheck = (rdr[6] == DBNull.Value) ? null : (byte[])(rdr[6]);
            if (item.value?.Length < 0)
                throw new Exception("Too little data in value...");
            item.checkOutStamp = (rdr[7] == DBNull.Value) ? null : new Guid((byte[])rdr[7]);
            item.correlationIdNoLengthCheck = (rdr[8] == DBNull.Value) ? null : (string)rdr[8];
            item.created = (rdr[9] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[9]);
            item.modified = (rdr[10] == DBNull.Value) ? null : new UnixTimeUtc((long)rdr[10]);
            return item;
       }

        protected virtual async Task<OutboxRecord> GetAsync(Guid identityId,Guid driveId,Guid fileId,string recipient)
        {
            if (recipient == null) throw new Exception("Cannot be null");
            if (recipient?.Length < 0) throw new Exception("Too short");
            if (recipient?.Length > 65535) throw new Exception("Too long");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get1Command = cn.CreateCommand();
            {
                get1Command.CommandText = "SELECT rowId,type,priority,dependencyFileId,checkOutCount,nextRunTime,value,checkOutStamp,correlationId,created,modified FROM outbox " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId AND recipient = @recipient LIMIT 1;";
                var get1Param1 = get1Command.CreateParameter();
                get1Param1.ParameterName = "@identityId";
                get1Command.Parameters.Add(get1Param1);
                var get1Param2 = get1Command.CreateParameter();
                get1Param2.ParameterName = "@driveId";
                get1Command.Parameters.Add(get1Param2);
                var get1Param3 = get1Command.CreateParameter();
                get1Param3.ParameterName = "@fileId";
                get1Command.Parameters.Add(get1Param3);
                var get1Param4 = get1Command.CreateParameter();
                get1Param4.ParameterName = "@recipient";
                get1Command.Parameters.Add(get1Param4);

                get1Param1.Value = identityId.ToByteArray();
                get1Param2.Value = driveId.ToByteArray();
                get1Param3.Value = fileId.ToByteArray();
                get1Param4.Value = recipient;
                {
                    using (var rdr = await get1Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader1(rdr, identityId,driveId,fileId,recipient);
                        return r;
                    } // using
                } //
            } // using
        }

    }
}
