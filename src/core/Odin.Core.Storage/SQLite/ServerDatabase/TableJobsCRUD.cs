using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;
using Odin.Core.Identity;

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
        private Int32 _maxRuns;
        public Int32 maxRuns
        {
           get {
                   return _maxRuns;
               }
           set {
                  _maxRuns = value;
               }
        }
        private UnixTimeUtc _onSuccessDeleteAfter;
        public UnixTimeUtc onSuccessDeleteAfter
        {
           get {
                   return _onSuccessDeleteAfter;
               }
           set {
                  _onSuccessDeleteAfter = value;
               }
        }
        private UnixTimeUtc _onFailDeleteAfter;
        public UnixTimeUtc onFailDeleteAfter
        {
           get {
                   return _onFailDeleteAfter;
               }
           set {
                  _onFailDeleteAfter = value;
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
        private string _inputType;
        public string inputType
        {
           get {
                   return _inputType;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 0) throw new Exception("Too short");
                    if (value?.Length > 65535) throw new Exception("Too long");
                  _inputType = value;
               }
        }
        private string _inputData;
        public string inputData
        {
           get {
                   return _inputData;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 0) throw new Exception("Too short");
                    if (value?.Length > 65535) throw new Exception("Too long");
                  _inputData = value;
               }
        }
        private string _inputHash;
        public string inputHash
        {
           get {
                   return _inputHash;
               }
           set {
                    if (value?.Length < 0) throw new Exception("Too short");
                    if (value?.Length > 65535) throw new Exception("Too long");
                  _inputHash = value;
               }
        }
        private string _outputType;
        public string outputType
        {
           get {
                   return _outputType;
               }
           set {
                    if (value?.Length < 0) throw new Exception("Too short");
                    if (value?.Length > 65535) throw new Exception("Too long");
                  _outputType = value;
               }
        }
        private string _outputData;
        public string outputData
        {
           get {
                   return _outputData;
               }
           set {
                    if (value?.Length < 0) throw new Exception("Too short");
                    if (value?.Length > 65535) throw new Exception("Too long");
                  _outputData = value;
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

        public TableJobsCRUD(ServerDatabase db, CacheHelper cache) : base(db)
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
                     +"state INT NOT NULL, "
                     +"priority INT NOT NULL, "
                     +"nextRun INT NOT NULL, "
                     +"lastRun INT , "
                     +"runCount INT NOT NULL, "
                     +"maxRuns INT NOT NULL, "
                     +"onSuccessDeleteAfter INT NOT NULL, "
                     +"onFailDeleteAfter INT NOT NULL, "
                     +"correlationId STRING NOT NULL, "
                     +"inputType STRING NOT NULL, "
                     +"inputData STRING NOT NULL, "
                     +"inputHash STRING  UNIQUE, "
                     +"outputType STRING , "
                     +"outputData STRING , "
                     +"created INT NOT NULL, "
                     +"modified INT  "
                     +", PRIMARY KEY (id)"
                     +");"
                     +"CREATE INDEX IF NOT EXISTS Idx0TableJobsCRUD ON jobs(state);"
                     +"CREATE INDEX IF NOT EXISTS Idx1TableJobsCRUD ON jobs(priority);"
                     +"CREATE INDEX IF NOT EXISTS Idx2TableJobsCRUD ON jobs(nextRun);"
                     +"CREATE INDEX IF NOT EXISTS Idx3TableJobsCRUD ON jobs(inputHash);"
                     ;
                    conn.ExecuteNonQuery(cmd);
            }
        }

        public virtual int Insert(DatabaseConnection conn, JobsRecord item)
        {
                using (var _insertCommand = _database.CreateCommand())
                {
                    _insertCommand.CommandText = "INSERT INTO jobs (id,state,priority,nextRun,lastRun,runCount,maxRuns,onSuccessDeleteAfter,onFailDeleteAfter,correlationId,inputType,inputData,inputHash,outputType,outputData,created,modified) " +
                                                 "VALUES ($id,$state,$priority,$nextRun,$lastRun,$runCount,$maxRuns,$onSuccessDeleteAfter,$onFailDeleteAfter,$correlationId,$inputType,$inputData,$inputHash,$outputType,$outputData,$created,$modified)";
                    var _insertParam1 = _insertCommand.CreateParameter();
                    _insertParam1.ParameterName = "$id";
                    _insertCommand.Parameters.Add(_insertParam1);
                    var _insertParam2 = _insertCommand.CreateParameter();
                    _insertParam2.ParameterName = "$state";
                    _insertCommand.Parameters.Add(_insertParam2);
                    var _insertParam3 = _insertCommand.CreateParameter();
                    _insertParam3.ParameterName = "$priority";
                    _insertCommand.Parameters.Add(_insertParam3);
                    var _insertParam4 = _insertCommand.CreateParameter();
                    _insertParam4.ParameterName = "$nextRun";
                    _insertCommand.Parameters.Add(_insertParam4);
                    var _insertParam5 = _insertCommand.CreateParameter();
                    _insertParam5.ParameterName = "$lastRun";
                    _insertCommand.Parameters.Add(_insertParam5);
                    var _insertParam6 = _insertCommand.CreateParameter();
                    _insertParam6.ParameterName = "$runCount";
                    _insertCommand.Parameters.Add(_insertParam6);
                    var _insertParam7 = _insertCommand.CreateParameter();
                    _insertParam7.ParameterName = "$maxRuns";
                    _insertCommand.Parameters.Add(_insertParam7);
                    var _insertParam8 = _insertCommand.CreateParameter();
                    _insertParam8.ParameterName = "$onSuccessDeleteAfter";
                    _insertCommand.Parameters.Add(_insertParam8);
                    var _insertParam9 = _insertCommand.CreateParameter();
                    _insertParam9.ParameterName = "$onFailDeleteAfter";
                    _insertCommand.Parameters.Add(_insertParam9);
                    var _insertParam10 = _insertCommand.CreateParameter();
                    _insertParam10.ParameterName = "$correlationId";
                    _insertCommand.Parameters.Add(_insertParam10);
                    var _insertParam11 = _insertCommand.CreateParameter();
                    _insertParam11.ParameterName = "$inputType";
                    _insertCommand.Parameters.Add(_insertParam11);
                    var _insertParam12 = _insertCommand.CreateParameter();
                    _insertParam12.ParameterName = "$inputData";
                    _insertCommand.Parameters.Add(_insertParam12);
                    var _insertParam13 = _insertCommand.CreateParameter();
                    _insertParam13.ParameterName = "$inputHash";
                    _insertCommand.Parameters.Add(_insertParam13);
                    var _insertParam14 = _insertCommand.CreateParameter();
                    _insertParam14.ParameterName = "$outputType";
                    _insertCommand.Parameters.Add(_insertParam14);
                    var _insertParam15 = _insertCommand.CreateParameter();
                    _insertParam15.ParameterName = "$outputData";
                    _insertCommand.Parameters.Add(_insertParam15);
                    var _insertParam16 = _insertCommand.CreateParameter();
                    _insertParam16.ParameterName = "$created";
                    _insertCommand.Parameters.Add(_insertParam16);
                    var _insertParam17 = _insertCommand.CreateParameter();
                    _insertParam17.ParameterName = "$modified";
                    _insertCommand.Parameters.Add(_insertParam17);
                _insertParam1.Value = item.id.ToByteArray();
                _insertParam2.Value = item.state;
                _insertParam3.Value = item.priority;
                _insertParam4.Value = item.nextRun.milliseconds;
                _insertParam5.Value = item.lastRun == null ? (object)DBNull.Value : item.lastRun?.milliseconds;
                _insertParam6.Value = item.runCount;
                _insertParam7.Value = item.maxRuns;
                _insertParam8.Value = item.onSuccessDeleteAfter.milliseconds;
                _insertParam9.Value = item.onFailDeleteAfter.milliseconds;
                _insertParam10.Value = item.correlationId;
                _insertParam11.Value = item.inputType;
                _insertParam12.Value = item.inputData;
                _insertParam13.Value = item.inputHash ?? (object)DBNull.Value;
                _insertParam14.Value = item.outputType ?? (object)DBNull.Value;
                _insertParam15.Value = item.outputData ?? (object)DBNull.Value;
                var now = UnixTimeUtcUnique.Now();
                _insertParam16.Value = now.uniqueTime;
                item.modified = null;
                _insertParam17.Value = DBNull.Value;
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
                _insertCommand.CommandText = "INSERT OR IGNORE INTO jobs (id,state,priority,nextRun,lastRun,runCount,maxRuns,onSuccessDeleteAfter,onFailDeleteAfter,correlationId,inputType,inputData,inputHash,outputType,outputData,created,modified) " +
                                             "VALUES (@id,@state,@priority,@nextRun,@lastRun,@runCount,@maxRuns,@onSuccessDeleteAfter,@onFailDeleteAfter,@correlationId,@inputType,@inputData,@inputHash,@outputType,@outputData,@created,@modified)";
                var _insertParam1 = _insertCommand.CreateParameter();
                _insertParam1.ParameterName = "@id";
                _insertCommand.Parameters.Add(_insertParam1);
                var _insertParam2 = _insertCommand.CreateParameter();
                _insertParam2.ParameterName = "@state";
                _insertCommand.Parameters.Add(_insertParam2);
                var _insertParam3 = _insertCommand.CreateParameter();
                _insertParam3.ParameterName = "@priority";
                _insertCommand.Parameters.Add(_insertParam3);
                var _insertParam4 = _insertCommand.CreateParameter();
                _insertParam4.ParameterName = "@nextRun";
                _insertCommand.Parameters.Add(_insertParam4);
                var _insertParam5 = _insertCommand.CreateParameter();
                _insertParam5.ParameterName = "@lastRun";
                _insertCommand.Parameters.Add(_insertParam5);
                var _insertParam6 = _insertCommand.CreateParameter();
                _insertParam6.ParameterName = "@runCount";
                _insertCommand.Parameters.Add(_insertParam6);
                var _insertParam7 = _insertCommand.CreateParameter();
                _insertParam7.ParameterName = "@maxRuns";
                _insertCommand.Parameters.Add(_insertParam7);
                var _insertParam8 = _insertCommand.CreateParameter();
                _insertParam8.ParameterName = "@onSuccessDeleteAfter";
                _insertCommand.Parameters.Add(_insertParam8);
                var _insertParam9 = _insertCommand.CreateParameter();
                _insertParam9.ParameterName = "@onFailDeleteAfter";
                _insertCommand.Parameters.Add(_insertParam9);
                var _insertParam10 = _insertCommand.CreateParameter();
                _insertParam10.ParameterName = "@correlationId";
                _insertCommand.Parameters.Add(_insertParam10);
                var _insertParam11 = _insertCommand.CreateParameter();
                _insertParam11.ParameterName = "@inputType";
                _insertCommand.Parameters.Add(_insertParam11);
                var _insertParam12 = _insertCommand.CreateParameter();
                _insertParam12.ParameterName = "@inputData";
                _insertCommand.Parameters.Add(_insertParam12);
                var _insertParam13 = _insertCommand.CreateParameter();
                _insertParam13.ParameterName = "@inputHash";
                _insertCommand.Parameters.Add(_insertParam13);
                var _insertParam14 = _insertCommand.CreateParameter();
                _insertParam14.ParameterName = "@outputType";
                _insertCommand.Parameters.Add(_insertParam14);
                var _insertParam15 = _insertCommand.CreateParameter();
                _insertParam15.ParameterName = "@outputData";
                _insertCommand.Parameters.Add(_insertParam15);
                var _insertParam16 = _insertCommand.CreateParameter();
                _insertParam16.ParameterName = "@created";
                _insertCommand.Parameters.Add(_insertParam16);
                var _insertParam17 = _insertCommand.CreateParameter();
                _insertParam17.ParameterName = "@modified";
                _insertCommand.Parameters.Add(_insertParam17);
                _insertParam1.Value = item.id.ToByteArray();
                _insertParam2.Value = item.state;
                _insertParam3.Value = item.priority;
                _insertParam4.Value = item.nextRun.milliseconds;
                _insertParam5.Value = item.lastRun == null ? (object)DBNull.Value : item.lastRun?.milliseconds;
                _insertParam6.Value = item.runCount;
                _insertParam7.Value = item.maxRuns;
                _insertParam8.Value = item.onSuccessDeleteAfter.milliseconds;
                _insertParam9.Value = item.onFailDeleteAfter.milliseconds;
                _insertParam10.Value = item.correlationId;
                _insertParam11.Value = item.inputType;
                _insertParam12.Value = item.inputData;
                _insertParam13.Value = item.inputHash ?? (object)DBNull.Value;
                _insertParam14.Value = item.outputType ?? (object)DBNull.Value;
                _insertParam15.Value = item.outputData ?? (object)DBNull.Value;
                var now = UnixTimeUtcUnique.Now();
                _insertParam16.Value = now.uniqueTime;
                item.modified = null;
                _insertParam17.Value = DBNull.Value;
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
                    _upsertCommand.CommandText = "INSERT INTO jobs (id,state,priority,nextRun,lastRun,runCount,maxRuns,onSuccessDeleteAfter,onFailDeleteAfter,correlationId,inputType,inputData,inputHash,outputType,outputData,created) " +
                                                 "VALUES ($id,$state,$priority,$nextRun,$lastRun,$runCount,$maxRuns,$onSuccessDeleteAfter,$onFailDeleteAfter,$correlationId,$inputType,$inputData,$inputHash,$outputType,$outputData,$created)"+
                                                 "ON CONFLICT (id) DO UPDATE "+
                                                 "SET state = $state,priority = $priority,nextRun = $nextRun,lastRun = $lastRun,runCount = $runCount,maxRuns = $maxRuns,onSuccessDeleteAfter = $onSuccessDeleteAfter,onFailDeleteAfter = $onFailDeleteAfter,correlationId = $correlationId,inputType = $inputType,inputData = $inputData,inputHash = $inputHash,outputType = $outputType,outputData = $outputData,modified = $modified "+
                                                 "RETURNING created, modified;";
                    var _upsertParam1 = _upsertCommand.CreateParameter();
                    _upsertParam1.ParameterName = "$id";
                    _upsertCommand.Parameters.Add(_upsertParam1);
                    var _upsertParam2 = _upsertCommand.CreateParameter();
                    _upsertParam2.ParameterName = "$state";
                    _upsertCommand.Parameters.Add(_upsertParam2);
                    var _upsertParam3 = _upsertCommand.CreateParameter();
                    _upsertParam3.ParameterName = "$priority";
                    _upsertCommand.Parameters.Add(_upsertParam3);
                    var _upsertParam4 = _upsertCommand.CreateParameter();
                    _upsertParam4.ParameterName = "$nextRun";
                    _upsertCommand.Parameters.Add(_upsertParam4);
                    var _upsertParam5 = _upsertCommand.CreateParameter();
                    _upsertParam5.ParameterName = "$lastRun";
                    _upsertCommand.Parameters.Add(_upsertParam5);
                    var _upsertParam6 = _upsertCommand.CreateParameter();
                    _upsertParam6.ParameterName = "$runCount";
                    _upsertCommand.Parameters.Add(_upsertParam6);
                    var _upsertParam7 = _upsertCommand.CreateParameter();
                    _upsertParam7.ParameterName = "$maxRuns";
                    _upsertCommand.Parameters.Add(_upsertParam7);
                    var _upsertParam8 = _upsertCommand.CreateParameter();
                    _upsertParam8.ParameterName = "$onSuccessDeleteAfter";
                    _upsertCommand.Parameters.Add(_upsertParam8);
                    var _upsertParam9 = _upsertCommand.CreateParameter();
                    _upsertParam9.ParameterName = "$onFailDeleteAfter";
                    _upsertCommand.Parameters.Add(_upsertParam9);
                    var _upsertParam10 = _upsertCommand.CreateParameter();
                    _upsertParam10.ParameterName = "$correlationId";
                    _upsertCommand.Parameters.Add(_upsertParam10);
                    var _upsertParam11 = _upsertCommand.CreateParameter();
                    _upsertParam11.ParameterName = "$inputType";
                    _upsertCommand.Parameters.Add(_upsertParam11);
                    var _upsertParam12 = _upsertCommand.CreateParameter();
                    _upsertParam12.ParameterName = "$inputData";
                    _upsertCommand.Parameters.Add(_upsertParam12);
                    var _upsertParam13 = _upsertCommand.CreateParameter();
                    _upsertParam13.ParameterName = "$inputHash";
                    _upsertCommand.Parameters.Add(_upsertParam13);
                    var _upsertParam14 = _upsertCommand.CreateParameter();
                    _upsertParam14.ParameterName = "$outputType";
                    _upsertCommand.Parameters.Add(_upsertParam14);
                    var _upsertParam15 = _upsertCommand.CreateParameter();
                    _upsertParam15.ParameterName = "$outputData";
                    _upsertCommand.Parameters.Add(_upsertParam15);
                    var _upsertParam16 = _upsertCommand.CreateParameter();
                    _upsertParam16.ParameterName = "$created";
                    _upsertCommand.Parameters.Add(_upsertParam16);
                    var _upsertParam17 = _upsertCommand.CreateParameter();
                    _upsertParam17.ParameterName = "$modified";
                    _upsertCommand.Parameters.Add(_upsertParam17);
                var now = UnixTimeUtcUnique.Now();
                _upsertParam1.Value = item.id.ToByteArray();
                _upsertParam2.Value = item.state;
                _upsertParam3.Value = item.priority;
                _upsertParam4.Value = item.nextRun.milliseconds;
                _upsertParam5.Value = item.lastRun == null ? (object)DBNull.Value : item.lastRun?.milliseconds;
                _upsertParam6.Value = item.runCount;
                _upsertParam7.Value = item.maxRuns;
                _upsertParam8.Value = item.onSuccessDeleteAfter.milliseconds;
                _upsertParam9.Value = item.onFailDeleteAfter.milliseconds;
                _upsertParam10.Value = item.correlationId;
                _upsertParam11.Value = item.inputType;
                _upsertParam12.Value = item.inputData;
                _upsertParam13.Value = item.inputHash ?? (object)DBNull.Value;
                _upsertParam14.Value = item.outputType ?? (object)DBNull.Value;
                _upsertParam15.Value = item.outputData ?? (object)DBNull.Value;
                _upsertParam16.Value = now.uniqueTime;
                _upsertParam17.Value = now.uniqueTime;
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
                                                 "SET state = $state,priority = $priority,nextRun = $nextRun,lastRun = $lastRun,runCount = $runCount,maxRuns = $maxRuns,onSuccessDeleteAfter = $onSuccessDeleteAfter,onFailDeleteAfter = $onFailDeleteAfter,correlationId = $correlationId,inputType = $inputType,inputData = $inputData,inputHash = $inputHash,outputType = $outputType,outputData = $outputData,modified = $modified "+
                                                 "WHERE (id = $id)";
                    var _updateParam1 = _updateCommand.CreateParameter();
                    _updateParam1.ParameterName = "$id";
                    _updateCommand.Parameters.Add(_updateParam1);
                    var _updateParam2 = _updateCommand.CreateParameter();
                    _updateParam2.ParameterName = "$state";
                    _updateCommand.Parameters.Add(_updateParam2);
                    var _updateParam3 = _updateCommand.CreateParameter();
                    _updateParam3.ParameterName = "$priority";
                    _updateCommand.Parameters.Add(_updateParam3);
                    var _updateParam4 = _updateCommand.CreateParameter();
                    _updateParam4.ParameterName = "$nextRun";
                    _updateCommand.Parameters.Add(_updateParam4);
                    var _updateParam5 = _updateCommand.CreateParameter();
                    _updateParam5.ParameterName = "$lastRun";
                    _updateCommand.Parameters.Add(_updateParam5);
                    var _updateParam6 = _updateCommand.CreateParameter();
                    _updateParam6.ParameterName = "$runCount";
                    _updateCommand.Parameters.Add(_updateParam6);
                    var _updateParam7 = _updateCommand.CreateParameter();
                    _updateParam7.ParameterName = "$maxRuns";
                    _updateCommand.Parameters.Add(_updateParam7);
                    var _updateParam8 = _updateCommand.CreateParameter();
                    _updateParam8.ParameterName = "$onSuccessDeleteAfter";
                    _updateCommand.Parameters.Add(_updateParam8);
                    var _updateParam9 = _updateCommand.CreateParameter();
                    _updateParam9.ParameterName = "$onFailDeleteAfter";
                    _updateCommand.Parameters.Add(_updateParam9);
                    var _updateParam10 = _updateCommand.CreateParameter();
                    _updateParam10.ParameterName = "$correlationId";
                    _updateCommand.Parameters.Add(_updateParam10);
                    var _updateParam11 = _updateCommand.CreateParameter();
                    _updateParam11.ParameterName = "$inputType";
                    _updateCommand.Parameters.Add(_updateParam11);
                    var _updateParam12 = _updateCommand.CreateParameter();
                    _updateParam12.ParameterName = "$inputData";
                    _updateCommand.Parameters.Add(_updateParam12);
                    var _updateParam13 = _updateCommand.CreateParameter();
                    _updateParam13.ParameterName = "$inputHash";
                    _updateCommand.Parameters.Add(_updateParam13);
                    var _updateParam14 = _updateCommand.CreateParameter();
                    _updateParam14.ParameterName = "$outputType";
                    _updateCommand.Parameters.Add(_updateParam14);
                    var _updateParam15 = _updateCommand.CreateParameter();
                    _updateParam15.ParameterName = "$outputData";
                    _updateCommand.Parameters.Add(_updateParam15);
                    var _updateParam16 = _updateCommand.CreateParameter();
                    _updateParam16.ParameterName = "$created";
                    _updateCommand.Parameters.Add(_updateParam16);
                    var _updateParam17 = _updateCommand.CreateParameter();
                    _updateParam17.ParameterName = "$modified";
                    _updateCommand.Parameters.Add(_updateParam17);
                var now = UnixTimeUtcUnique.Now();
                _updateParam1.Value = item.id.ToByteArray();
                _updateParam2.Value = item.state;
                _updateParam3.Value = item.priority;
                _updateParam4.Value = item.nextRun.milliseconds;
                _updateParam5.Value = item.lastRun == null ? (object)DBNull.Value : item.lastRun?.milliseconds;
                _updateParam6.Value = item.runCount;
                _updateParam7.Value = item.maxRuns;
                _updateParam8.Value = item.onSuccessDeleteAfter.milliseconds;
                _updateParam9.Value = item.onFailDeleteAfter.milliseconds;
                _updateParam10.Value = item.correlationId;
                _updateParam11.Value = item.inputType;
                _updateParam12.Value = item.inputData;
                _updateParam13.Value = item.inputHash ?? (object)DBNull.Value;
                _updateParam14.Value = item.outputType ?? (object)DBNull.Value;
                _updateParam15.Value = item.outputData ?? (object)DBNull.Value;
                _updateParam16.Value = now.uniqueTime;
                _updateParam17.Value = now.uniqueTime;
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
                    var count = conn.ExecuteNonQuery(_getCountCommand);
                    return count;
                }
        }

        // SELECT id,state,priority,nextRun,lastRun,runCount,maxRuns,onSuccessDeleteAfter,onFailDeleteAfter,correlationId,inputType,inputData,inputHash,outputType,outputData,created,modified
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
                item.maxRuns = rdr.GetInt32(6);
            }

            if (rdr.IsDBNull(7))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.onSuccessDeleteAfter = new UnixTimeUtc(rdr.GetInt64(7));
            }

            if (rdr.IsDBNull(8))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.onFailDeleteAfter = new UnixTimeUtc(rdr.GetInt64(8));
            }

            if (rdr.IsDBNull(9))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.correlationId = rdr.GetString(9);
            }

            if (rdr.IsDBNull(10))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.inputType = rdr.GetString(10);
            }

            if (rdr.IsDBNull(11))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.inputData = rdr.GetString(11);
            }

            if (rdr.IsDBNull(12))
                item.inputHash = null;
            else
            {
                item.inputHash = rdr.GetString(12);
            }

            if (rdr.IsDBNull(13))
                item.outputType = null;
            else
            {
                item.outputType = rdr.GetString(13);
            }

            if (rdr.IsDBNull(14))
                item.outputData = null;
            else
            {
                item.outputData = rdr.GetString(14);
            }

            if (rdr.IsDBNull(15))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.created = new UnixTimeUtcUnique(rdr.GetInt64(15));
            }

            if (rdr.IsDBNull(16))
                item.modified = null;
            else
            {
                item.modified = new UnixTimeUtcUnique(rdr.GetInt64(16));
            }
            return item;
       }

        public int Delete(DatabaseConnection conn, Guid id)
        {
                using (var _delete0Command = _database.CreateCommand())
                {
                    _delete0Command.CommandText = "DELETE FROM jobs " +
                                                 "WHERE id = $id";
                    var _delete0Param1 = _delete0Command.CreateParameter();
                    _delete0Param1.ParameterName = "$id";
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
                item.state = rdr.GetInt32(0);
            }

            if (rdr.IsDBNull(1))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.priority = rdr.GetInt32(1);
            }

            if (rdr.IsDBNull(2))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.nextRun = new UnixTimeUtc(rdr.GetInt64(2));
            }

            if (rdr.IsDBNull(3))
                item.lastRun = null;
            else
            {
                item.lastRun = new UnixTimeUtc(rdr.GetInt64(3));
            }

            if (rdr.IsDBNull(4))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.runCount = rdr.GetInt32(4);
            }

            if (rdr.IsDBNull(5))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.maxRuns = rdr.GetInt32(5);
            }

            if (rdr.IsDBNull(6))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.onSuccessDeleteAfter = new UnixTimeUtc(rdr.GetInt64(6));
            }

            if (rdr.IsDBNull(7))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.onFailDeleteAfter = new UnixTimeUtc(rdr.GetInt64(7));
            }

            if (rdr.IsDBNull(8))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.correlationId = rdr.GetString(8);
            }

            if (rdr.IsDBNull(9))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.inputType = rdr.GetString(9);
            }

            if (rdr.IsDBNull(10))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.inputData = rdr.GetString(10);
            }

            if (rdr.IsDBNull(11))
                item.inputHash = null;
            else
            {
                item.inputHash = rdr.GetString(11);
            }

            if (rdr.IsDBNull(12))
                item.outputType = null;
            else
            {
                item.outputType = rdr.GetString(12);
            }

            if (rdr.IsDBNull(13))
                item.outputData = null;
            else
            {
                item.outputData = rdr.GetString(13);
            }

            if (rdr.IsDBNull(14))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.created = new UnixTimeUtcUnique(rdr.GetInt64(14));
            }

            if (rdr.IsDBNull(15))
                item.modified = null;
            else
            {
                item.modified = new UnixTimeUtcUnique(rdr.GetInt64(15));
            }
            return item;
       }

        public JobsRecord Get(DatabaseConnection conn, Guid id)
        {
                using (var _get0Command = _database.CreateCommand())
                {
                    _get0Command.CommandText = "SELECT state,priority,nextRun,lastRun,runCount,maxRuns,onSuccessDeleteAfter,onFailDeleteAfter,correlationId,inputType,inputData,inputHash,outputType,outputData,created,modified FROM jobs " +
                                                 "WHERE id = $id LIMIT 1;";
                    var _get0Param1 = _get0Command.CreateParameter();
                    _get0Param1.ParameterName = "$id";
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
