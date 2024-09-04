using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;
using Odin.Core.Identity;
using System.Runtime.CompilerServices;

namespace Odin.Core.Storage.SQLite.ServerDatabase
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

    public class TableJobsCRUD : TableBase
    {
        private bool _disposed = false;

        public TableJobsCRUD(ServerDatabase db, CacheHelper cache) : base(db, "jobs")
        {
        }

        ~TableJobsCRUD()
        {
            if (_disposed == false) throw new Exception("TableJobsCRUD Not disposed properly");
        }

        public override void Dispose()
        {
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        public sealed override void EnsureTableExists(DatabaseConnection conn, bool dropExisting = false)
        {
                using (var cmd = _database.CreateCommand())
                {
                    if (dropExisting)
                    {
                       cmd.CommandText = "DROP TABLE IF EXISTS jobs;";
                       conn.ExecuteNonQuery(cmd);
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
                    conn.ExecuteNonQuery(cmd);
            }
        }

        public virtual int Insert(DatabaseConnection conn, JobsRecord item)
        {
            using (var _insertCommand = _database.CreateCommand())
            {
                _insertCommand.CommandText = "INSERT INTO jobs (id,name,state,priority,nextRun,lastRun,runCount,maxAttempts,retryDelay,onSuccessDeleteAfter,onFailureDeleteAfter,expiresAt,correlationId,jobType,jobData,jobHash,lastError,created,modified) " +
                                             "VALUES (@id,@name,@state,@priority,@nextRun,@lastRun,@runCount,@maxAttempts,@retryDelay,@onSuccessDeleteAfter,@onFailureDeleteAfter,@expiresAt,@correlationId,@jobType,@jobData,@jobHash,@lastError,@created,@modified)";
                var _insertParam1 = _insertCommand.CreateParameter();
                _insertParam1.ParameterName = "@id";
                _insertCommand.Parameters.Add(_insertParam1);
                var _insertParam2 = _insertCommand.CreateParameter();
                _insertParam2.ParameterName = "@name";
                _insertCommand.Parameters.Add(_insertParam2);
                var _insertParam3 = _insertCommand.CreateParameter();
                _insertParam3.ParameterName = "@state";
                _insertCommand.Parameters.Add(_insertParam3);
                var _insertParam4 = _insertCommand.CreateParameter();
                _insertParam4.ParameterName = "@priority";
                _insertCommand.Parameters.Add(_insertParam4);
                var _insertParam5 = _insertCommand.CreateParameter();
                _insertParam5.ParameterName = "@nextRun";
                _insertCommand.Parameters.Add(_insertParam5);
                var _insertParam6 = _insertCommand.CreateParameter();
                _insertParam6.ParameterName = "@lastRun";
                _insertCommand.Parameters.Add(_insertParam6);
                var _insertParam7 = _insertCommand.CreateParameter();
                _insertParam7.ParameterName = "@runCount";
                _insertCommand.Parameters.Add(_insertParam7);
                var _insertParam8 = _insertCommand.CreateParameter();
                _insertParam8.ParameterName = "@maxAttempts";
                _insertCommand.Parameters.Add(_insertParam8);
                var _insertParam9 = _insertCommand.CreateParameter();
                _insertParam9.ParameterName = "@retryDelay";
                _insertCommand.Parameters.Add(_insertParam9);
                var _insertParam10 = _insertCommand.CreateParameter();
                _insertParam10.ParameterName = "@onSuccessDeleteAfter";
                _insertCommand.Parameters.Add(_insertParam10);
                var _insertParam11 = _insertCommand.CreateParameter();
                _insertParam11.ParameterName = "@onFailureDeleteAfter";
                _insertCommand.Parameters.Add(_insertParam11);
                var _insertParam12 = _insertCommand.CreateParameter();
                _insertParam12.ParameterName = "@expiresAt";
                _insertCommand.Parameters.Add(_insertParam12);
                var _insertParam13 = _insertCommand.CreateParameter();
                _insertParam13.ParameterName = "@correlationId";
                _insertCommand.Parameters.Add(_insertParam13);
                var _insertParam14 = _insertCommand.CreateParameter();
                _insertParam14.ParameterName = "@jobType";
                _insertCommand.Parameters.Add(_insertParam14);
                var _insertParam15 = _insertCommand.CreateParameter();
                _insertParam15.ParameterName = "@jobData";
                _insertCommand.Parameters.Add(_insertParam15);
                var _insertParam16 = _insertCommand.CreateParameter();
                _insertParam16.ParameterName = "@jobHash";
                _insertCommand.Parameters.Add(_insertParam16);
                var _insertParam17 = _insertCommand.CreateParameter();
                _insertParam17.ParameterName = "@lastError";
                _insertCommand.Parameters.Add(_insertParam17);
                var _insertParam18 = _insertCommand.CreateParameter();
                _insertParam18.ParameterName = "@created";
                _insertCommand.Parameters.Add(_insertParam18);
                var _insertParam19 = _insertCommand.CreateParameter();
                _insertParam19.ParameterName = "@modified";
                _insertCommand.Parameters.Add(_insertParam19);
                _insertParam1.Value = item.id.ToByteArray();
                _insertParam2.Value = item.name;
                _insertParam3.Value = item.state;
                _insertParam4.Value = item.priority;
                _insertParam5.Value = item.nextRun.milliseconds;
                _insertParam6.Value = item.lastRun == null ? (object)DBNull.Value : item.lastRun?.milliseconds;
                _insertParam7.Value = item.runCount;
                _insertParam8.Value = item.maxAttempts;
                _insertParam9.Value = item.retryDelay;
                _insertParam10.Value = item.onSuccessDeleteAfter;
                _insertParam11.Value = item.onFailureDeleteAfter;
                _insertParam12.Value = item.expiresAt == null ? (object)DBNull.Value : item.expiresAt?.milliseconds;
                _insertParam13.Value = item.correlationId;
                _insertParam14.Value = item.jobType;
                _insertParam15.Value = item.jobData ?? (object)DBNull.Value;
                _insertParam16.Value = item.jobHash ?? (object)DBNull.Value;
                _insertParam17.Value = item.lastError ?? (object)DBNull.Value;
                var now = UnixTimeUtcUnique.Now();
                _insertParam18.Value = now.uniqueTime;
                item.modified = null;
                _insertParam19.Value = DBNull.Value;
                var count = conn.ExecuteNonQuery(_insertCommand);
                if (count > 0)
                {
                     item.created = now;
                }
                return count;
            } // Using
        }

        public virtual int TryInsert(DatabaseConnection conn, JobsRecord item)
        {
            using (var _insertCommand = _database.CreateCommand())
            {
                _insertCommand.CommandText = "INSERT OR IGNORE INTO jobs (id,name,state,priority,nextRun,lastRun,runCount,maxAttempts,retryDelay,onSuccessDeleteAfter,onFailureDeleteAfter,expiresAt,correlationId,jobType,jobData,jobHash,lastError,created,modified) " +
                                             "VALUES (@id,@name,@state,@priority,@nextRun,@lastRun,@runCount,@maxAttempts,@retryDelay,@onSuccessDeleteAfter,@onFailureDeleteAfter,@expiresAt,@correlationId,@jobType,@jobData,@jobHash,@lastError,@created,@modified)";
                var _insertParam1 = _insertCommand.CreateParameter();
                _insertParam1.ParameterName = "@id";
                _insertCommand.Parameters.Add(_insertParam1);
                var _insertParam2 = _insertCommand.CreateParameter();
                _insertParam2.ParameterName = "@name";
                _insertCommand.Parameters.Add(_insertParam2);
                var _insertParam3 = _insertCommand.CreateParameter();
                _insertParam3.ParameterName = "@state";
                _insertCommand.Parameters.Add(_insertParam3);
                var _insertParam4 = _insertCommand.CreateParameter();
                _insertParam4.ParameterName = "@priority";
                _insertCommand.Parameters.Add(_insertParam4);
                var _insertParam5 = _insertCommand.CreateParameter();
                _insertParam5.ParameterName = "@nextRun";
                _insertCommand.Parameters.Add(_insertParam5);
                var _insertParam6 = _insertCommand.CreateParameter();
                _insertParam6.ParameterName = "@lastRun";
                _insertCommand.Parameters.Add(_insertParam6);
                var _insertParam7 = _insertCommand.CreateParameter();
                _insertParam7.ParameterName = "@runCount";
                _insertCommand.Parameters.Add(_insertParam7);
                var _insertParam8 = _insertCommand.CreateParameter();
                _insertParam8.ParameterName = "@maxAttempts";
                _insertCommand.Parameters.Add(_insertParam8);
                var _insertParam9 = _insertCommand.CreateParameter();
                _insertParam9.ParameterName = "@retryDelay";
                _insertCommand.Parameters.Add(_insertParam9);
                var _insertParam10 = _insertCommand.CreateParameter();
                _insertParam10.ParameterName = "@onSuccessDeleteAfter";
                _insertCommand.Parameters.Add(_insertParam10);
                var _insertParam11 = _insertCommand.CreateParameter();
                _insertParam11.ParameterName = "@onFailureDeleteAfter";
                _insertCommand.Parameters.Add(_insertParam11);
                var _insertParam12 = _insertCommand.CreateParameter();
                _insertParam12.ParameterName = "@expiresAt";
                _insertCommand.Parameters.Add(_insertParam12);
                var _insertParam13 = _insertCommand.CreateParameter();
                _insertParam13.ParameterName = "@correlationId";
                _insertCommand.Parameters.Add(_insertParam13);
                var _insertParam14 = _insertCommand.CreateParameter();
                _insertParam14.ParameterName = "@jobType";
                _insertCommand.Parameters.Add(_insertParam14);
                var _insertParam15 = _insertCommand.CreateParameter();
                _insertParam15.ParameterName = "@jobData";
                _insertCommand.Parameters.Add(_insertParam15);
                var _insertParam16 = _insertCommand.CreateParameter();
                _insertParam16.ParameterName = "@jobHash";
                _insertCommand.Parameters.Add(_insertParam16);
                var _insertParam17 = _insertCommand.CreateParameter();
                _insertParam17.ParameterName = "@lastError";
                _insertCommand.Parameters.Add(_insertParam17);
                var _insertParam18 = _insertCommand.CreateParameter();
                _insertParam18.ParameterName = "@created";
                _insertCommand.Parameters.Add(_insertParam18);
                var _insertParam19 = _insertCommand.CreateParameter();
                _insertParam19.ParameterName = "@modified";
                _insertCommand.Parameters.Add(_insertParam19);
                _insertParam1.Value = item.id.ToByteArray();
                _insertParam2.Value = item.name;
                _insertParam3.Value = item.state;
                _insertParam4.Value = item.priority;
                _insertParam5.Value = item.nextRun.milliseconds;
                _insertParam6.Value = item.lastRun == null ? (object)DBNull.Value : item.lastRun?.milliseconds;
                _insertParam7.Value = item.runCount;
                _insertParam8.Value = item.maxAttempts;
                _insertParam9.Value = item.retryDelay;
                _insertParam10.Value = item.onSuccessDeleteAfter;
                _insertParam11.Value = item.onFailureDeleteAfter;
                _insertParam12.Value = item.expiresAt == null ? (object)DBNull.Value : item.expiresAt?.milliseconds;
                _insertParam13.Value = item.correlationId;
                _insertParam14.Value = item.jobType;
                _insertParam15.Value = item.jobData ?? (object)DBNull.Value;
                _insertParam16.Value = item.jobHash ?? (object)DBNull.Value;
                _insertParam17.Value = item.lastError ?? (object)DBNull.Value;
                var now = UnixTimeUtcUnique.Now();
                _insertParam18.Value = now.uniqueTime;
                item.modified = null;
                _insertParam19.Value = DBNull.Value;
                var count = conn.ExecuteNonQuery(_insertCommand);
                if (count > 0)
                {
                    item.created = now;
                }
                return count;
            } // Using
        }

        public virtual int Upsert(DatabaseConnection conn, JobsRecord item)
        {
            using (var _upsertCommand = _database.CreateCommand())
            {
                _upsertCommand.CommandText = "INSERT INTO jobs (id,name,state,priority,nextRun,lastRun,runCount,maxAttempts,retryDelay,onSuccessDeleteAfter,onFailureDeleteAfter,expiresAt,correlationId,jobType,jobData,jobHash,lastError,created) " +
                                             "VALUES (@id,@name,@state,@priority,@nextRun,@lastRun,@runCount,@maxAttempts,@retryDelay,@onSuccessDeleteAfter,@onFailureDeleteAfter,@expiresAt,@correlationId,@jobType,@jobData,@jobHash,@lastError,@created)"+
                                             "ON CONFLICT (id) DO UPDATE "+
                                             "SET name = @name,state = @state,priority = @priority,nextRun = @nextRun,lastRun = @lastRun,runCount = @runCount,maxAttempts = @maxAttempts,retryDelay = @retryDelay,onSuccessDeleteAfter = @onSuccessDeleteAfter,onFailureDeleteAfter = @onFailureDeleteAfter,expiresAt = @expiresAt,correlationId = @correlationId,jobType = @jobType,jobData = @jobData,jobHash = @jobHash,lastError = @lastError,modified = @modified "+
                                             "RETURNING created, modified;";
                var _upsertParam1 = _upsertCommand.CreateParameter();
                _upsertParam1.ParameterName = "@id";
                _upsertCommand.Parameters.Add(_upsertParam1);
                var _upsertParam2 = _upsertCommand.CreateParameter();
                _upsertParam2.ParameterName = "@name";
                _upsertCommand.Parameters.Add(_upsertParam2);
                var _upsertParam3 = _upsertCommand.CreateParameter();
                _upsertParam3.ParameterName = "@state";
                _upsertCommand.Parameters.Add(_upsertParam3);
                var _upsertParam4 = _upsertCommand.CreateParameter();
                _upsertParam4.ParameterName = "@priority";
                _upsertCommand.Parameters.Add(_upsertParam4);
                var _upsertParam5 = _upsertCommand.CreateParameter();
                _upsertParam5.ParameterName = "@nextRun";
                _upsertCommand.Parameters.Add(_upsertParam5);
                var _upsertParam6 = _upsertCommand.CreateParameter();
                _upsertParam6.ParameterName = "@lastRun";
                _upsertCommand.Parameters.Add(_upsertParam6);
                var _upsertParam7 = _upsertCommand.CreateParameter();
                _upsertParam7.ParameterName = "@runCount";
                _upsertCommand.Parameters.Add(_upsertParam7);
                var _upsertParam8 = _upsertCommand.CreateParameter();
                _upsertParam8.ParameterName = "@maxAttempts";
                _upsertCommand.Parameters.Add(_upsertParam8);
                var _upsertParam9 = _upsertCommand.CreateParameter();
                _upsertParam9.ParameterName = "@retryDelay";
                _upsertCommand.Parameters.Add(_upsertParam9);
                var _upsertParam10 = _upsertCommand.CreateParameter();
                _upsertParam10.ParameterName = "@onSuccessDeleteAfter";
                _upsertCommand.Parameters.Add(_upsertParam10);
                var _upsertParam11 = _upsertCommand.CreateParameter();
                _upsertParam11.ParameterName = "@onFailureDeleteAfter";
                _upsertCommand.Parameters.Add(_upsertParam11);
                var _upsertParam12 = _upsertCommand.CreateParameter();
                _upsertParam12.ParameterName = "@expiresAt";
                _upsertCommand.Parameters.Add(_upsertParam12);
                var _upsertParam13 = _upsertCommand.CreateParameter();
                _upsertParam13.ParameterName = "@correlationId";
                _upsertCommand.Parameters.Add(_upsertParam13);
                var _upsertParam14 = _upsertCommand.CreateParameter();
                _upsertParam14.ParameterName = "@jobType";
                _upsertCommand.Parameters.Add(_upsertParam14);
                var _upsertParam15 = _upsertCommand.CreateParameter();
                _upsertParam15.ParameterName = "@jobData";
                _upsertCommand.Parameters.Add(_upsertParam15);
                var _upsertParam16 = _upsertCommand.CreateParameter();
                _upsertParam16.ParameterName = "@jobHash";
                _upsertCommand.Parameters.Add(_upsertParam16);
                var _upsertParam17 = _upsertCommand.CreateParameter();
                _upsertParam17.ParameterName = "@lastError";
                _upsertCommand.Parameters.Add(_upsertParam17);
                var _upsertParam18 = _upsertCommand.CreateParameter();
                _upsertParam18.ParameterName = "@created";
                _upsertCommand.Parameters.Add(_upsertParam18);
                var _upsertParam19 = _upsertCommand.CreateParameter();
                _upsertParam19.ParameterName = "@modified";
                _upsertCommand.Parameters.Add(_upsertParam19);
                var now = UnixTimeUtcUnique.Now();
                _upsertParam1.Value = item.id.ToByteArray();
                _upsertParam2.Value = item.name;
                _upsertParam3.Value = item.state;
                _upsertParam4.Value = item.priority;
                _upsertParam5.Value = item.nextRun.milliseconds;
                _upsertParam6.Value = item.lastRun == null ? (object)DBNull.Value : item.lastRun?.milliseconds;
                _upsertParam7.Value = item.runCount;
                _upsertParam8.Value = item.maxAttempts;
                _upsertParam9.Value = item.retryDelay;
                _upsertParam10.Value = item.onSuccessDeleteAfter;
                _upsertParam11.Value = item.onFailureDeleteAfter;
                _upsertParam12.Value = item.expiresAt == null ? (object)DBNull.Value : item.expiresAt?.milliseconds;
                _upsertParam13.Value = item.correlationId;
                _upsertParam14.Value = item.jobType;
                _upsertParam15.Value = item.jobData ?? (object)DBNull.Value;
                _upsertParam16.Value = item.jobHash ?? (object)DBNull.Value;
                _upsertParam17.Value = item.lastError ?? (object)DBNull.Value;
                _upsertParam18.Value = now.uniqueTime;
                _upsertParam19.Value = now.uniqueTime;
                using (SqliteDataReader rdr = conn.ExecuteReader(_upsertCommand, System.Data.CommandBehavior.SingleRow))
                {
                   if (rdr.Read())
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
                }
                return 0;
            } // Using
        }

        public virtual int Update(DatabaseConnection conn, JobsRecord item)
        {
            using (var _updateCommand = _database.CreateCommand())
            {
                _updateCommand.CommandText = "UPDATE jobs " +
                                             "SET name = @name,state = @state,priority = @priority,nextRun = @nextRun,lastRun = @lastRun,runCount = @runCount,maxAttempts = @maxAttempts,retryDelay = @retryDelay,onSuccessDeleteAfter = @onSuccessDeleteAfter,onFailureDeleteAfter = @onFailureDeleteAfter,expiresAt = @expiresAt,correlationId = @correlationId,jobType = @jobType,jobData = @jobData,jobHash = @jobHash,lastError = @lastError,modified = @modified "+
                                             "WHERE (id = @id)";
                var _updateParam1 = _updateCommand.CreateParameter();
                _updateParam1.ParameterName = "@id";
                _updateCommand.Parameters.Add(_updateParam1);
                var _updateParam2 = _updateCommand.CreateParameter();
                _updateParam2.ParameterName = "@name";
                _updateCommand.Parameters.Add(_updateParam2);
                var _updateParam3 = _updateCommand.CreateParameter();
                _updateParam3.ParameterName = "@state";
                _updateCommand.Parameters.Add(_updateParam3);
                var _updateParam4 = _updateCommand.CreateParameter();
                _updateParam4.ParameterName = "@priority";
                _updateCommand.Parameters.Add(_updateParam4);
                var _updateParam5 = _updateCommand.CreateParameter();
                _updateParam5.ParameterName = "@nextRun";
                _updateCommand.Parameters.Add(_updateParam5);
                var _updateParam6 = _updateCommand.CreateParameter();
                _updateParam6.ParameterName = "@lastRun";
                _updateCommand.Parameters.Add(_updateParam6);
                var _updateParam7 = _updateCommand.CreateParameter();
                _updateParam7.ParameterName = "@runCount";
                _updateCommand.Parameters.Add(_updateParam7);
                var _updateParam8 = _updateCommand.CreateParameter();
                _updateParam8.ParameterName = "@maxAttempts";
                _updateCommand.Parameters.Add(_updateParam8);
                var _updateParam9 = _updateCommand.CreateParameter();
                _updateParam9.ParameterName = "@retryDelay";
                _updateCommand.Parameters.Add(_updateParam9);
                var _updateParam10 = _updateCommand.CreateParameter();
                _updateParam10.ParameterName = "@onSuccessDeleteAfter";
                _updateCommand.Parameters.Add(_updateParam10);
                var _updateParam11 = _updateCommand.CreateParameter();
                _updateParam11.ParameterName = "@onFailureDeleteAfter";
                _updateCommand.Parameters.Add(_updateParam11);
                var _updateParam12 = _updateCommand.CreateParameter();
                _updateParam12.ParameterName = "@expiresAt";
                _updateCommand.Parameters.Add(_updateParam12);
                var _updateParam13 = _updateCommand.CreateParameter();
                _updateParam13.ParameterName = "@correlationId";
                _updateCommand.Parameters.Add(_updateParam13);
                var _updateParam14 = _updateCommand.CreateParameter();
                _updateParam14.ParameterName = "@jobType";
                _updateCommand.Parameters.Add(_updateParam14);
                var _updateParam15 = _updateCommand.CreateParameter();
                _updateParam15.ParameterName = "@jobData";
                _updateCommand.Parameters.Add(_updateParam15);
                var _updateParam16 = _updateCommand.CreateParameter();
                _updateParam16.ParameterName = "@jobHash";
                _updateCommand.Parameters.Add(_updateParam16);
                var _updateParam17 = _updateCommand.CreateParameter();
                _updateParam17.ParameterName = "@lastError";
                _updateCommand.Parameters.Add(_updateParam17);
                var _updateParam18 = _updateCommand.CreateParameter();
                _updateParam18.ParameterName = "@created";
                _updateCommand.Parameters.Add(_updateParam18);
                var _updateParam19 = _updateCommand.CreateParameter();
                _updateParam19.ParameterName = "@modified";
                _updateCommand.Parameters.Add(_updateParam19);
             var now = UnixTimeUtcUnique.Now();
                _updateParam1.Value = item.id.ToByteArray();
                _updateParam2.Value = item.name;
                _updateParam3.Value = item.state;
                _updateParam4.Value = item.priority;
                _updateParam5.Value = item.nextRun.milliseconds;
                _updateParam6.Value = item.lastRun == null ? (object)DBNull.Value : item.lastRun?.milliseconds;
                _updateParam7.Value = item.runCount;
                _updateParam8.Value = item.maxAttempts;
                _updateParam9.Value = item.retryDelay;
                _updateParam10.Value = item.onSuccessDeleteAfter;
                _updateParam11.Value = item.onFailureDeleteAfter;
                _updateParam12.Value = item.expiresAt == null ? (object)DBNull.Value : item.expiresAt?.milliseconds;
                _updateParam13.Value = item.correlationId;
                _updateParam14.Value = item.jobType;
                _updateParam15.Value = item.jobData ?? (object)DBNull.Value;
                _updateParam16.Value = item.jobHash ?? (object)DBNull.Value;
                _updateParam17.Value = item.lastError ?? (object)DBNull.Value;
                _updateParam18.Value = now.uniqueTime;
                _updateParam19.Value = now.uniqueTime;
                var count = conn.ExecuteNonQuery(_updateCommand);
                if (count > 0)
                {
                     item.modified = now;
                }
                return count;
            } // Using
        }

        public virtual int GetCountDirty(DatabaseConnection conn)
        {
            using (var _getCountCommand = _database.CreateCommand())
            {
                _getCountCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM jobs; PRAGMA read_uncommitted = 0;";
                var count = conn.ExecuteScalar(_getCountCommand);
                if (count == null || count == DBNull.Value || !(count is int || count is long))
                    return -1;
                else
                    return Convert.ToInt32(count);
            }
        }

        public override List<string> GetColumnNames()
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
        public JobsRecord ReadRecordFromReaderAll(SqliteDataReader rdr)
        {
            var result = new List<JobsRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new JobsRecord();

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(0, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in id...");
                item.id = new Guid(_guid);
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

        public int Delete(DatabaseConnection conn, Guid id)
        {
            using (var _delete0Command = _database.CreateCommand())
            {
                _delete0Command.CommandText = "DELETE FROM jobs " +
                                             "WHERE id = @id";
                var _delete0Param1 = _delete0Command.CreateParameter();
                _delete0Param1.ParameterName = "@id";
                _delete0Command.Parameters.Add(_delete0Param1);

                _delete0Param1.Value = id.ToByteArray();
                var count = conn.ExecuteNonQuery(_delete0Command);
                return count;
            } // Using
        }

        public JobsRecord ReadRecordFromReader0(SqliteDataReader rdr, Guid id)
        {
            var result = new List<JobsRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
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

        public JobsRecord Get(DatabaseConnection conn, Guid id)
        {
            using (var _get0Command = _database.CreateCommand())
            {
                _get0Command.CommandText = "SELECT name,state,priority,nextRun,lastRun,runCount,maxAttempts,retryDelay,onSuccessDeleteAfter,onFailureDeleteAfter,expiresAt,correlationId,jobType,jobData,jobHash,lastError,created,modified FROM jobs " +
                                             "WHERE id = @id LIMIT 1;";
                var _get0Param1 = _get0Command.CreateParameter();
                _get0Param1.ParameterName = "@id";
                _get0Command.Parameters.Add(_get0Param1);

                _get0Param1.Value = id.ToByteArray();
                lock (conn._lock)
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_get0Command, System.Data.CommandBehavior.SingleRow))
                    {
                        if (!rdr.Read())
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr, id);
                        return r;
                    } // using
                } // lock
            } // using
        }

    }
}
