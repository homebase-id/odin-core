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
using Odin.Core.Storage.SQLite;

// THIS FILE IS AUTO GENERATED - DO NOT EDIT

namespace Odin.Core.Storage.Database.System.Table
{
    public record JobsRecord
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
        private Guid _id;
        public Guid id
        {
           get {
                   return _id;
               }
           set {
                  _id = value;
               }
        }
        private string _name;
        public string name
        {
           get {
                   return _name;
               }
           set {
                    if (value == null) throw new OdinDatabaseValidationException("Cannot be null name");
                    if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short name, was {value.Length} (min 0)");
                    if (value?.Length > 64) throw new OdinDatabaseValidationException($"Too long name, was {value.Length} (max 64)");
                  _name = value;
               }
        }
        internal string nameNoLengthCheck
        {
           get {
                   return _name;
               }
           set {
                    if (value == null) throw new OdinDatabaseValidationException("Cannot be null name");
                    if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short name, was {value.Length} (min 0)");
                  _name = value;
               }
        }
        private Int32 _state;
        public Int32 state
        {
           get {
                   return _state;
               }
           set {
                  _state = value;
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
        private UnixTimeUtc _nextRun;
        public UnixTimeUtc nextRun
        {
           get {
                   return _nextRun;
               }
           set {
                  _nextRun = value;
               }
        }
        private UnixTimeUtc? _lastRun;
        public UnixTimeUtc? lastRun
        {
           get {
                   return _lastRun;
               }
           set {
                  _lastRun = value;
               }
        }
        private Int32 _runCount;
        public Int32 runCount
        {
           get {
                   return _runCount;
               }
           set {
                  _runCount = value;
               }
        }
        private Int32 _maxAttempts;
        public Int32 maxAttempts
        {
           get {
                   return _maxAttempts;
               }
           set {
                  _maxAttempts = value;
               }
        }
        private Int64 _retryDelay;
        public Int64 retryDelay
        {
           get {
                   return _retryDelay;
               }
           set {
                  _retryDelay = value;
               }
        }
        private Int64 _onSuccessDeleteAfter;
        public Int64 onSuccessDeleteAfter
        {
           get {
                   return _onSuccessDeleteAfter;
               }
           set {
                  _onSuccessDeleteAfter = value;
               }
        }
        private Int64 _onFailureDeleteAfter;
        public Int64 onFailureDeleteAfter
        {
           get {
                   return _onFailureDeleteAfter;
               }
           set {
                  _onFailureDeleteAfter = value;
               }
        }
        private UnixTimeUtc? _expiresAt;
        public UnixTimeUtc? expiresAt
        {
           get {
                   return _expiresAt;
               }
           set {
                  _expiresAt = value;
               }
        }
        private string _correlationId;
        public string correlationId
        {
           get {
                   return _correlationId;
               }
           set {
                    if (value == null) throw new OdinDatabaseValidationException("Cannot be null correlationId");
                    if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short correlationId, was {value.Length} (min 0)");
                    if (value?.Length > 64) throw new OdinDatabaseValidationException($"Too long correlationId, was {value.Length} (max 64)");
                  _correlationId = value;
               }
        }
        internal string correlationIdNoLengthCheck
        {
           get {
                   return _correlationId;
               }
           set {
                    if (value == null) throw new OdinDatabaseValidationException("Cannot be null correlationId");
                    if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short correlationId, was {value.Length} (min 0)");
                  _correlationId = value;
               }
        }
        private string _jobType;
        public string jobType
        {
           get {
                   return _jobType;
               }
           set {
                    if (value == null) throw new OdinDatabaseValidationException("Cannot be null jobType");
                    if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short jobType, was {value.Length} (min 0)");
                    if (value?.Length > 65535) throw new OdinDatabaseValidationException($"Too long jobType, was {value.Length} (max 65535)");
                  _jobType = value;
               }
        }
        internal string jobTypeNoLengthCheck
        {
           get {
                   return _jobType;
               }
           set {
                    if (value == null) throw new OdinDatabaseValidationException("Cannot be null jobType");
                    if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short jobType, was {value.Length} (min 0)");
                  _jobType = value;
               }
        }
        private string _jobData;
        public string jobData
        {
           get {
                   return _jobData;
               }
           set {
                    if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short jobData, was {value.Length} (min 0)");
                    if (value?.Length > 65535) throw new OdinDatabaseValidationException($"Too long jobData, was {value.Length} (max 65535)");
                  _jobData = value;
               }
        }
        internal string jobDataNoLengthCheck
        {
           get {
                   return _jobData;
               }
           set {
                    if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short jobData, was {value.Length} (min 0)");
                  _jobData = value;
               }
        }
        private string _jobHash;
        public string jobHash
        {
           get {
                   return _jobHash;
               }
           set {
                    if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short jobHash, was {value.Length} (min 0)");
                    if (value?.Length > 65535) throw new OdinDatabaseValidationException($"Too long jobHash, was {value.Length} (max 65535)");
                  _jobHash = value;
               }
        }
        internal string jobHashNoLengthCheck
        {
           get {
                   return _jobHash;
               }
           set {
                    if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short jobHash, was {value.Length} (min 0)");
                  _jobHash = value;
               }
        }
        private string _lastError;
        public string lastError
        {
           get {
                   return _lastError;
               }
           set {
                    if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short lastError, was {value.Length} (min 0)");
                    if (value?.Length > 65535) throw new OdinDatabaseValidationException($"Too long lastError, was {value.Length} (max 65535)");
                  _lastError = value;
               }
        }
        internal string lastErrorNoLengthCheck
        {
           get {
                   return _lastError;
               }
           set {
                    if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short lastError, was {value.Length} (min 0)");
                  _lastError = value;
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
        private UnixTimeUtc _modified;
        public UnixTimeUtc modified
        {
           get {
                   return _modified;
               }
           set {
                  _modified = value;
               }
        }
    } // End of record JobsRecord

    public abstract class TableJobsCRUD
    {
        private readonly ScopedSystemConnectionFactory _scopedConnectionFactory;

        protected TableJobsCRUD(CacheHelper cache, ScopedSystemConnectionFactory scopedConnectionFactory)
        {
            _scopedConnectionFactory = scopedConnectionFactory;
        }


        public virtual async Task EnsureTableExistsAsync(bool dropExisting = false)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var cmd = cn.CreateCommand();
            if (dropExisting)
            {
                cmd.CommandText = "DROP TABLE IF EXISTS Jobs;";
                await cmd.ExecuteNonQueryAsync();
            }
            var rowid = "";
            if (_scopedConnectionFactory.DatabaseType == DatabaseType.Postgres)
               rowid = "rowid BIGSERIAL PRIMARY KEY,";
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            cmd.CommandText =
                "CREATE TABLE IF NOT EXISTS Jobs("
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
            await cmd.ExecuteNonQueryAsync();
        }

        public virtual async Task<int> InsertAsync(JobsRecord item)
        {
            item.id.AssertGuidNotEmpty("Guid parameter id cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr = SqlExtensions.SqlNowString(_scopedConnectionFactory.DatabaseType);
                insertCommand.CommandText = "INSERT INTO Jobs (id,name,state,priority,nextRun,lastRun,runCount,maxAttempts,retryDelay,onSuccessDeleteAfter,onFailureDeleteAfter,expiresAt,correlationId,jobType,jobData,jobHash,lastError,created,modified) " +
                                             $"VALUES (@id,@name,@state,@priority,@nextRun,@lastRun,@runCount,@maxAttempts,@retryDelay,@onSuccessDeleteAfter,@onFailureDeleteAfter,@expiresAt,@correlationId,@jobType,@jobData,@jobHash,@lastError,{sqlNowStr},{sqlNowStr})"+
                                            "RETURNING Jobs.created,Jobs.modified,Jobs.rowId;";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.ParameterName = "@id";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.ParameterName = "@name";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.ParameterName = "@state";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.ParameterName = "@priority";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.ParameterName = "@nextRun";
                insertCommand.Parameters.Add(insertParam5);
                var insertParam6 = insertCommand.CreateParameter();
                insertParam6.ParameterName = "@lastRun";
                insertCommand.Parameters.Add(insertParam6);
                var insertParam7 = insertCommand.CreateParameter();
                insertParam7.ParameterName = "@runCount";
                insertCommand.Parameters.Add(insertParam7);
                var insertParam8 = insertCommand.CreateParameter();
                insertParam8.ParameterName = "@maxAttempts";
                insertCommand.Parameters.Add(insertParam8);
                var insertParam9 = insertCommand.CreateParameter();
                insertParam9.ParameterName = "@retryDelay";
                insertCommand.Parameters.Add(insertParam9);
                var insertParam10 = insertCommand.CreateParameter();
                insertParam10.ParameterName = "@onSuccessDeleteAfter";
                insertCommand.Parameters.Add(insertParam10);
                var insertParam11 = insertCommand.CreateParameter();
                insertParam11.ParameterName = "@onFailureDeleteAfter";
                insertCommand.Parameters.Add(insertParam11);
                var insertParam12 = insertCommand.CreateParameter();
                insertParam12.ParameterName = "@expiresAt";
                insertCommand.Parameters.Add(insertParam12);
                var insertParam13 = insertCommand.CreateParameter();
                insertParam13.ParameterName = "@correlationId";
                insertCommand.Parameters.Add(insertParam13);
                var insertParam14 = insertCommand.CreateParameter();
                insertParam14.ParameterName = "@jobType";
                insertCommand.Parameters.Add(insertParam14);
                var insertParam15 = insertCommand.CreateParameter();
                insertParam15.ParameterName = "@jobData";
                insertCommand.Parameters.Add(insertParam15);
                var insertParam16 = insertCommand.CreateParameter();
                insertParam16.ParameterName = "@jobHash";
                insertCommand.Parameters.Add(insertParam16);
                var insertParam17 = insertCommand.CreateParameter();
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
                    long modified = (long) rdr[1];
                    item.created = new UnixTimeUtc(created);
                    item.modified = new UnixTimeUtc((long)modified);
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        public virtual async Task<bool> TryInsertAsync(JobsRecord item)
        {
            item.id.AssertGuidNotEmpty("Guid parameter id cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr = SqlExtensions.SqlNowString(_scopedConnectionFactory.DatabaseType);
                insertCommand.CommandText = "INSERT INTO Jobs (id,name,state,priority,nextRun,lastRun,runCount,maxAttempts,retryDelay,onSuccessDeleteAfter,onFailureDeleteAfter,expiresAt,correlationId,jobType,jobData,jobHash,lastError,created,modified) " +
                                            $"VALUES (@id,@name,@state,@priority,@nextRun,@lastRun,@runCount,@maxAttempts,@retryDelay,@onSuccessDeleteAfter,@onFailureDeleteAfter,@expiresAt,@correlationId,@jobType,@jobData,@jobHash,@lastError,{sqlNowStr},{sqlNowStr}) " +
                                            "ON CONFLICT DO NOTHING "+
                                            "RETURNING Jobs.created,Jobs.modified,Jobs.rowId;";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.ParameterName = "@id";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.ParameterName = "@name";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.ParameterName = "@state";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.ParameterName = "@priority";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.ParameterName = "@nextRun";
                insertCommand.Parameters.Add(insertParam5);
                var insertParam6 = insertCommand.CreateParameter();
                insertParam6.ParameterName = "@lastRun";
                insertCommand.Parameters.Add(insertParam6);
                var insertParam7 = insertCommand.CreateParameter();
                insertParam7.ParameterName = "@runCount";
                insertCommand.Parameters.Add(insertParam7);
                var insertParam8 = insertCommand.CreateParameter();
                insertParam8.ParameterName = "@maxAttempts";
                insertCommand.Parameters.Add(insertParam8);
                var insertParam9 = insertCommand.CreateParameter();
                insertParam9.ParameterName = "@retryDelay";
                insertCommand.Parameters.Add(insertParam9);
                var insertParam10 = insertCommand.CreateParameter();
                insertParam10.ParameterName = "@onSuccessDeleteAfter";
                insertCommand.Parameters.Add(insertParam10);
                var insertParam11 = insertCommand.CreateParameter();
                insertParam11.ParameterName = "@onFailureDeleteAfter";
                insertCommand.Parameters.Add(insertParam11);
                var insertParam12 = insertCommand.CreateParameter();
                insertParam12.ParameterName = "@expiresAt";
                insertCommand.Parameters.Add(insertParam12);
                var insertParam13 = insertCommand.CreateParameter();
                insertParam13.ParameterName = "@correlationId";
                insertCommand.Parameters.Add(insertParam13);
                var insertParam14 = insertCommand.CreateParameter();
                insertParam14.ParameterName = "@jobType";
                insertCommand.Parameters.Add(insertParam14);
                var insertParam15 = insertCommand.CreateParameter();
                insertParam15.ParameterName = "@jobData";
                insertCommand.Parameters.Add(insertParam15);
                var insertParam16 = insertCommand.CreateParameter();
                insertParam16.ParameterName = "@jobHash";
                insertCommand.Parameters.Add(insertParam16);
                var insertParam17 = insertCommand.CreateParameter();
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
                    long modified = (long) rdr[1];
                    item.created = new UnixTimeUtc(created);
                    item.modified = new UnixTimeUtc((long)modified);
                    item.rowId = (long) rdr[2];
                    return true;
                }
                return false;
            }
        }

        public virtual async Task<int> UpsertAsync(JobsRecord item)
        {
            item.id.AssertGuidNotEmpty("Guid parameter id cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                string sqlNowStr = SqlExtensions.SqlNowString(_scopedConnectionFactory.DatabaseType);
                upsertCommand.CommandText = "INSERT INTO Jobs (id,name,state,priority,nextRun,lastRun,runCount,maxAttempts,retryDelay,onSuccessDeleteAfter,onFailureDeleteAfter,expiresAt,correlationId,jobType,jobData,jobHash,lastError,created,modified) " +
                                            $"VALUES (@id,@name,@state,@priority,@nextRun,@lastRun,@runCount,@maxAttempts,@retryDelay,@onSuccessDeleteAfter,@onFailureDeleteAfter,@expiresAt,@correlationId,@jobType,@jobData,@jobHash,@lastError,{sqlNowStr},{sqlNowStr})"+
                                            "ON CONFLICT (id) DO UPDATE "+
                                            $"SET name = @name,state = @state,priority = @priority,nextRun = @nextRun,lastRun = @lastRun,runCount = @runCount,maxAttempts = @maxAttempts,retryDelay = @retryDelay,onSuccessDeleteAfter = @onSuccessDeleteAfter,onFailureDeleteAfter = @onFailureDeleteAfter,expiresAt = @expiresAt,correlationId = @correlationId,jobType = @jobType,jobData = @jobData,jobHash = @jobHash,lastError = @lastError,modified = {SqlExtensions.MaxString(_scopedConnectionFactory.DatabaseType)}(Jobs.modified+1,{sqlNowStr}) "+
                                            "RETURNING Jobs.created,Jobs.modified,Jobs.rowId;";
                var upsertParam1 = upsertCommand.CreateParameter();
                upsertParam1.ParameterName = "@id";
                upsertCommand.Parameters.Add(upsertParam1);
                var upsertParam2 = upsertCommand.CreateParameter();
                upsertParam2.ParameterName = "@name";
                upsertCommand.Parameters.Add(upsertParam2);
                var upsertParam3 = upsertCommand.CreateParameter();
                upsertParam3.ParameterName = "@state";
                upsertCommand.Parameters.Add(upsertParam3);
                var upsertParam4 = upsertCommand.CreateParameter();
                upsertParam4.ParameterName = "@priority";
                upsertCommand.Parameters.Add(upsertParam4);
                var upsertParam5 = upsertCommand.CreateParameter();
                upsertParam5.ParameterName = "@nextRun";
                upsertCommand.Parameters.Add(upsertParam5);
                var upsertParam6 = upsertCommand.CreateParameter();
                upsertParam6.ParameterName = "@lastRun";
                upsertCommand.Parameters.Add(upsertParam6);
                var upsertParam7 = upsertCommand.CreateParameter();
                upsertParam7.ParameterName = "@runCount";
                upsertCommand.Parameters.Add(upsertParam7);
                var upsertParam8 = upsertCommand.CreateParameter();
                upsertParam8.ParameterName = "@maxAttempts";
                upsertCommand.Parameters.Add(upsertParam8);
                var upsertParam9 = upsertCommand.CreateParameter();
                upsertParam9.ParameterName = "@retryDelay";
                upsertCommand.Parameters.Add(upsertParam9);
                var upsertParam10 = upsertCommand.CreateParameter();
                upsertParam10.ParameterName = "@onSuccessDeleteAfter";
                upsertCommand.Parameters.Add(upsertParam10);
                var upsertParam11 = upsertCommand.CreateParameter();
                upsertParam11.ParameterName = "@onFailureDeleteAfter";
                upsertCommand.Parameters.Add(upsertParam11);
                var upsertParam12 = upsertCommand.CreateParameter();
                upsertParam12.ParameterName = "@expiresAt";
                upsertCommand.Parameters.Add(upsertParam12);
                var upsertParam13 = upsertCommand.CreateParameter();
                upsertParam13.ParameterName = "@correlationId";
                upsertCommand.Parameters.Add(upsertParam13);
                var upsertParam14 = upsertCommand.CreateParameter();
                upsertParam14.ParameterName = "@jobType";
                upsertCommand.Parameters.Add(upsertParam14);
                var upsertParam15 = upsertCommand.CreateParameter();
                upsertParam15.ParameterName = "@jobData";
                upsertCommand.Parameters.Add(upsertParam15);
                var upsertParam16 = upsertCommand.CreateParameter();
                upsertParam16.ParameterName = "@jobHash";
                upsertCommand.Parameters.Add(upsertParam16);
                var upsertParam17 = upsertCommand.CreateParameter();
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
                    long modified = (long) rdr[1];
                    item.created = new UnixTimeUtc(created);
                    item.modified = new UnixTimeUtc((long)modified);
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        public virtual async Task<int> UpdateAsync(JobsRecord item)
        {
            item.id.AssertGuidNotEmpty("Guid parameter id cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                string sqlNowStr = SqlExtensions.SqlNowString(_scopedConnectionFactory.DatabaseType);
                updateCommand.CommandText = "UPDATE Jobs " +
                                            $"SET name = @name,state = @state,priority = @priority,nextRun = @nextRun,lastRun = @lastRun,runCount = @runCount,maxAttempts = @maxAttempts,retryDelay = @retryDelay,onSuccessDeleteAfter = @onSuccessDeleteAfter,onFailureDeleteAfter = @onFailureDeleteAfter,expiresAt = @expiresAt,correlationId = @correlationId,jobType = @jobType,jobData = @jobData,jobHash = @jobHash,lastError = @lastError,modified = {SqlExtensions.MaxString(_scopedConnectionFactory.DatabaseType)}(Jobs.modified+1,{sqlNowStr}) "+
                                            "WHERE (id = @id) "+
                                            "RETURNING Jobs.created,Jobs.modified,Jobs.rowId;";
                var updateParam1 = updateCommand.CreateParameter();
                updateParam1.ParameterName = "@id";
                updateCommand.Parameters.Add(updateParam1);
                var updateParam2 = updateCommand.CreateParameter();
                updateParam2.ParameterName = "@name";
                updateCommand.Parameters.Add(updateParam2);
                var updateParam3 = updateCommand.CreateParameter();
                updateParam3.ParameterName = "@state";
                updateCommand.Parameters.Add(updateParam3);
                var updateParam4 = updateCommand.CreateParameter();
                updateParam4.ParameterName = "@priority";
                updateCommand.Parameters.Add(updateParam4);
                var updateParam5 = updateCommand.CreateParameter();
                updateParam5.ParameterName = "@nextRun";
                updateCommand.Parameters.Add(updateParam5);
                var updateParam6 = updateCommand.CreateParameter();
                updateParam6.ParameterName = "@lastRun";
                updateCommand.Parameters.Add(updateParam6);
                var updateParam7 = updateCommand.CreateParameter();
                updateParam7.ParameterName = "@runCount";
                updateCommand.Parameters.Add(updateParam7);
                var updateParam8 = updateCommand.CreateParameter();
                updateParam8.ParameterName = "@maxAttempts";
                updateCommand.Parameters.Add(updateParam8);
                var updateParam9 = updateCommand.CreateParameter();
                updateParam9.ParameterName = "@retryDelay";
                updateCommand.Parameters.Add(updateParam9);
                var updateParam10 = updateCommand.CreateParameter();
                updateParam10.ParameterName = "@onSuccessDeleteAfter";
                updateCommand.Parameters.Add(updateParam10);
                var updateParam11 = updateCommand.CreateParameter();
                updateParam11.ParameterName = "@onFailureDeleteAfter";
                updateCommand.Parameters.Add(updateParam11);
                var updateParam12 = updateCommand.CreateParameter();
                updateParam12.ParameterName = "@expiresAt";
                updateCommand.Parameters.Add(updateParam12);
                var updateParam13 = updateCommand.CreateParameter();
                updateParam13.ParameterName = "@correlationId";
                updateCommand.Parameters.Add(updateParam13);
                var updateParam14 = updateCommand.CreateParameter();
                updateParam14.ParameterName = "@jobType";
                updateCommand.Parameters.Add(updateParam14);
                var updateParam15 = updateCommand.CreateParameter();
                updateParam15.ParameterName = "@jobData";
                updateCommand.Parameters.Add(updateParam15);
                var updateParam16 = updateCommand.CreateParameter();
                updateParam16.ParameterName = "@jobHash";
                updateCommand.Parameters.Add(updateParam16);
                var updateParam17 = updateCommand.CreateParameter();
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
                    long modified = (long) rdr[1];
                    item.created = new UnixTimeUtc(created);
                    item.modified = new UnixTimeUtc((long)modified);
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        public virtual async Task<int> GetCountAsync()
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

        public static List<string> GetColumnNames()
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
            item.nameNoLengthCheck = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[2];
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
            item.correlationIdNoLengthCheck = (rdr[13] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[13];
            item.jobTypeNoLengthCheck = (rdr[14] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[14];
            item.jobDataNoLengthCheck = (rdr[15] == DBNull.Value) ? null : (string)rdr[15];
            item.jobHashNoLengthCheck = (rdr[16] == DBNull.Value) ? null : (string)rdr[16];
            item.lastErrorNoLengthCheck = (rdr[17] == DBNull.Value) ? null : (string)rdr[17];
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
                var delete0Param1 = delete0Command.CreateParameter();
                delete0Param1.ParameterName = "@id";
                delete0Command.Parameters.Add(delete0Param1);

                delete0Param1.Value = id.ToByteArray();
                var count = await delete0Command.ExecuteNonQueryAsync();
                return count;
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
            item.nameNoLengthCheck = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[1];
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
            item.correlationIdNoLengthCheck = (rdr[12] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[12];
            item.jobTypeNoLengthCheck = (rdr[13] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[13];
            item.jobDataNoLengthCheck = (rdr[14] == DBNull.Value) ? null : (string)rdr[14];
            item.jobHashNoLengthCheck = (rdr[15] == DBNull.Value) ? null : (string)rdr[15];
            item.lastErrorNoLengthCheck = (rdr[16] == DBNull.Value) ? null : (string)rdr[16];
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
                                             "WHERE id = @id LIMIT 1;"+
                                             ";";
                var get0Param1 = get0Command.CreateParameter();
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
