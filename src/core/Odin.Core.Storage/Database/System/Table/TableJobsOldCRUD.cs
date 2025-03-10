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

namespace Odin.Core.Storage.Database.System.Table
{
    public class JobsOldRecord
    {
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
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 0) throw new Exception("Too short");
                    if (value?.Length > 64) throw new Exception("Too long");
                  _name = value;
               }
        }
        internal string nameNoLengthCheck
        {
           get {
                   return _name;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 0) throw new Exception("Too short");
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
                    if (value == null) throw new Exception("Cannot be null");
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
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 0) throw new Exception("Too short");
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
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 0) throw new Exception("Too short");
                    if (value?.Length > 65535) throw new Exception("Too long");
                  _jobType = value;
               }
        }
        internal string jobTypeNoLengthCheck
        {
           get {
                   return _jobType;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 0) throw new Exception("Too short");
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
                    if (value?.Length < 0) throw new Exception("Too short");
                    if (value?.Length > 65535) throw new Exception("Too long");
                  _jobData = value;
               }
        }
        internal string jobDataNoLengthCheck
        {
           get {
                   return _jobData;
               }
           set {
                    if (value?.Length < 0) throw new Exception("Too short");
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
                    if (value?.Length < 0) throw new Exception("Too short");
                    if (value?.Length > 65535) throw new Exception("Too long");
                  _jobHash = value;
               }
        }
        internal string jobHashNoLengthCheck
        {
           get {
                   return _jobHash;
               }
           set {
                    if (value?.Length < 0) throw new Exception("Too short");
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
                    if (value?.Length < 0) throw new Exception("Too short");
                    if (value?.Length > 65535) throw new Exception("Too long");
                  _lastError = value;
               }
        }
        internal string lastErrorNoLengthCheck
        {
           get {
                   return _lastError;
               }
           set {
                    if (value?.Length < 0) throw new Exception("Too short");
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
    } // End of class JobsOldRecord

    public class TableJobsOldCRUD
    {
        private readonly ScopedSystemConnectionFactory _scopedConnectionFactory;

        public TableJobsOldCRUD(CacheHelper cache, ScopedSystemConnectionFactory scopedConnectionFactory)
        {
            _scopedConnectionFactory = scopedConnectionFactory;
        }


        public virtual async Task EnsureTableExistsAsync(bool dropExisting = false)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var cmd = cn.CreateCommand();
            if (dropExisting)
            {
                cmd.CommandText = "DROP TABLE IF EXISTS JobsOld;";
                await cmd.ExecuteNonQueryAsync();
            }
            var rowid = "";
            if (_scopedConnectionFactory.DatabaseType == DatabaseType.Postgres)
            {
                   rowid = ", rowid BIGSERIAL NOT NULL UNIQUE ";
            }
            cmd.CommandText =
                "CREATE TABLE IF NOT EXISTS JobsOld("
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
                   +"modified BIGINT  "
                   + rowid
                   +", PRIMARY KEY (id)"
                   +");"
                   +"CREATE INDEX IF NOT EXISTS Idx0JobsOld ON JobsOld(state);"
                   +"CREATE INDEX IF NOT EXISTS Idx1JobsOld ON JobsOld(expiresAt);"
                   +"CREATE INDEX IF NOT EXISTS Idx2JobsOld ON JobsOld(nextRun,priority);"
                   +"CREATE INDEX IF NOT EXISTS Idx3JobsOld ON JobsOld(jobHash);"
                   ;
            await cmd.ExecuteNonQueryAsync();
        }

        public virtual async Task<int> InsertAsync(JobsOldRecord item)
        {
            item.id.AssertGuidNotEmpty("Guid parameter id cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO JobsOld (id,name,state,priority,nextRun,lastRun,runCount,maxAttempts,retryDelay,onSuccessDeleteAfter,onFailureDeleteAfter,expiresAt,correlationId,jobType,jobData,jobHash,lastError,created,modified) " +
                                             "VALUES (@id,@name,@state,@priority,@nextRun,@lastRun,@runCount,@maxAttempts,@retryDelay,@onSuccessDeleteAfter,@onFailureDeleteAfter,@expiresAt,@correlationId,@jobType,@jobData,@jobHash,@lastError,@created,@modified)";
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
                var insertParam18 = insertCommand.CreateParameter();
                insertParam18.ParameterName = "@created";
                insertCommand.Parameters.Add(insertParam18);
                var insertParam19 = insertCommand.CreateParameter();
                insertParam19.ParameterName = "@modified";
                insertCommand.Parameters.Add(insertParam19);
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
                var now = UnixTimeUtc.Now();
                insertParam18.Value = now.milliseconds;
                item.modified = null;
                insertParam19.Value = DBNull.Value;
                var count = await insertCommand.ExecuteNonQueryAsync();
                if (count > 0)
                {
                     item.created = now;
                }
                return count;
            }
        }

        public virtual async Task<bool> TryInsertAsync(JobsOldRecord item)
        {
            item.id.AssertGuidNotEmpty("Guid parameter id cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO JobsOld (id,name,state,priority,nextRun,lastRun,runCount,maxAttempts,retryDelay,onSuccessDeleteAfter,onFailureDeleteAfter,expiresAt,correlationId,jobType,jobData,jobHash,lastError,created,modified) " +
                                             "VALUES (@id,@name,@state,@priority,@nextRun,@lastRun,@runCount,@maxAttempts,@retryDelay,@onSuccessDeleteAfter,@onFailureDeleteAfter,@expiresAt,@correlationId,@jobType,@jobData,@jobHash,@lastError,@created,@modified) " +
                                             "ON CONFLICT DO NOTHING";
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
                var insertParam18 = insertCommand.CreateParameter();
                insertParam18.ParameterName = "@created";
                insertCommand.Parameters.Add(insertParam18);
                var insertParam19 = insertCommand.CreateParameter();
                insertParam19.ParameterName = "@modified";
                insertCommand.Parameters.Add(insertParam19);
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
                var now = UnixTimeUtc.Now();
                insertParam18.Value = now.milliseconds;
                item.modified = null;
                insertParam19.Value = DBNull.Value;
                var count = await insertCommand.ExecuteNonQueryAsync();
                if (count > 0)
                {
                    item.created = now;
                }
                return count > 0;
            }
        }

        public virtual async Task<int> UpsertAsync(JobsOldRecord item)
        {
            item.id.AssertGuidNotEmpty("Guid parameter id cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                upsertCommand.CommandText = "INSERT INTO JobsOld (id,name,state,priority,nextRun,lastRun,runCount,maxAttempts,retryDelay,onSuccessDeleteAfter,onFailureDeleteAfter,expiresAt,correlationId,jobType,jobData,jobHash,lastError,created) " +
                                             "VALUES (@id,@name,@state,@priority,@nextRun,@lastRun,@runCount,@maxAttempts,@retryDelay,@onSuccessDeleteAfter,@onFailureDeleteAfter,@expiresAt,@correlationId,@jobType,@jobData,@jobHash,@lastError,@created)"+
                                             "ON CONFLICT (id) DO UPDATE "+
                                             "SET name = @name,state = @state,priority = @priority,nextRun = @nextRun,lastRun = @lastRun,runCount = @runCount,maxAttempts = @maxAttempts,retryDelay = @retryDelay,onSuccessDeleteAfter = @onSuccessDeleteAfter,onFailureDeleteAfter = @onFailureDeleteAfter,expiresAt = @expiresAt,correlationId = @correlationId,jobType = @jobType,jobData = @jobData,jobHash = @jobHash,lastError = @lastError,modified = @modified "+
                                             "RETURNING created, modified;";
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
                var upsertParam18 = upsertCommand.CreateParameter();
                upsertParam18.ParameterName = "@created";
                upsertCommand.Parameters.Add(upsertParam18);
                var upsertParam19 = upsertCommand.CreateParameter();
                upsertParam19.ParameterName = "@modified";
                upsertCommand.Parameters.Add(upsertParam19);
                var now = UnixTimeUtc.Now();
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
                upsertParam18.Value = now.milliseconds;
                upsertParam19.Value = now.milliseconds;
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

        public virtual async Task<int> UpdateAsync(JobsOldRecord item)
        {
            item.id.AssertGuidNotEmpty("Guid parameter id cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                updateCommand.CommandText = "UPDATE JobsOld " +
                                             "SET name = @name,state = @state,priority = @priority,nextRun = @nextRun,lastRun = @lastRun,runCount = @runCount,maxAttempts = @maxAttempts,retryDelay = @retryDelay,onSuccessDeleteAfter = @onSuccessDeleteAfter,onFailureDeleteAfter = @onFailureDeleteAfter,expiresAt = @expiresAt,correlationId = @correlationId,jobType = @jobType,jobData = @jobData,jobHash = @jobHash,lastError = @lastError,modified = @modified "+
                                             "WHERE (id = @id)";
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
                var updateParam18 = updateCommand.CreateParameter();
                updateParam18.ParameterName = "@created";
                updateCommand.Parameters.Add(updateParam18);
                var updateParam19 = updateCommand.CreateParameter();
                updateParam19.ParameterName = "@modified";
                updateCommand.Parameters.Add(updateParam19);
                var now = UnixTimeUtc.Now();
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
                updateParam18.Value = now.milliseconds;
                updateParam19.Value = now.milliseconds;
                var count = await updateCommand.ExecuteNonQueryAsync();
                if (count > 0)
                {
                     item.modified = now;
                }
                return count;
            }
        }

        public virtual async Task<int> RenameAsync()
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getCountCommand = cn.CreateCommand();
            {
                getCountCommand.CommandText = "ALTER TABLE jobs RENAME TO JobsOld;";
                var count = await getCountCommand.ExecuteScalarAsync();
                if (count == null || count == DBNull.Value || !(count is int || count is long))
                    return -1;
                else
                    return Convert.ToInt32(count);
            }
        }

        public virtual async Task<int> GetCountAsync()
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getCountCommand = cn.CreateCommand();
            {
                 // TODO: this is SQLite specific
                getCountCommand.CommandText = "SELECT COUNT(*) FROM JobsOld;";
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

        // SELECT id,name,state,priority,nextRun,lastRun,runCount,maxAttempts,retryDelay,onSuccessDeleteAfter,onFailureDeleteAfter,expiresAt,correlationId,jobType,jobData,jobHash,lastError,created,modified
        public JobsOldRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<JobsOldRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new JobsOldRecord();
            item.id = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[0]);
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
            item.modified = (rdr[18] == DBNull.Value) ? null : new UnixTimeUtc((long)rdr[18]);
            return item;
       }

        public virtual async Task<int> DeleteAsync(Guid id)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM JobsOld " +
                                             "WHERE id = @id";
                var delete0Param1 = delete0Command.CreateParameter();
                delete0Param1.ParameterName = "@id";
                delete0Command.Parameters.Add(delete0Param1);

                delete0Param1.Value = id.ToByteArray();
                var count = await delete0Command.ExecuteNonQueryAsync();
                return count;
            }
        }

        public JobsOldRecord ReadRecordFromReader0(DbDataReader rdr)
        {
            var result = new List<JobsOldRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new JobsOldRecord();
            item.id = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[0]);
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
            item.modified = (rdr[18] == DBNull.Value) ? null : new UnixTimeUtc((long)rdr[18]);
            return item;
       }

        public virtual async Task<List<JobsOldRecord>> GetAllAsync()
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT id,name,state,priority,nextRun,lastRun,runCount,maxAttempts,retryDelay,onSuccessDeleteAfter,onFailureDeleteAfter,expiresAt,correlationId,jobType,jobData,jobHash,lastError,created,modified FROM JobsOld " +
                                             ";";

                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return new List<JobsOldRecord>();
                        }
                        var result = new List<JobsOldRecord>();
                        while (true)
                        {
                            result.Add(ReadRecordFromReader0(rdr));
                            if (!await rdr.ReadAsync())
                                break;
                        }
                        return result;
                    } // using
                } //
            } // using
        }

        public JobsOldRecord ReadRecordFromReader1(DbDataReader rdr,Guid id)
        {
            var result = new List<JobsOldRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new JobsOldRecord();
            item.id = id;
            item.nameNoLengthCheck = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[0];
            item.state = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[1];
            item.priority = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[2];
            item.nextRun = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[3]);
            item.lastRun = (rdr[4] == DBNull.Value) ? null : new UnixTimeUtc((long)rdr[4]);
            item.runCount = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[5];
            item.maxAttempts = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[6];
            item.retryDelay = (rdr[7] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[7];
            item.onSuccessDeleteAfter = (rdr[8] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[8];
            item.onFailureDeleteAfter = (rdr[9] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[9];
            item.expiresAt = (rdr[10] == DBNull.Value) ? null : new UnixTimeUtc((long)rdr[10]);
            item.correlationIdNoLengthCheck = (rdr[11] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[11];
            item.jobTypeNoLengthCheck = (rdr[12] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[12];
            item.jobDataNoLengthCheck = (rdr[13] == DBNull.Value) ? null : (string)rdr[13];
            item.jobHashNoLengthCheck = (rdr[14] == DBNull.Value) ? null : (string)rdr[14];
            item.lastErrorNoLengthCheck = (rdr[15] == DBNull.Value) ? null : (string)rdr[15];
            item.created = (rdr[16] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[16]);
            item.modified = (rdr[17] == DBNull.Value) ? null : new UnixTimeUtc((long)rdr[17]);
            return item;
       }

        public virtual async Task<JobsOldRecord> GetAsync(Guid id)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get1Command = cn.CreateCommand();
            {
                get1Command.CommandText = "SELECT name,state,priority,nextRun,lastRun,runCount,maxAttempts,retryDelay,onSuccessDeleteAfter,onFailureDeleteAfter,expiresAt,correlationId,jobType,jobData,jobHash,lastError,created,modified FROM JobsOld " +
                                             "WHERE id = @id LIMIT 1;"+
                                             ";";
                var get1Param1 = get1Command.CreateParameter();
                get1Param1.ParameterName = "@id";
                get1Command.Parameters.Add(get1Param1);

                get1Param1.Value = id.ToByteArray();
                {
                    using (var rdr = await get1Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader1(rdr,id);
                        return r;
                    } // using
                } //
            } // using
        }

    }
}
