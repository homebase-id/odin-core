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
using Odin.Core.Util;

// THIS FILE IS AUTO GENERATED - DO NOT EDIT

namespace Odin.Core.Storage.Database.System.Table
{
    public class JobsRecord
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
        private UnixTimeUtcUnique _created;
        public UnixTimeUtcUnique created
        {
           get {
                   return _created;
               }
           set {
                  _created = value;
               }
        }
        private UnixTimeUtcUnique? _modified;
        public UnixTimeUtcUnique? modified
        {
           get {
                   return _modified;
               }
           set {
                  _modified = value;
               }
        }
    } // End of class JobsRecord

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
            {
                if (dropExisting)
                {
                   cmd.CommandText = "DROP TABLE IF EXISTS jobs;";
                   await cmd.ExecuteNonQueryAsync();
                }
                cmd.CommandText =
                "CREATE TABLE IF NOT EXISTS jobs("
                 +"id BLOB NOT NULL UNIQUE, "
                 +"name STRING NOT NULL, "
                 +"state INT NOT NULL, "
                 +"priority INT NOT NULL, "
                 +"nextRun INT NOT NULL, "
                 +"lastRun INT , "
                 +"runCount INT NOT NULL, "
                 +"maxAttempts INT NOT NULL, "
                 +"retryDelay INT NOT NULL, "
                 +"onSuccessDeleteAfter INT NOT NULL, "
                 +"onFailureDeleteAfter INT NOT NULL, "
                 +"expiresAt INT , "
                 +"correlationId STRING NOT NULL, "
                 +"jobType STRING NOT NULL, "
                 +"jobData STRING , "
                 +"jobHash STRING  UNIQUE, "
                 +"lastError STRING , "
                 +"created INT NOT NULL, "
                 +"modified INT  "
                 +", PRIMARY KEY (id)"
                 +");"
                 +"CREATE INDEX IF NOT EXISTS Idx0TableJobsCRUD ON jobs(state);"
                 +"CREATE INDEX IF NOT EXISTS Idx1TableJobsCRUD ON jobs(expiresAt);"
                 +"CREATE INDEX IF NOT EXISTS Idx2TableJobsCRUD ON jobs(nextRun,priority);"
                 +"CREATE INDEX IF NOT EXISTS Idx3TableJobsCRUD ON jobs(jobHash);"
                 ;
                 await cmd.ExecuteNonQueryAsync();
            }
        }

        public virtual async Task<int> InsertAsync(JobsRecord item)
        {
            item.id.AssertGuidNotEmpty("Guid parameter id cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO jobs (id,name,state,priority,nextRun,lastRun,runCount,maxAttempts,retryDelay,onSuccessDeleteAfter,onFailureDeleteAfter,expiresAt,correlationId,jobType,jobData,jobHash,lastError,created,modified) " +
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
                var now = UnixTimeUtcUnique.Now();
                insertParam18.Value = now.uniqueTime;
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

        public virtual async Task<int> TryInsertAsync(JobsRecord item)
        {
            item.id.AssertGuidNotEmpty("Guid parameter id cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT OR IGNORE INTO jobs (id,name,state,priority,nextRun,lastRun,runCount,maxAttempts,retryDelay,onSuccessDeleteAfter,onFailureDeleteAfter,expiresAt,correlationId,jobType,jobData,jobHash,lastError,created,modified) " +
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
                var now = UnixTimeUtcUnique.Now();
                insertParam18.Value = now.uniqueTime;
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

        public virtual async Task<int> UpsertAsync(JobsRecord item)
        {
            item.id.AssertGuidNotEmpty("Guid parameter id cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                upsertCommand.CommandText = "INSERT INTO jobs (id,name,state,priority,nextRun,lastRun,runCount,maxAttempts,retryDelay,onSuccessDeleteAfter,onFailureDeleteAfter,expiresAt,correlationId,jobType,jobData,jobHash,lastError,created) " +
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
                var now = UnixTimeUtcUnique.Now();
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
                upsertParam18.Value = now.uniqueTime;
                upsertParam19.Value = now.uniqueTime;
                await using var rdr = await upsertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                   long created = rdr.GetInt64(0);
                   long? modified = rdr.IsDBNull(1) ? null : rdr.GetInt64(1);
                   item.created = new UnixTimeUtcUnique(created);
                   if (modified != null)
                      item.modified = new UnixTimeUtcUnique((long)modified);
                   else
                      item.modified = null;
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
                updateCommand.CommandText = "UPDATE jobs " +
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
                var now = UnixTimeUtcUnique.Now();
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
                updateParam18.Value = now.uniqueTime;
                updateParam19.Value = now.uniqueTime;
                var count = await updateCommand.ExecuteNonQueryAsync();
                if (count > 0)
                {
                     item.modified = now;
                }
                return count;
            }
        }

        public virtual async Task<int> GetCountDirtyAsync()
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getCountCommand = cn.CreateCommand();
            {
                 // TODO: this is SQLite specific
                getCountCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM jobs; PRAGMA read_uncommitted = 0;";
                var count = await getCountCommand.ExecuteScalarAsync();
                if (count == null || count == DBNull.Value || !(count is int || count is long))
                    return -1;
                else
                    return Convert.ToInt32(count);
            }
        }

        public List<string> GetColumnNames()
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
        public JobsRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<JobsRecord>();
            byte[] tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new JobsRecord();

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(0, 0, guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in id...");
                item.id = new Guid(guid);
            }

            if (rdr.IsDBNull(1))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.name = rdr.GetString(1);
            }

            if (rdr.IsDBNull(2))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.state = rdr.GetInt32(2);
            }

            if (rdr.IsDBNull(3))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.priority = rdr.GetInt32(3);
            }

            if (rdr.IsDBNull(4))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.nextRun = new UnixTimeUtc(rdr.GetInt64(4));
            }

            if (rdr.IsDBNull(5))
                item.lastRun = null;
            else
            {
                item.lastRun = new UnixTimeUtc(rdr.GetInt64(5));
            }

            if (rdr.IsDBNull(6))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.runCount = rdr.GetInt32(6);
            }

            if (rdr.IsDBNull(7))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.maxAttempts = rdr.GetInt32(7);
            }

            if (rdr.IsDBNull(8))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                        item.retryDelay = rdr.GetInt64(8);
            }

            if (rdr.IsDBNull(9))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                        item.onSuccessDeleteAfter = rdr.GetInt64(9);
            }

            if (rdr.IsDBNull(10))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                        item.onFailureDeleteAfter = rdr.GetInt64(10);
            }

            if (rdr.IsDBNull(11))
                item.expiresAt = null;
            else
            {
                item.expiresAt = new UnixTimeUtc(rdr.GetInt64(11));
            }

            if (rdr.IsDBNull(12))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.correlationId = rdr.GetString(12);
            }

            if (rdr.IsDBNull(13))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.jobType = rdr.GetString(13);
            }

            if (rdr.IsDBNull(14))
                item.jobData = null;
            else
            {
                item.jobData = rdr.GetString(14);
            }

            if (rdr.IsDBNull(15))
                item.jobHash = null;
            else
            {
                item.jobHash = rdr.GetString(15);
            }

            if (rdr.IsDBNull(16))
                item.lastError = null;
            else
            {
                item.lastError = rdr.GetString(16);
            }

            if (rdr.IsDBNull(17))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.created = new UnixTimeUtcUnique(rdr.GetInt64(17));
            }

            if (rdr.IsDBNull(18))
                item.modified = null;
            else
            {
                item.modified = new UnixTimeUtcUnique(rdr.GetInt64(18));
            }
            return item;
       }

        public virtual async Task<int> DeleteAsync(Guid id)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM jobs " +
                                             "WHERE id = @id";
                var delete0Param1 = delete0Command.CreateParameter();
                delete0Param1.ParameterName = "@id";
                delete0Command.Parameters.Add(delete0Param1);

                delete0Param1.Value = id.ToByteArray();
                var count = await delete0Command.ExecuteNonQueryAsync();
                return count;
            }
        }

        public JobsRecord ReadRecordFromReader0(DbDataReader rdr, Guid id)
        {
            var result = new List<JobsRecord>();
            byte[] tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new JobsRecord();
            item.id = id;

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.name = rdr.GetString(0);
            }

            if (rdr.IsDBNull(1))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.state = rdr.GetInt32(1);
            }

            if (rdr.IsDBNull(2))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.priority = rdr.GetInt32(2);
            }

            if (rdr.IsDBNull(3))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.nextRun = new UnixTimeUtc(rdr.GetInt64(3));
            }

            if (rdr.IsDBNull(4))
                item.lastRun = null;
            else
            {
                item.lastRun = new UnixTimeUtc(rdr.GetInt64(4));
            }

            if (rdr.IsDBNull(5))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.runCount = rdr.GetInt32(5);
            }

            if (rdr.IsDBNull(6))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.maxAttempts = rdr.GetInt32(6);
            }

            if (rdr.IsDBNull(7))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                        item.retryDelay = rdr.GetInt64(7);
            }

            if (rdr.IsDBNull(8))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                        item.onSuccessDeleteAfter = rdr.GetInt64(8);
            }

            if (rdr.IsDBNull(9))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                        item.onFailureDeleteAfter = rdr.GetInt64(9);
            }

            if (rdr.IsDBNull(10))
                item.expiresAt = null;
            else
            {
                item.expiresAt = new UnixTimeUtc(rdr.GetInt64(10));
            }

            if (rdr.IsDBNull(11))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.correlationId = rdr.GetString(11);
            }

            if (rdr.IsDBNull(12))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.jobType = rdr.GetString(12);
            }

            if (rdr.IsDBNull(13))
                item.jobData = null;
            else
            {
                item.jobData = rdr.GetString(13);
            }

            if (rdr.IsDBNull(14))
                item.jobHash = null;
            else
            {
                item.jobHash = rdr.GetString(14);
            }

            if (rdr.IsDBNull(15))
                item.lastError = null;
            else
            {
                item.lastError = rdr.GetString(15);
            }

            if (rdr.IsDBNull(16))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.created = new UnixTimeUtcUnique(rdr.GetInt64(16));
            }

            if (rdr.IsDBNull(17))
                item.modified = null;
            else
            {
                item.modified = new UnixTimeUtcUnique(rdr.GetInt64(17));
            }
            return item;
       }

        public virtual async Task<JobsRecord> GetAsync(Guid id)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT name,state,priority,nextRun,lastRun,runCount,maxAttempts,retryDelay,onSuccessDeleteAfter,onFailureDeleteAfter,expiresAt,correlationId,jobType,jobData,jobHash,lastError,created,modified FROM jobs " +
                                             "WHERE id = @id LIMIT 1;";
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
                        var r = ReadRecordFromReader0(rdr, id);
                        return r;
                    } // using
                } //
            } // using
        }

    }
}
