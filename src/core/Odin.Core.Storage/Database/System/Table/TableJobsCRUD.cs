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
        public override string TableName { get; } = "Jobs";

        public TableJobsCRUD(ScopedSystemConnectionFactory scopedConnectionFactory)
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
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
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
                insertCommand.AddParameter("@id", DbType.Binary, item.id);
                insertCommand.AddParameter("@name", DbType.String, item.name);
                insertCommand.AddParameter("@state", DbType.Int32, item.state);
                insertCommand.AddParameter("@priority", DbType.Int32, item.priority);
                insertCommand.AddParameter("@nextRun", DbType.Int64, item.nextRun.milliseconds);
                insertCommand.AddParameter("@lastRun", DbType.Int64, item.lastRun?.milliseconds);
                insertCommand.AddParameter("@runCount", DbType.Int32, item.runCount);
                insertCommand.AddParameter("@maxAttempts", DbType.Int32, item.maxAttempts);
                insertCommand.AddParameter("@retryDelay", DbType.Int64, item.retryDelay);
                insertCommand.AddParameter("@onSuccessDeleteAfter", DbType.Int64, item.onSuccessDeleteAfter);
                insertCommand.AddParameter("@onFailureDeleteAfter", DbType.Int64, item.onFailureDeleteAfter);
                insertCommand.AddParameter("@expiresAt", DbType.Int64, item.expiresAt?.milliseconds);
                insertCommand.AddParameter("@correlationId", DbType.String, item.correlationId);
                insertCommand.AddParameter("@jobType", DbType.String, item.jobType);
                insertCommand.AddParameter("@jobData", DbType.String, item.jobData);
                insertCommand.AddParameter("@jobHash", DbType.String, item.jobHash);
                insertCommand.AddParameter("@lastError", DbType.String, item.lastError);
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
                insertCommand.AddParameter("@id", DbType.Binary, item.id);
                insertCommand.AddParameter("@name", DbType.String, item.name);
                insertCommand.AddParameter("@state", DbType.Int32, item.state);
                insertCommand.AddParameter("@priority", DbType.Int32, item.priority);
                insertCommand.AddParameter("@nextRun", DbType.Int64, item.nextRun.milliseconds);
                insertCommand.AddParameter("@lastRun", DbType.Int64, item.lastRun?.milliseconds);
                insertCommand.AddParameter("@runCount", DbType.Int32, item.runCount);
                insertCommand.AddParameter("@maxAttempts", DbType.Int32, item.maxAttempts);
                insertCommand.AddParameter("@retryDelay", DbType.Int64, item.retryDelay);
                insertCommand.AddParameter("@onSuccessDeleteAfter", DbType.Int64, item.onSuccessDeleteAfter);
                insertCommand.AddParameter("@onFailureDeleteAfter", DbType.Int64, item.onFailureDeleteAfter);
                insertCommand.AddParameter("@expiresAt", DbType.Int64, item.expiresAt?.milliseconds);
                insertCommand.AddParameter("@correlationId", DbType.String, item.correlationId);
                insertCommand.AddParameter("@jobType", DbType.String, item.jobType);
                insertCommand.AddParameter("@jobData", DbType.String, item.jobData);
                insertCommand.AddParameter("@jobHash", DbType.String, item.jobHash);
                insertCommand.AddParameter("@lastError", DbType.String, item.lastError);
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
                upsertCommand.AddParameter("@id", DbType.Binary, item.id);
                upsertCommand.AddParameter("@name", DbType.String, item.name);
                upsertCommand.AddParameter("@state", DbType.Int32, item.state);
                upsertCommand.AddParameter("@priority", DbType.Int32, item.priority);
                upsertCommand.AddParameter("@nextRun", DbType.Int64, item.nextRun.milliseconds);
                upsertCommand.AddParameter("@lastRun", DbType.Int64, item.lastRun?.milliseconds);
                upsertCommand.AddParameter("@runCount", DbType.Int32, item.runCount);
                upsertCommand.AddParameter("@maxAttempts", DbType.Int32, item.maxAttempts);
                upsertCommand.AddParameter("@retryDelay", DbType.Int64, item.retryDelay);
                upsertCommand.AddParameter("@onSuccessDeleteAfter", DbType.Int64, item.onSuccessDeleteAfter);
                upsertCommand.AddParameter("@onFailureDeleteAfter", DbType.Int64, item.onFailureDeleteAfter);
                upsertCommand.AddParameter("@expiresAt", DbType.Int64, item.expiresAt?.milliseconds);
                upsertCommand.AddParameter("@correlationId", DbType.String, item.correlationId);
                upsertCommand.AddParameter("@jobType", DbType.String, item.jobType);
                upsertCommand.AddParameter("@jobData", DbType.String, item.jobData);
                upsertCommand.AddParameter("@jobHash", DbType.String, item.jobHash);
                upsertCommand.AddParameter("@lastError", DbType.String, item.lastError);
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
                updateCommand.AddParameter("@id", DbType.Binary, item.id);
                updateCommand.AddParameter("@name", DbType.String, item.name);
                updateCommand.AddParameter("@state", DbType.Int32, item.state);
                updateCommand.AddParameter("@priority", DbType.Int32, item.priority);
                updateCommand.AddParameter("@nextRun", DbType.Int64, item.nextRun.milliseconds);
                updateCommand.AddParameter("@lastRun", DbType.Int64, item.lastRun?.milliseconds);
                updateCommand.AddParameter("@runCount", DbType.Int32, item.runCount);
                updateCommand.AddParameter("@maxAttempts", DbType.Int32, item.maxAttempts);
                updateCommand.AddParameter("@retryDelay", DbType.Int64, item.retryDelay);
                updateCommand.AddParameter("@onSuccessDeleteAfter", DbType.Int64, item.onSuccessDeleteAfter);
                updateCommand.AddParameter("@onFailureDeleteAfter", DbType.Int64, item.onFailureDeleteAfter);
                updateCommand.AddParameter("@expiresAt", DbType.Int64, item.expiresAt?.milliseconds);
                updateCommand.AddParameter("@correlationId", DbType.String, item.correlationId);
                updateCommand.AddParameter("@jobType", DbType.String, item.jobType);
                updateCommand.AddParameter("@jobData", DbType.String, item.jobData);
                updateCommand.AddParameter("@jobHash", DbType.String, item.jobHash);
                updateCommand.AddParameter("@lastError", DbType.String, item.lastError);
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
            item.modified = (rdr[19] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[19]);
            return item;
       }

        public virtual async Task<int> DeleteAsync(Guid id)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM Jobs " +
                                             "WHERE id = @id";

                delete0Command.AddParameter("@id", DbType.Binary, id);
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

                deleteCommand.AddParameter("@id", DbType.Binary, id);
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
            item.modified = (rdr[18] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[18]);
            return item;
       }

        public virtual async Task<JobsRecord> GetAsync(Guid id)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT rowId,name,state,priority,nextRun,lastRun,runCount,maxAttempts,retryDelay,onSuccessDeleteAfter,onFailureDeleteAfter,expiresAt,correlationId,jobType,jobData,jobHash,lastError,created,modified FROM Jobs " +
                                             "WHERE id = @id LIMIT 1 "+
                                             ";";

                get0Command.AddParameter("@id", DbType.Binary, id);
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
