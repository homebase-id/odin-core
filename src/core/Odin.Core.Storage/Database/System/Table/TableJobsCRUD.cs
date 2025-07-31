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

namespace Odin.Core.Storage.Database.System.Table
{
    public record JobsRecord
    {
        public Int64 rowId { get; set; }
        public Guid id { get; set; }
        public string name { get; set; }
        public Int32 state { get; set; }
        public Int32 priority { get; set; }
        public UnixTimeUtc nextRun { get; set; }
        public UnixTimeUtc? lastRun { get; set; }
        public Int32 runCount { get; set; }
        public Int32 maxAttempts { get; set; }
        public Int64 retryDelay { get; set; }
        public Int64 onSuccessDeleteAfter { get; set; }
        public Int64 onFailureDeleteAfter { get; set; }
        public UnixTimeUtc? expiresAt { get; set; }
        public string correlationId { get; set; }
        public string jobType { get; set; }
        public string jobData { get; set; }
        public string jobHash { get; set; }
        public string lastError { get; set; }
        public UnixTimeUtc created { get; set; }
        public UnixTimeUtc modified { get; set; }
        public void Validate()
        {
            id.AssertGuidNotEmpty("Guid parameter id cannot be set to Empty GUID.");
            if (name == null) throw new OdinDatabaseValidationException("Cannot be null name");
            if (name?.Length < 0) throw new OdinDatabaseValidationException($"Too short name, was {name.Length} (min 0)");
            if (name?.Length > 64) throw new OdinDatabaseValidationException($"Too long name, was {name.Length} (max 64)");
            if (correlationId == null) throw new OdinDatabaseValidationException("Cannot be null correlationId");
            if (correlationId?.Length < 0) throw new OdinDatabaseValidationException($"Too short correlationId, was {correlationId.Length} (min 0)");
            if (correlationId?.Length > 64) throw new OdinDatabaseValidationException($"Too long correlationId, was {correlationId.Length} (max 64)");
            if (jobType == null) throw new OdinDatabaseValidationException("Cannot be null jobType");
            if (jobType?.Length < 0) throw new OdinDatabaseValidationException($"Too short jobType, was {jobType.Length} (min 0)");
            if (jobType?.Length > 65535) throw new OdinDatabaseValidationException($"Too long jobType, was {jobType.Length} (max 65535)");
            if (jobData?.Length < 0) throw new OdinDatabaseValidationException($"Too short jobData, was {jobData.Length} (min 0)");
            if (jobData?.Length > 65535) throw new OdinDatabaseValidationException($"Too long jobData, was {jobData.Length} (max 65535)");
            if (jobHash?.Length < 0) throw new OdinDatabaseValidationException($"Too short jobHash, was {jobHash.Length} (min 0)");
            if (jobHash?.Length > 65535) throw new OdinDatabaseValidationException($"Too long jobHash, was {jobHash.Length} (max 65535)");
            if (lastError?.Length < 0) throw new OdinDatabaseValidationException($"Too short lastError, was {lastError.Length} (min 0)");
            if (lastError?.Length > 65535) throw new OdinDatabaseValidationException($"Too long lastError, was {lastError.Length} (max 65535)");
        }
    } // End of record JobsRecord

    public abstract class TableJobsCRUD : TableBase
    {
        private ScopedSystemConnectionFactory _scopedConnectionFactory { get; init; }
        public override string TableName { get; init; } = "Jobs";

        public TableJobsCRUD(CacheHelper cache, ScopedSystemConnectionFactory scopedConnectionFactory)
        {
            _scopedConnectionFactory = scopedConnectionFactory;
        }


