using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using Dapper;
using Odin.Core.Storage.RepositoryPattern.Connection.System;
using Odin.Core.Time;

namespace Odin.Core.Storage.RepositoryPattern.Repositories.System
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

    public class TableJobsCRUD(ISystemDbConnectionFactory connectionFactory)
    {
        protected readonly ISystemDbConnectionFactory ConnectionFactory = connectionFactory;

        public async Task  EnsureTableExists(bool dropExisting = false)
        {
            await using var cn = await ConnectionFactory.CreateAsync();

            if (dropExisting)
            {
                await cn.ExecuteAsync("DROP TABLE IF EXISTS jobs;");
            }
            const string sql =
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
            await cn.ExecuteAsync(sql);
        }

        public virtual async Task<int> Insert(JobsRecord item)
        {
            await using var cn = await ConnectionFactory.CreateAsync();
            await using var cmd = cn.CreateCommand();

            cmd.CommandText = "INSERT INTO jobs (id,name,state,priority,nextRun,lastRun,runCount,maxAttempts,retryDelay,onSuccessDeleteAfter,onFailureDeleteAfter,expiresAt,correlationId,jobType,jobData,jobHash,lastError,created,modified) " +
                                         "VALUES ($id,$name,$state,$priority,$nextRun,$lastRun,$runCount,$maxAttempts,$retryDelay,$onSuccessDeleteAfter,$onFailureDeleteAfter,$expiresAt,$correlationId,$jobType,$jobData,$jobHash,$lastError,$created,$modified)";
            var cmd1 = cmd.CreateParameter();
            cmd1.ParameterName = "$id";
            cmd.Parameters.Add(cmd1);
            var cmd2 = cmd.CreateParameter();
            cmd2.ParameterName = "$name";
            cmd.Parameters.Add(cmd2);
            var cmd3 = cmd.CreateParameter();
            cmd3.ParameterName = "$state";
            cmd.Parameters.Add(cmd3);
            var cmd4 = cmd.CreateParameter();
            cmd4.ParameterName = "$priority";
            cmd.Parameters.Add(cmd4);
            var cmd5 = cmd.CreateParameter();
            cmd5.ParameterName = "$nextRun";
            cmd.Parameters.Add(cmd5);
            var cmd6 = cmd.CreateParameter();
            cmd6.ParameterName = "$lastRun";
            cmd.Parameters.Add(cmd6);
            var cmd7 = cmd.CreateParameter();
            cmd7.ParameterName = "$runCount";
            cmd.Parameters.Add(cmd7);
            var cmd8 = cmd.CreateParameter();
            cmd8.ParameterName = "$maxAttempts";
            cmd.Parameters.Add(cmd8);
            var cmd9 = cmd.CreateParameter();
            cmd9.ParameterName = "$retryDelay";
            cmd.Parameters.Add(cmd9);
            var cmd10 = cmd.CreateParameter();
            cmd10.ParameterName = "$onSuccessDeleteAfter";
            cmd.Parameters.Add(cmd10);
            var cmd11 = cmd.CreateParameter();
            cmd11.ParameterName = "$onFailureDeleteAfter";
            cmd.Parameters.Add(cmd11);
            var cmd12 = cmd.CreateParameter();
            cmd12.ParameterName = "$expiresAt";
            cmd.Parameters.Add(cmd12);
            var cmd13 = cmd.CreateParameter();
            cmd13.ParameterName = "$correlationId";
            cmd.Parameters.Add(cmd13);
            var cmd14 = cmd.CreateParameter();
            cmd14.ParameterName = "$jobType";
            cmd.Parameters.Add(cmd14);
            var cmd15 = cmd.CreateParameter();
            cmd15.ParameterName = "$jobData";
            cmd.Parameters.Add(cmd15);
            var cmd16 = cmd.CreateParameter();
            cmd16.ParameterName = "$jobHash";
            cmd.Parameters.Add(cmd16);
            var cmd17 = cmd.CreateParameter();
            cmd17.ParameterName = "$lastError";
            cmd.Parameters.Add(cmd17);
            var cmd18 = cmd.CreateParameter();
            cmd18.ParameterName = "$created";
            cmd.Parameters.Add(cmd18);
            var cmd19 = cmd.CreateParameter();
            cmd19.ParameterName = "$modified";
            cmd.Parameters.Add(cmd19);

            cmd1.Value = item.id.ToByteArray();
            cmd2.Value = item.name;
            cmd3.Value = item.state;
            cmd4.Value = item.priority;
            cmd5.Value = item.nextRun.milliseconds;
            cmd6.Value = item.lastRun == null ? (object)DBNull.Value : item.lastRun?.milliseconds;
            cmd7.Value = item.runCount;
            cmd8.Value = item.maxAttempts;
            cmd9.Value = item.retryDelay;
            cmd10.Value = item.onSuccessDeleteAfter;
            cmd11.Value = item.onFailureDeleteAfter;
            cmd12.Value = item.expiresAt == null ? (object)DBNull.Value : item.expiresAt?.milliseconds;
            cmd13.Value = item.correlationId;
            cmd14.Value = item.jobType;
            cmd15.Value = item.jobData ?? (object)DBNull.Value;
            cmd16.Value = item.jobHash ?? (object)DBNull.Value;
            cmd17.Value = item.lastError ?? (object)DBNull.Value;
            var now = UnixTimeUtcUnique.Now();
            cmd18.Value = now.uniqueTime;
            item.modified = null;
            cmd19.Value = DBNull.Value;
            var count = await cmd.ExecuteNonQueryAsync();
            if (count > 0)
            {
                item.created = now;
            }
            return count;
        }

        public virtual async Task<bool> TryInsert(JobsRecord item)
        {
            await using var cn = await ConnectionFactory.CreateAsync();
            await using var cmd = cn.CreateCommand();

            cmd.CommandText = "INSERT OR IGNORE INTO jobs (id,name,state,priority,nextRun,lastRun,runCount,maxAttempts,retryDelay,onSuccessDeleteAfter,onFailureDeleteAfter,expiresAt,correlationId,jobType,jobData,jobHash,lastError,created,modified) " +
                                         "VALUES (@id,@name,@state,@priority,@nextRun,@lastRun,@runCount,@maxAttempts,@retryDelay,@onSuccessDeleteAfter,@onFailureDeleteAfter,@expiresAt,@correlationId,@jobType,@jobData,@jobHash,@lastError,@created,@modified)";
            var cmd1 = cmd.CreateParameter();
            cmd1.ParameterName = "@id";
            cmd.Parameters.Add(cmd1);
            var cmd2 = cmd.CreateParameter();
            cmd2.ParameterName = "@name";
            cmd.Parameters.Add(cmd2);
            var cmd3 = cmd.CreateParameter();
            cmd3.ParameterName = "@state";
            cmd.Parameters.Add(cmd3);
            var cmd4 = cmd.CreateParameter();
            cmd4.ParameterName = "@priority";
            cmd.Parameters.Add(cmd4);
            var cmd5 = cmd.CreateParameter();
            cmd5.ParameterName = "@nextRun";
            cmd.Parameters.Add(cmd5);
            var cmd6 = cmd.CreateParameter();
            cmd6.ParameterName = "@lastRun";
            cmd.Parameters.Add(cmd6);
            var cmd7 = cmd.CreateParameter();
            cmd7.ParameterName = "@runCount";
            cmd.Parameters.Add(cmd7);
            var cmd8 = cmd.CreateParameter();
            cmd8.ParameterName = "@maxAttempts";
            cmd.Parameters.Add(cmd8);
            var cmd9 = cmd.CreateParameter();
            cmd9.ParameterName = "@retryDelay";
            cmd.Parameters.Add(cmd9);
            var cmd10 = cmd.CreateParameter();
            cmd10.ParameterName = "@onSuccessDeleteAfter";
            cmd.Parameters.Add(cmd10);
            var cmd11 = cmd.CreateParameter();
            cmd11.ParameterName = "@onFailureDeleteAfter";
            cmd.Parameters.Add(cmd11);
            var cmd12 = cmd.CreateParameter();
            cmd12.ParameterName = "@expiresAt";
            cmd.Parameters.Add(cmd12);
            var cmd13 = cmd.CreateParameter();
            cmd13.ParameterName = "@correlationId";
            cmd.Parameters.Add(cmd13);
            var cmd14 = cmd.CreateParameter();
            cmd14.ParameterName = "@jobType";
            cmd.Parameters.Add(cmd14);
            var cmd15 = cmd.CreateParameter();
            cmd15.ParameterName = "@jobData";
            cmd.Parameters.Add(cmd15);
            var cmd16 = cmd.CreateParameter();
            cmd16.ParameterName = "@jobHash";
            cmd.Parameters.Add(cmd16);
            var cmd17 = cmd.CreateParameter();
            cmd17.ParameterName = "@lastError";
            cmd.Parameters.Add(cmd17);
            var cmd18 = cmd.CreateParameter();
            cmd18.ParameterName = "@created";
            cmd.Parameters.Add(cmd18);
            var cmd19 = cmd.CreateParameter();
            cmd19.ParameterName = "@modified";
            cmd.Parameters.Add(cmd19);

            cmd1.Value = item.id.ToByteArray();
            cmd2.Value = item.name;
            cmd3.Value = item.state;
            cmd4.Value = item.priority;
            cmd5.Value = item.nextRun.milliseconds;
            cmd6.Value = item.lastRun == null ? (object)DBNull.Value : item.lastRun?.milliseconds;
            cmd7.Value = item.runCount;
            cmd8.Value = item.maxAttempts;
            cmd9.Value = item.retryDelay;
            cmd10.Value = item.onSuccessDeleteAfter;
            cmd11.Value = item.onFailureDeleteAfter;
            cmd12.Value = item.expiresAt == null ? (object)DBNull.Value : item.expiresAt?.milliseconds;
            cmd13.Value = item.correlationId;
            cmd14.Value = item.jobType;
            cmd15.Value = item.jobData ?? (object)DBNull.Value;
            cmd16.Value = item.jobHash ?? (object)DBNull.Value;
            cmd17.Value = item.lastError ?? (object)DBNull.Value;
            var now = UnixTimeUtcUnique.Now();
            cmd18.Value = now.uniqueTime;
            item.modified = null;
            cmd19.Value = DBNull.Value;
            var count = await cmd.ExecuteNonQueryAsync();
            if (count > 0)
            {
                item.created = now;
            }
            return count > 0;
        }

        public virtual async Task<int> Upsert(JobsRecord item)
        {
            await using var cn = await ConnectionFactory.CreateAsync();
            await using var cmd = cn.CreateCommand();

            cmd.CommandText = "INSERT INTO jobs (id,name,state,priority,nextRun,lastRun,runCount,maxAttempts,retryDelay,onSuccessDeleteAfter,onFailureDeleteAfter,expiresAt,correlationId,jobType,jobData,jobHash,lastError,created) " +
                                         "VALUES ($id,$name,$state,$priority,$nextRun,$lastRun,$runCount,$maxAttempts,$retryDelay,$onSuccessDeleteAfter,$onFailureDeleteAfter,$expiresAt,$correlationId,$jobType,$jobData,$jobHash,$lastError,$created)"+
                                         "ON CONFLICT (id) DO UPDATE "+
                                         "SET name = $name,state = $state,priority = $priority,nextRun = $nextRun,lastRun = $lastRun,runCount = $runCount,maxAttempts = $maxAttempts,retryDelay = $retryDelay,onSuccessDeleteAfter = $onSuccessDeleteAfter,onFailureDeleteAfter = $onFailureDeleteAfter,expiresAt = $expiresAt,correlationId = $correlationId,jobType = $jobType,jobData = $jobData,jobHash = $jobHash,lastError = $lastError,modified = $modified "+
                                         "RETURNING created, modified;";
            var cmd1 = cmd.CreateParameter();
            cmd1.ParameterName = "$id";
            cmd.Parameters.Add(cmd1);
            var cmd2 = cmd.CreateParameter();
            cmd2.ParameterName = "$name";
            cmd.Parameters.Add(cmd2);
            var cmd3 = cmd.CreateParameter();
            cmd3.ParameterName = "$state";
            cmd.Parameters.Add(cmd3);
            var cmd4 = cmd.CreateParameter();
            cmd4.ParameterName = "$priority";
            cmd.Parameters.Add(cmd4);
            var cmd5 = cmd.CreateParameter();
            cmd5.ParameterName = "$nextRun";
            cmd.Parameters.Add(cmd5);
            var cmd6 = cmd.CreateParameter();
            cmd6.ParameterName = "$lastRun";
            cmd.Parameters.Add(cmd6);
            var cmd7 = cmd.CreateParameter();
            cmd7.ParameterName = "$runCount";
            cmd.Parameters.Add(cmd7);
            var cmd8 = cmd.CreateParameter();
            cmd8.ParameterName = "$maxAttempts";
            cmd.Parameters.Add(cmd8);
            var cmd9 = cmd.CreateParameter();
            cmd9.ParameterName = "$retryDelay";
            cmd.Parameters.Add(cmd9);
            var cmd10 = cmd.CreateParameter();
            cmd10.ParameterName = "$onSuccessDeleteAfter";
            cmd.Parameters.Add(cmd10);
            var cmd11 = cmd.CreateParameter();
            cmd11.ParameterName = "$onFailureDeleteAfter";
            cmd.Parameters.Add(cmd11);
            var cmd12 = cmd.CreateParameter();
            cmd12.ParameterName = "$expiresAt";
            cmd.Parameters.Add(cmd12);
            var cmd13 = cmd.CreateParameter();
            cmd13.ParameterName = "$correlationId";
            cmd.Parameters.Add(cmd13);
            var cmd14 = cmd.CreateParameter();
            cmd14.ParameterName = "$jobType";
            cmd.Parameters.Add(cmd14);
            var cmd15 = cmd.CreateParameter();
            cmd15.ParameterName = "$jobData";
            cmd.Parameters.Add(cmd15);
            var cmd16 = cmd.CreateParameter();
            cmd16.ParameterName = "$jobHash";
            cmd.Parameters.Add(cmd16);
            var cmd17 = cmd.CreateParameter();
            cmd17.ParameterName = "$lastError";
            cmd.Parameters.Add(cmd17);
            var cmd18 = cmd.CreateParameter();
            cmd18.ParameterName = "$created";
            cmd.Parameters.Add(cmd18);
            var cmd19 = cmd.CreateParameter();
            cmd19.ParameterName = "$modified";
            cmd.Parameters.Add(cmd19);

            var now = UnixTimeUtcUnique.Now();
            cmd1.Value = item.id.ToByteArray();
            cmd2.Value = item.name;
            cmd3.Value = item.state;
            cmd4.Value = item.priority;
            cmd5.Value = item.nextRun.milliseconds;
            cmd6.Value = item.lastRun == null ? (object)DBNull.Value : item.lastRun?.milliseconds;
            cmd7.Value = item.runCount;
            cmd8.Value = item.maxAttempts;
            cmd9.Value = item.retryDelay;
            cmd10.Value = item.onSuccessDeleteAfter;
            cmd11.Value = item.onFailureDeleteAfter;
            cmd12.Value = item.expiresAt == null ? (object)DBNull.Value : item.expiresAt?.milliseconds;
            cmd13.Value = item.correlationId;
            cmd14.Value = item.jobType;
            cmd15.Value = item.jobData ?? (object)DBNull.Value;
            cmd16.Value = item.jobHash ?? (object)DBNull.Value;
            cmd17.Value = item.lastError ?? (object)DBNull.Value;
            cmd18.Value = now.uniqueTime;
            cmd19.Value = now.uniqueTime;
            await using (var rdr = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow))
            {
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
            }
            return 0;
        }

        public virtual async Task<int> Update(JobsRecord item)
        {
            await using var cn = await ConnectionFactory.CreateAsync();
            await using var cmd = cn.CreateCommand();

            cmd.CommandText =
                "UPDATE jobs " +
                "SET name = $name,state = $state,priority = $priority,nextRun = $nextRun,lastRun = $lastRun,runCount = $runCount,maxAttempts = $maxAttempts,retryDelay = $retryDelay,onSuccessDeleteAfter = $onSuccessDeleteAfter,onFailureDeleteAfter = $onFailureDeleteAfter,expiresAt = $expiresAt,correlationId = $correlationId,jobType = $jobType,jobData = $jobData,jobHash = $jobHash,lastError = $lastError,modified = $modified "+
                "WHERE (id = $id)";
            var cmd1 = cmd.CreateParameter();
            cmd1.ParameterName = "$id";
            cmd.Parameters.Add(cmd1);
            var cmd2 = cmd.CreateParameter();
            cmd2.ParameterName = "$name";
            cmd.Parameters.Add(cmd2);
            var cmd3 = cmd.CreateParameter();
            cmd3.ParameterName = "$state";
            cmd.Parameters.Add(cmd3);
            var cmd4 = cmd.CreateParameter();
            cmd4.ParameterName = "$priority";
            cmd.Parameters.Add(cmd4);
            var cmd5 = cmd.CreateParameter();
            cmd5.ParameterName = "$nextRun";
            cmd.Parameters.Add(cmd5);
            var cmd6 = cmd.CreateParameter();
            cmd6.ParameterName = "$lastRun";
            cmd.Parameters.Add(cmd6);
            var cmd7 = cmd.CreateParameter();
            cmd7.ParameterName = "$runCount";
            cmd.Parameters.Add(cmd7);
            var cmd8 = cmd.CreateParameter();
            cmd8.ParameterName = "$maxAttempts";
            cmd.Parameters.Add(cmd8);
            var cmd9 = cmd.CreateParameter();
            cmd9.ParameterName = "$retryDelay";
            cmd.Parameters.Add(cmd9);
            var cmd10 = cmd.CreateParameter();
            cmd10.ParameterName = "$onSuccessDeleteAfter";
            cmd.Parameters.Add(cmd10);
            var cmd11 = cmd.CreateParameter();
            cmd11.ParameterName = "$onFailureDeleteAfter";
            cmd.Parameters.Add(cmd11);
            var cmd12 = cmd.CreateParameter();
            cmd12.ParameterName = "$expiresAt";
            cmd.Parameters.Add(cmd12);
            var cmd13 = cmd.CreateParameter();
            cmd13.ParameterName = "$correlationId";
            cmd.Parameters.Add(cmd13);
            var cmd14 = cmd.CreateParameter();
            cmd14.ParameterName = "$jobType";
            cmd.Parameters.Add(cmd14);
            var cmd15 = cmd.CreateParameter();
            cmd15.ParameterName = "$jobData";
            cmd.Parameters.Add(cmd15);
            var cmd16 = cmd.CreateParameter();
            cmd16.ParameterName = "$jobHash";
            cmd.Parameters.Add(cmd16);
            var cmd17 = cmd.CreateParameter();
            cmd17.ParameterName = "$lastError";
            cmd.Parameters.Add(cmd17);
            var cmd18 = cmd.CreateParameter();
            cmd18.ParameterName = "$created";
            cmd.Parameters.Add(cmd18);
            var cmd19 = cmd.CreateParameter();
            cmd19.ParameterName = "$modified";
            cmd.Parameters.Add(cmd19);
            var now = UnixTimeUtcUnique.Now();
            cmd1.Value = item.id.ToByteArray();
            cmd2.Value = item.name;
            cmd3.Value = item.state;
            cmd4.Value = item.priority;
            cmd5.Value = item.nextRun.milliseconds;
            cmd6.Value = item.lastRun == null ? (object)DBNull.Value : item.lastRun?.milliseconds;
            cmd7.Value = item.runCount;
            cmd8.Value = item.maxAttempts;
            cmd9.Value = item.retryDelay;
            cmd10.Value = item.onSuccessDeleteAfter;
            cmd11.Value = item.onFailureDeleteAfter;
            cmd12.Value = item.expiresAt == null ? (object)DBNull.Value : item.expiresAt?.milliseconds;
            cmd13.Value = item.correlationId;
            cmd14.Value = item.jobType;
            cmd15.Value = item.jobData ?? (object)DBNull.Value;
            cmd16.Value = item.jobHash ?? (object)DBNull.Value;
            cmd17.Value = item.lastError ?? (object)DBNull.Value;
            cmd18.Value = now.uniqueTime;
            cmd19.Value = now.uniqueTime;
            var count = await cmd.ExecuteNonQueryAsync();
            if (count > 0)
            {
                 item.modified = now;
            }
            return count;
        }

        // SELECT id,name,state,priority,nextRun,lastRun,runCount,maxAttempts,retryDelay,onSuccessDeleteAfter,onFailureDeleteAfter,expiresAt,correlationId,jobType,jobData,jobHash,lastError,created,modified
        public JobsRecord ReadRecordFromReaderAll(DbDataReader rdr)
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

        public async Task<int> Delete(Guid id)
        {
            await using var cn = await ConnectionFactory.CreateAsync();
            await using var cmd = cn.CreateCommand();

            cmd.CommandText = "DELETE FROM jobs " +
                                         "WHERE id = $id";
            var cmd1 = cmd.CreateParameter();
            cmd1.ParameterName = "$id";
            cmd.Parameters.Add(cmd1);

            cmd1.Value = id.ToByteArray();
            var count = await cmd.ExecuteNonQueryAsync();
            return count;
        }

        public JobsRecord ReadRecordFromReader0(DbDataReader rdr, Guid id)
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

        public async Task<JobsRecord> Get(Guid id)
        {
            await using var cn = await ConnectionFactory.CreateAsync();
            await using var cmd = cn.CreateCommand();

            cmd.CommandText = "SELECT name,state,priority,nextRun,lastRun,runCount,maxAttempts,retryDelay,onSuccessDeleteAfter,onFailureDeleteAfter,expiresAt,correlationId,jobType,jobData,jobHash,lastError,created,modified FROM jobs " +
                                         "WHERE id = $id LIMIT 1;";
            var cmd1 = cmd.CreateParameter();
            cmd1.ParameterName = "$id";
            cmd.Parameters.Add(cmd1);
            cmd1.Value = id.ToByteArray();

            await using var rdr = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow);
            if (await rdr.ReadAsync() == false)
            {
                return null;
            }

            var r = ReadRecordFromReader0(rdr, id);
            return r;
        }

    }
}