        public override async Task EnsureTableExistsAsync(bool dropExisting = false)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            if (dropExisting)
                await SqlHelper.DeleteTableAsync(cn, "Jobs");
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowid BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE Jobs IS '{ \"Version\": 0 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS Jobs( -- { \"Version\": 0 }\n"
                   +rowid
                   +"id BYTEA NOT NULL UNIQUE, "
                   +"name TEXT NOT NULL, "
                   +"state BIGINT NOT NULL, "
                   +"priority BIGINT NOT NULL, "
                   +"nextRun BIGINT NOT NULL, "
                   +"lastRun BIGINT , "
                   +"runCount BIGINT NOT NULL, "
                   +"maxAttempts BIGINT NOT NULL, "
                   +"retryDelay BIGINT NOT NULL, "
                   +"onSuccessDeleteAfter BIGINT NOT NULL, "
                   +"onFailureDeleteAfter BIGINT NOT NULL, "
                   +"expiresAt BIGINT , "
                   +"correlationId TEXT NOT NULL, "
                   +"jobType TEXT NOT NULL, "
                   +"jobData TEXT , "
                   +"jobHash TEXT  UNIQUE, "
                   +"lastError TEXT , "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +$"){wori};"
                   +"CREATE INDEX IF NOT EXISTS Idx0Jobs ON Jobs(state);"
                   +"CREATE INDEX IF NOT EXISTS Idx1Jobs ON Jobs(expiresAt);"
                   +"CREATE INDEX IF NOT EXISTS Idx2Jobs ON Jobs(nextRun,priority);"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "Jobs", createSql, commentSql);
        }

        public virtual async Task<int> InsertAsync(JobsRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr = insertCommand.SqlNow();
                insertCommand.CommandText = "INSERT INTO Jobs (id,name,state,priority,nextRun,lastRun,runCount,maxAttempts,retryDelay,onSuccessDeleteAfter,onFailureDeleteAfter,expiresAt,correlationId,jobType,jobData,jobHash,lastError,created,modified) " +
                                           $"VALUES (@id,@name,@state,@priority,@nextRun,@lastRun,@runCount,@maxAttempts,@retryDelay,@onSuccessDeleteAfter,@onFailureDeleteAfter,@expiresAt,@correlationId,@jobType,@jobData,@jobHash,@lastError,{sqlNowStr},{sqlNowStr})"+
                                            "RETURNING created,modified,rowId;";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.DbType = DbType.Binary;
                insertParam1.ParameterName = "@id";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.DbType = DbType.String;
                insertParam2.ParameterName = "@name";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.DbType = DbType.Int32;
                insertParam3.ParameterName = "@state";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.DbType = DbType.Int32;
                insertParam4.ParameterName = "@priority";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.DbType = DbType.Int64;
                insertParam5.ParameterName = "@nextRun";
                insertCommand.Parameters.Add(insertParam5);
                var insertParam6 = insertCommand.CreateParameter();
                insertParam6.DbType = DbType.Int64;
                insertParam6.ParameterName = "@lastRun";
                insertCommand.Parameters.Add(insertParam6);
                var insertParam7 = insertCommand.CreateParameter();
                insertParam7.DbType = DbType.Int32;
                insertParam7.ParameterName = "@runCount";
                insertCommand.Parameters.Add(insertParam7);
                var insertParam8 = insertCommand.CreateParameter();
                insertParam8.DbType = DbType.Int32;
                insertParam8.ParameterName = "@maxAttempts";
                insertCommand.Parameters.Add(insertParam8);
                var insertParam9 = insertCommand.CreateParameter();
                insertParam9.DbType = DbType.Int64;
                insertParam9.ParameterName = "@retryDelay";
                insertCommand.Parameters.Add(insertParam9);
                var insertParam10 = insertCommand.CreateParameter();
                insertParam10.DbType = DbType.Int64;
                insertParam10.ParameterName = "@onSuccessDeleteAfter";
                insertCommand.Parameters.Add(insertParam10);
                var insertParam11 = insertCommand.CreateParameter();
                insertParam11.DbType = DbType.Int64;
                insertParam11.ParameterName = "@onFailureDeleteAfter";
                insertCommand.Parameters.Add(insertParam11);
                var insertParam12 = insertCommand.CreateParameter();
                insertParam12.DbType = DbType.Int64;
                insertParam12.ParameterName = "@expiresAt";
                insertCommand.Parameters.Add(insertParam12);
                var insertParam13 = insertCommand.CreateParameter();
                insertParam13.DbType = DbType.String;
                insertParam13.ParameterName = "@correlationId";
                insertCommand.Parameters.Add(insertParam13);
                var insertParam14 = insertCommand.CreateParameter();
                insertParam14.DbType = DbType.String;
                insertParam14.ParameterName = "@jobType";
                insertCommand.Parameters.Add(insertParam14);
                var insertParam15 = insertCommand.CreateParameter();
                insertParam15.DbType = DbType.String;
                insertParam15.ParameterName = "@jobData";
                insertCommand.Parameters.Add(insertParam15);
                var insertParam16 = insertCommand.CreateParameter();
                insertParam16.DbType = DbType.String;
                insertParam16.ParameterName = "@jobHash";
                insertCommand.Parameters.Add(insertParam16);
                var insertParam17 = insertCommand.CreateParameter();
                insertParam17.DbType = DbType.String;
                insertParam17.ParameterName = "@lastError";
                insertCommand.Parameters.Add(insertParam17);
                insertParam1.Value = item.id.ToByteArray();
                insertParam2.Value = item.name;
                insertParam3.Value = item.state;
                insertParam4.Value = item.priority;
                insertParam5.Value = item.nextRun.milliseconds;
                insertParam6.Value = item.lastRun == null ? (object)DBNull.Value : item.lastRun?.milliseconds;
                insertParam7.Value = item.runCount;
                insertParam8.Value = item.maxAttempts;
                insertParam9.Value = item.retryDelay;
                insertParam10.Value = item.onSuccessDeleteAfter;
                insertParam11.Value = item.onFailureDeleteAfter;
                insertParam12.Value = item.expiresAt == null ? (object)DBNull.Value : item.expiresAt?.milliseconds;
                insertParam13.Value = item.correlationId;
                insertParam14.Value = item.jobType;
                insertParam15.Value = item.jobData ?? (object)DBNull.Value;
                insertParam16.Value = item.jobHash ?? (object)DBNull.Value;
                insertParam17.Value = item.lastError ?? (object)DBNull.Value;
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

        public virtual async Task<bool> TryInsertAsync(JobsRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr = insertCommand.SqlNow();
                insertCommand.CommandText = "INSERT INTO Jobs (id,name,state,priority,nextRun,lastRun,runCount,maxAttempts,retryDelay,onSuccessDeleteAfter,onFailureDeleteAfter,expiresAt,correlationId,jobType,jobData,jobHash,lastError,created,modified) " +
                                            $"VALUES (@id,@name,@state,@priority,@nextRun,@lastRun,@runCount,@maxAttempts,@retryDelay,@onSuccessDeleteAfter,@onFailureDeleteAfter,@expiresAt,@correlationId,@jobType,@jobData,@jobHash,@lastError,{sqlNowStr},{sqlNowStr}) " +
                                            "ON CONFLICT DO NOTHING "+
                                            "RETURNING created,modified,rowId;";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.DbType = DbType.Binary;
                insertParam1.ParameterName = "@id";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.DbType = DbType.String;
                insertParam2.ParameterName = "@name";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.DbType = DbType.Int32;
                insertParam3.ParameterName = "@state";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.DbType = DbType.Int32;
                insertParam4.ParameterName = "@priority";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.DbType = DbType.Int64;
                insertParam5.ParameterName = "@nextRun";
                insertCommand.Parameters.Add(insertParam5);
                var insertParam6 = insertCommand.CreateParameter();
                insertParam6.DbType = DbType.Int64;
                insertParam6.ParameterName = "@lastRun";
                insertCommand.Parameters.Add(insertParam6);
                var insertParam7 = insertCommand.CreateParameter();
                insertParam7.DbType = DbType.Int32;
                insertParam7.ParameterName = "@runCount";
                insertCommand.Parameters.Add(insertParam7);
                var insertParam8 = insertCommand.CreateParameter();
                insertParam8.DbType = DbType.Int32;
                insertParam8.ParameterName = "@maxAttempts";
                insertCommand.Parameters.Add(insertParam8);
                var insertParam9 = insertCommand.CreateParameter();
                insertParam9.DbType = DbType.Int64;
                insertParam9.ParameterName = "@retryDelay";
                insertCommand.Parameters.Add(insertParam9);
                var insertParam10 = insertCommand.CreateParameter();
                insertParam10.DbType = DbType.Int64;
                insertParam10.ParameterName = "@onSuccessDeleteAfter";
                insertCommand.Parameters.Add(insertParam10);
                var insertParam11 = insertCommand.CreateParameter();
                insertParam11.DbType = DbType.Int64;
                insertParam11.ParameterName = "@onFailureDeleteAfter";
                insertCommand.Parameters.Add(insertParam11);
                var insertParam12 = insertCommand.CreateParameter();
                insertParam12.DbType = DbType.Int64;
                insertParam12.ParameterName = "@expiresAt";
                insertCommand.Parameters.Add(insertParam12);
                var insertParam13 = insertCommand.CreateParameter();
                insertParam13.DbType = DbType.String;
                insertParam13.ParameterName = "@correlationId";
                insertCommand.Parameters.Add(insertParam13);
                var insertParam14 = insertCommand.CreateParameter();
                insertParam14.DbType = DbType.String;
                insertParam14.ParameterName = "@jobType";
                insertCommand.Parameters.Add(insertParam14);
                var insertParam15 = insertCommand.CreateParameter();
                insertParam15.DbType = DbType.String;
                insertParam15.ParameterName = "@jobData";
                insertCommand.Parameters.Add(insertParam15);
                var insertParam16 = insertCommand.CreateParameter();
                insertParam16.DbType = DbType.String;
                insertParam16.ParameterName = "@jobHash";
                insertCommand.Parameters.Add(insertParam16);
                var insertParam17 = insertCommand.CreateParameter();
                insertParam17.DbType = DbType.String;
                insertParam17.ParameterName = "@lastError";
                insertCommand.Parameters.Add(insertParam17);
                insertParam1.Value = item.id.ToByteArray();
                insertParam2.Value = item.name;
                insertParam3.Value = item.state;
                insertParam4.Value = item.priority;
                insertParam5.Value = item.nextRun.milliseconds;
                insertParam6.Value = item.lastRun == null ? (object)DBNull.Value : item.lastRun?.milliseconds;
                insertParam7.Value = item.runCount;
                insertParam8.Value = item.maxAttempts;
                insertParam9.Value = item.retryDelay;
                insertParam10.Value = item.onSuccessDeleteAfter;
                insertParam11.Value = item.onFailureDeleteAfter;
                insertParam12.Value = item.expiresAt == null ? (object)DBNull.Value : item.expiresAt?.milliseconds;
                insertParam13.Value = item.correlationId;
                insertParam14.Value = item.jobType;
                insertParam15.Value = item.jobData ?? (object)DBNull.Value;
                insertParam16.Value = item.jobHash ?? (object)DBNull.Value;
                insertParam17.Value = item.lastError ?? (object)DBNull.Value;
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

        public virtual async Task<int> UpsertAsync(JobsRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                string sqlNowStr = upsertCommand.SqlNow();
                upsertCommand.CommandText = "INSERT INTO Jobs (id,name,state,priority,nextRun,lastRun,runCount,maxAttempts,retryDelay,onSuccessDeleteAfter,onFailureDeleteAfter,expiresAt,correlationId,jobType,jobData,jobHash,lastError,created,modified) " +
                                            $"VALUES (@id,@name,@state,@priority,@nextRun,@lastRun,@runCount,@maxAttempts,@retryDelay,@onSuccessDeleteAfter,@onFailureDeleteAfter,@expiresAt,@correlationId,@jobType,@jobData,@jobHash,@lastError,{sqlNowStr},{sqlNowStr})"+
                                            "ON CONFLICT (id) DO UPDATE "+
                                            $"SET name = @name,state = @state,priority = @priority,nextRun = @nextRun,lastRun = @lastRun,runCount = @runCount,maxAttempts = @maxAttempts,retryDelay = @retryDelay,onSuccessDeleteAfter = @onSuccessDeleteAfter,onFailureDeleteAfter = @onFailureDeleteAfter,expiresAt = @expiresAt,correlationId = @correlationId,jobType = @jobType,jobData = @jobData,jobHash = @jobHash,lastError = @lastError,modified = {upsertCommand.SqlMax()}(Jobs.modified+1,{sqlNowStr}) "+
                                            "RETURNING created,modified,rowId;";
                var upsertParam1 = upsertCommand.CreateParameter();
                upsertParam1.DbType = DbType.Binary;
                upsertParam1.ParameterName = "@id";
                upsertCommand.Parameters.Add(upsertParam1);
                var upsertParam2 = upsertCommand.CreateParameter();
                upsertParam2.DbType = DbType.String;
                upsertParam2.ParameterName = "@name";
                upsertCommand.Parameters.Add(upsertParam2);
                var upsertParam3 = upsertCommand.CreateParameter();
                upsertParam3.DbType = DbType.Int32;
                upsertParam3.ParameterName = "@state";
                upsertCommand.Parameters.Add(upsertParam3);
                var upsertParam4 = upsertCommand.CreateParameter();
                upsertParam4.DbType = DbType.Int32;
                upsertParam4.ParameterName = "@priority";
                upsertCommand.Parameters.Add(upsertParam4);
                var upsertParam5 = upsertCommand.CreateParameter();
                upsertParam5.DbType = DbType.Int64;
                upsertParam5.ParameterName = "@nextRun";
                upsertCommand.Parameters.Add(upsertParam5);
                var upsertParam6 = upsertCommand.CreateParameter();
                upsertParam6.DbType = DbType.Int64;
                upsertParam6.ParameterName = "@lastRun";
                upsertCommand.Parameters.Add(upsertParam6);
                var upsertParam7 = upsertCommand.CreateParameter();
                upsertParam7.DbType = DbType.Int32;
                upsertParam7.ParameterName = "@runCount";
                upsertCommand.Parameters.Add(upsertParam7);
                var upsertParam8 = upsertCommand.CreateParameter();
                upsertParam8.DbType = DbType.Int32;
                upsertParam8.ParameterName = "@maxAttempts";
                upsertCommand.Parameters.Add(upsertParam8);
                var upsertParam9 = upsertCommand.CreateParameter();
                upsertParam9.DbType = DbType.Int64;
                upsertParam9.ParameterName = "@retryDelay";
                upsertCommand.Parameters.Add(upsertParam9);
                var upsertParam10 = upsertCommand.CreateParameter();
                upsertParam10.DbType = DbType.Int64;
                upsertParam10.ParameterName = "@onSuccessDeleteAfter";
                upsertCommand.Parameters.Add(upsertParam10);
                var upsertParam11 = upsertCommand.CreateParameter();
                upsertParam11.DbType = DbType.Int64;
                upsertParam11.ParameterName = "@onFailureDeleteAfter";
                upsertCommand.Parameters.Add(upsertParam11);
                var upsertParam12 = upsertCommand.CreateParameter();
                upsertParam12.DbType = DbType.Int64;
                upsertParam12.ParameterName = "@expiresAt";
                upsertCommand.Parameters.Add(upsertParam12);
                var upsertParam13 = upsertCommand.CreateParameter();
                upsertParam13.DbType = DbType.String;
                upsertParam13.ParameterName = "@correlationId";
                upsertCommand.Parameters.Add(upsertParam13);
                var upsertParam14 = upsertCommand.CreateParameter();
                upsertParam14.DbType = DbType.String;
                upsertParam14.ParameterName = "@jobType";
                upsertCommand.Parameters.Add(upsertParam14);
                var upsertParam15 = upsertCommand.CreateParameter();
                upsertParam15.DbType = DbType.String;
                upsertParam15.ParameterName = "@jobData";
                upsertCommand.Parameters.Add(upsertParam15);
                var upsertParam16 = upsertCommand.CreateParameter();
                upsertParam16.DbType = DbType.String;
                upsertParam16.ParameterName = "@jobHash";
                upsertCommand.Parameters.Add(upsertParam16);
                var upsertParam17 = upsertCommand.CreateParameter();
                upsertParam17.DbType = DbType.String;
                upsertParam17.ParameterName = "@lastError";
                upsertCommand.Parameters.Add(upsertParam17);
                upsertParam1.Value = item.id.ToByteArray();
                upsertParam2.Value = item.name;
                upsertParam3.Value = item.state;
                upsertParam4.Value = item.priority;
                upsertParam5.Value = item.nextRun.milliseconds;
                upsertParam6.Value = item.lastRun == null ? (object)DBNull.Value : item.lastRun?.milliseconds;
                upsertParam7.Value = item.runCount;
                upsertParam8.Value = item.maxAttempts;
                upsertParam9.Value = item.retryDelay;
                upsertParam10.Value = item.onSuccessDeleteAfter;
                upsertParam11.Value = item.onFailureDeleteAfter;
                upsertParam12.Value = item.expiresAt == null ? (object)DBNull.Value : item.expiresAt?.milliseconds;
                upsertParam13.Value = item.correlationId;
                upsertParam14.Value = item.jobType;
                upsertParam15.Value = item.jobData ?? (object)DBNull.Value;
                upsertParam16.Value = item.jobHash ?? (object)DBNull.Value;
                upsertParam17.Value = item.lastError ?? (object)DBNull.Value;
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

        public virtual async Task<int> UpdateAsync(JobsRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                string sqlNowStr = updateCommand.SqlNow();
                updateCommand.CommandText = "UPDATE Jobs " +
                                            $"SET name = @name,state = @state,priority = @priority,nextRun = @nextRun,lastRun = @lastRun,runCount = @runCount,maxAttempts = @maxAttempts,retryDelay = @retryDelay,onSuccessDeleteAfter = @onSuccessDeleteAfter,onFailureDeleteAfter = @onFailureDeleteAfter,expiresAt = @expiresAt,correlationId = @correlationId,jobType = @jobType,jobData = @jobData,jobHash = @jobHash,lastError = @lastError,modified = {updateCommand.SqlMax()}(Jobs.modified+1,{sqlNowStr}) "+
                                            "WHERE (id = @id) "+
                                            "RETURNING created,modified,rowId;";
                var updateParam1 = updateCommand.CreateParameter();
                updateParam1.DbType = DbType.Binary;
                updateParam1.ParameterName = "@id";
                updateCommand.Parameters.Add(updateParam1);
                var updateParam2 = updateCommand.CreateParameter();
                updateParam2.DbType = DbType.String;
                updateParam2.ParameterName = "@name";
                updateCommand.Parameters.Add(updateParam2);
                var updateParam3 = updateCommand.CreateParameter();
                updateParam3.DbType = DbType.Int32;
                updateParam3.ParameterName = "@state";
                updateCommand.Parameters.Add(updateParam3);
                var updateParam4 = updateCommand.CreateParameter();
                updateParam4.DbType = DbType.Int32;
                updateParam4.ParameterName = "@priority";
                updateCommand.Parameters.Add(updateParam4);
                var updateParam5 = updateCommand.CreateParameter();
                updateParam5.DbType = DbType.Int64;
                updateParam5.ParameterName = "@nextRun";
                updateCommand.Parameters.Add(updateParam5);
                var updateParam6 = updateCommand.CreateParameter();
                updateParam6.DbType = DbType.Int64;
                updateParam6.ParameterName = "@lastRun";
                updateCommand.Parameters.Add(updateParam6);
                var updateParam7 = updateCommand.CreateParameter();
                updateParam7.DbType = DbType.Int32;
                updateParam7.ParameterName = "@runCount";
                updateCommand.Parameters.Add(updateParam7);
                var updateParam8 = updateCommand.CreateParameter();
                updateParam8.DbType = DbType.Int32;
                updateParam8.ParameterName = "@maxAttempts";
                updateCommand.Parameters.Add(updateParam8);
                var updateParam9 = updateCommand.CreateParameter();
                updateParam9.DbType = DbType.Int64;
                updateParam9.ParameterName = "@retryDelay";
                updateCommand.Parameters.Add(updateParam9);
                var updateParam10 = updateCommand.CreateParameter();
                updateParam10.DbType = DbType.Int64;
                updateParam10.ParameterName = "@onSuccessDeleteAfter";
                updateCommand.Parameters.Add(updateParam10);
                var updateParam11 = updateCommand.CreateParameter();
                updateParam11.DbType = DbType.Int64;
                updateParam11.ParameterName = "@onFailureDeleteAfter";
                updateCommand.Parameters.Add(updateParam11);
                var updateParam12 = updateCommand.CreateParameter();
                updateParam12.DbType = DbType.Int64;
                updateParam12.ParameterName = "@expiresAt";
                updateCommand.Parameters.Add(updateParam12);
                var updateParam13 = updateCommand.CreateParameter();
                updateParam13.DbType = DbType.String;
                updateParam13.ParameterName = "@correlationId";
                updateCommand.Parameters.Add(updateParam13);
                var updateParam14 = updateCommand.CreateParameter();
                updateParam14.DbType = DbType.String;
                updateParam14.ParameterName = "@jobType";
                updateCommand.Parameters.Add(updateParam14);
                var updateParam15 = updateCommand.CreateParameter();
                updateParam15.DbType = DbType.String;
                updateParam15.ParameterName = "@jobData";
                updateCommand.Parameters.Add(updateParam15);
                var updateParam16 = updateCommand.CreateParameter();
                updateParam16.DbType = DbType.String;
                updateParam16.ParameterName = "@jobHash";
                updateCommand.Parameters.Add(updateParam16);
                var updateParam17 = updateCommand.CreateParameter();
                updateParam17.DbType = DbType.String;
                updateParam17.ParameterName = "@lastError";
                updateCommand.Parameters.Add(updateParam17);
                updateParam1.Value = item.id.ToByteArray();
                updateParam2.Value = item.name;
                updateParam3.Value = item.state;
                updateParam4.Value = item.priority;
                updateParam5.Value = item.nextRun.milliseconds;
                updateParam6.Value = item.lastRun == null ? (object)DBNull.Value : item.lastRun?.milliseconds;
                updateParam7.Value = item.runCount;
                updateParam8.Value = item.maxAttempts;
                updateParam9.Value = item.retryDelay;
                updateParam10.Value = item.onSuccessDeleteAfter;
                updateParam11.Value = item.onFailureDeleteAfter;
                updateParam12.Value = item.expiresAt == null ? (object)DBNull.Value : item.expiresAt?.milliseconds;
                updateParam13.Value = item.correlationId;
                updateParam14.Value = item.jobType;
                updateParam15.Value = item.jobData ?? (object)DBNull.Value;
                updateParam16.Value = item.jobHash ?? (object)DBNull.Value;
                updateParam17.Value = item.lastError ?? (object)DBNull.Value;
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

        public new async Task<int> GetCountAsync()
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getCountCommand = cn.CreateCommand();
            {
                 // TODO: this is SQLite specific
                getCountCommand.CommandText = "SELECT COUNT(*) FROM Jobs;";
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
            sl.Add("id");
            sl.Add("name");
            sl.Add("state");
            sl.Add("priority");
            sl.Add("nextRun");
            sl.Add("lastRun");
            sl.Add("runCount");
            sl.Add("maxAttempts");
            sl.Add("retryDelay");
            sl.Add("onSuccessDeleteAfter");
            sl.Add("onFailureDeleteAfter");
            sl.Add("expiresAt");
            sl.Add("correlationId");
            sl.Add("jobType");
            sl.Add("jobData");
            sl.Add("jobHash");
            sl.Add("lastError");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        // SELECT rowId,id,name,state,priority,nextRun,lastRun,runCount,maxAttempts,retryDelay,onSuccessDeleteAfter,onFailureDeleteAfter,expiresAt,correlationId,jobType,jobData,jobHash,lastError,created,modified
        public JobsRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<JobsRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new JobsRecord();
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.id = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.name = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[2];
            item.state = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[3];
            item.priority = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[4];
            item.nextRun = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[5]);
            item.lastRun = (rdr[6] == DBNull.Value) ? null : new UnixTimeUtc((long)rdr[6]);
            item.runCount = (rdr[7] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[7];
            item.maxAttempts = (rdr[8] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[8];
            item.retryDelay = (rdr[9] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[9];
            item.onSuccessDeleteAfter = (rdr[10] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[10];
            item.onFailureDeleteAfter = (rdr[11] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[11];
            item.expiresAt = (rdr[12] == DBNull.Value) ? null : new UnixTimeUtc((long)rdr[12]);
            item.correlationId = (rdr[13] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[13];
            item.jobType = (rdr[14] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[14];
            item.jobData = (rdr[15] == DBNull.Value) ? null : (string)rdr[15];
            item.jobHash = (rdr[16] == DBNull.Value) ? null : (string)rdr[16];
            item.lastError = (rdr[17] == DBNull.Value) ? null : (string)rdr[17];
            item.created = (rdr[18] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[18]);
            item.modified = (rdr[19] == DBNull.Value) ? item.created : new UnixTimeUtc((long)rdr[19]); // HACK
            return item;
       }

        public virtual async Task<int> DeleteAsync(Guid id)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM Jobs " +
                                             "WHERE id = @id";
                var delete0Param1 = delete0Command.CreateParameter();
                delete0Param1.DbType = DbType.Binary;
                delete0Param1.ParameterName = "@id";
                delete0Command.Parameters.Add(delete0Param1);

                delete0Param1.Value = id.ToByteArray();
                var count = await delete0Command.ExecuteNonQueryAsync();
                return count;
            }
        }

        public virtual async Task<JobsRecord> PopAsync(Guid id)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var deleteCommand = cn.CreateCommand();
            {
                deleteCommand.CommandText = "DELETE FROM Jobs " +
                                             "WHERE id = @id " + 
                                             "RETURNING rowId,name,state,priority,nextRun,lastRun,runCount,maxAttempts,retryDelay,onSuccessDeleteAfter,onFailureDeleteAfter,expiresAt,correlationId,jobType,jobData,jobHash,lastError,created,modified";
                var deleteParam1 = deleteCommand.CreateParameter();
                deleteParam1.DbType = DbType.Binary;
                deleteParam1.ParameterName = "@id";
                deleteCommand.Parameters.Add(deleteParam1);

                deleteParam1.Value = id.ToByteArray();
                using (var rdr = await deleteCommand.ExecuteReaderAsync(CommandBehavior.SingleRow))
                {
                    if (await rdr.ReadAsync())
                    {
                       return ReadRecordFromReader0(rdr,id);
                    }
                    else
                    {
                       return null;
                    }
                }
            }
        }

        public JobsRecord ReadRecordFromReader0(DbDataReader rdr,Guid id)
        {
            var result = new List<JobsRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new JobsRecord();
            item.id = id;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.name = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[1];
            item.state = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[2];
            item.priority = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[3];
            item.nextRun = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[4]);
            item.lastRun = (rdr[5] == DBNull.Value) ? null : new UnixTimeUtc((long)rdr[5]);
            item.runCount = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[6];
            item.maxAttempts = (rdr[7] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[7];
            item.retryDelay = (rdr[8] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[8];
            item.onSuccessDeleteAfter = (rdr[9] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[9];
            item.onFailureDeleteAfter = (rdr[10] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[10];
            item.expiresAt = (rdr[11] == DBNull.Value) ? null : new UnixTimeUtc((long)rdr[11]);
            item.correlationId = (rdr[12] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[12];
            item.jobType = (rdr[13] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[13];
            item.jobData = (rdr[14] == DBNull.Value) ? null : (string)rdr[14];
            item.jobHash = (rdr[15] == DBNull.Value) ? null : (string)rdr[15];
            item.lastError = (rdr[16] == DBNull.Value) ? null : (string)rdr[16];
            item.created = (rdr[17] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[17]);
            item.modified = (rdr[18] == DBNull.Value) ? item.created : new UnixTimeUtc((long)rdr[18]); // HACK
            return item;
       }

        public virtual async Task<JobsRecord> GetAsync(Guid id)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT rowId,name,state,priority,nextRun,lastRun,runCount,maxAttempts,retryDelay,onSuccessDeleteAfter,onFailureDeleteAfter,expiresAt,correlationId,jobType,jobData,jobHash,lastError,created,modified FROM Jobs " +
                                             "WHERE id = @id LIMIT 1;"+
                                             ";";
                var get0Param1 = get0Command.CreateParameter();
                get0Param1.DbType = DbType.Binary;
                get0Param1.ParameterName = "@id";
                get0Command.Parameters.Add(get0Param1);

                get0Param1.Value = id.ToByteArray();
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr,id);
                        return r;
                    } // using
                } //
            } // using
        }

    }
}
