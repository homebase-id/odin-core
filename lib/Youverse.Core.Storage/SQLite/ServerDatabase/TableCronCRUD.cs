using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Youverse.Core.Identity;

namespace Youverse.Core.Storage.Sqlite.ServerDatabase
{
    public class CronRecord
    {
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
        private byte[] _data;
        public byte[] data
        {
           get {
                   return _data;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 0) throw new Exception("Too short");
                    if (value?.Length > 65535) throw new Exception("Too long");
                  _data = value;
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
        private UnixTimeUtc _lastRun;
        public UnixTimeUtc lastRun
        {
           get {
                   return _lastRun;
               }
           set {
                  _lastRun = value;
               }
        }
        private Guid? _popStamp;
        public Guid? popStamp
        {
           get {
                   return _popStamp;
               }
           set {
                  _popStamp = value;
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
    } // End of class CronRecord

    public class TableCronCRUD : TableBase
    {
        private bool _disposed = false;
        private SqliteCommand _insertCommand = null;
        private static Object _insertLock = new Object();
        private SqliteParameter _insertParam1 = null;
        private SqliteParameter _insertParam2 = null;
        private SqliteParameter _insertParam3 = null;
        private SqliteParameter _insertParam4 = null;
        private SqliteParameter _insertParam5 = null;
        private SqliteParameter _insertParam6 = null;
        private SqliteParameter _insertParam7 = null;
        private SqliteParameter _insertParam8 = null;
        private SqliteParameter _insertParam9 = null;
        private SqliteCommand _updateCommand = null;
        private static Object _updateLock = new Object();
        private SqliteParameter _updateParam1 = null;
        private SqliteParameter _updateParam2 = null;
        private SqliteParameter _updateParam3 = null;
        private SqliteParameter _updateParam4 = null;
        private SqliteParameter _updateParam5 = null;
        private SqliteParameter _updateParam6 = null;
        private SqliteParameter _updateParam7 = null;
        private SqliteParameter _updateParam8 = null;
        private SqliteParameter _updateParam9 = null;
        private SqliteCommand _upsertCommand = null;
        private static Object _upsertLock = new Object();
        private SqliteParameter _upsertParam1 = null;
        private SqliteParameter _upsertParam2 = null;
        private SqliteParameter _upsertParam3 = null;
        private SqliteParameter _upsertParam4 = null;
        private SqliteParameter _upsertParam5 = null;
        private SqliteParameter _upsertParam6 = null;
        private SqliteParameter _upsertParam7 = null;
        private SqliteParameter _upsertParam8 = null;
        private SqliteParameter _upsertParam9 = null;
        private SqliteCommand _delete0Command = null;
        private static Object _delete0Lock = new Object();
        private SqliteParameter _delete0Param1 = null;
        private SqliteParameter _delete0Param2 = null;
        private SqliteCommand _get0Command = null;
        private static Object _get0Lock = new Object();
        private SqliteParameter _get0Param1 = null;
        private SqliteParameter _get0Param2 = null;

        public TableCronCRUD(ServerDatabase db) : base(db)
        {
        }

        ~TableCronCRUD()
        {
            if (_disposed == false) throw new Exception("TableCronCRUD Not disposed properly");
        }

        public override void Dispose()
        {
            _insertCommand?.Dispose();
            _insertCommand = null;
            _updateCommand?.Dispose();
            _updateCommand = null;
            _upsertCommand?.Dispose();
            _upsertCommand = null;
            _delete0Command?.Dispose();
            _delete0Command = null;
            _get0Command?.Dispose();
            _get0Command = null;
            _disposed = true;
        }

        public sealed override void EnsureTableExists(bool dropExisting = false)
        {
            using (var cmd = _database.CreateCommand())
            {
                if (dropExisting)
                {
                    cmd.CommandText = "DROP TABLE IF EXISTS cron;";
                    _database.ExecuteNonQuery(cmd);
                }
                cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS cron("
                     +"identityId BLOB NOT NULL, "
                     +"type INT NOT NULL, "
                     +"data BLOB NOT NULL, "
                     +"runCount INT NOT NULL, "
                     +"nextRun INT NOT NULL, "
                     +"lastRun INT NOT NULL, "
                     +"popStamp BLOB , "
                     +"created INT NOT NULL, "
                     +"modified INT  "
                     +", PRIMARY KEY (identityId,type)"
                     +");"
                     +"CREATE INDEX IF NOT EXISTS Idx0TableCronCRUD ON cron(nextRun);"
                     ;
                _database.ExecuteNonQuery(cmd);
                _database.Commit();
            }
        }

        public virtual int Insert(CronRecord item)
        {
            lock (_insertLock)
            {
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand();
                    _insertCommand.CommandText = "INSERT INTO cron (identityId,type,data,runCount,nextRun,lastRun,popStamp,created,modified) " +
                                                 "VALUES ($identityId,$type,$data,$runCount,$nextRun,$lastRun,$popStamp,$created,$modified)";
                    _insertParam1 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam1);
                    _insertParam1.ParameterName = "$identityId";
                    _insertParam2 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam2);
                    _insertParam2.ParameterName = "$type";
                    _insertParam3 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam3);
                    _insertParam3.ParameterName = "$data";
                    _insertParam4 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam4);
                    _insertParam4.ParameterName = "$runCount";
                    _insertParam5 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam5);
                    _insertParam5.ParameterName = "$nextRun";
                    _insertParam6 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam6);
                    _insertParam6.ParameterName = "$lastRun";
                    _insertParam7 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam7);
                    _insertParam7.ParameterName = "$popStamp";
                    _insertParam8 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam8);
                    _insertParam8.ParameterName = "$created";
                    _insertParam9 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam9);
                    _insertParam9.ParameterName = "$modified";
                    _insertCommand.Prepare();
                }
                _insertParam1.Value = item.identityId.ToByteArray();
                _insertParam2.Value = item.type;
                _insertParam3.Value = item.data;
                _insertParam4.Value = item.runCount;
                _insertParam5.Value = item.nextRun.milliseconds;
                _insertParam6.Value = item.lastRun.milliseconds;
                _insertParam7.Value = item.popStamp?.ToByteArray() ?? (object)DBNull.Value;
                _insertParam8.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _insertParam9.Value = DBNull.Value;
                return _database.ExecuteNonQuery(_insertCommand);
            } // Lock
        }

        public virtual int Upsert(CronRecord item)
        {
            lock (_upsertLock)
            {
                if (_upsertCommand == null)
                {
                    _upsertCommand = _database.CreateCommand();
                    _upsertCommand.CommandText = "INSERT INTO cron (identityId,type,data,runCount,nextRun,lastRun,popStamp,created,modified) " +
                                                 "VALUES ($identityId,$type,$data,$runCount,$nextRun,$lastRun,$popStamp,$created,$modified)"+
                                                 "ON CONFLICT (identityId,type) DO UPDATE "+
                                                 "SET data = $data,runCount = $runCount,nextRun = $nextRun,lastRun = $lastRun,popStamp = $popStamp,modified = $modified;";
                    _upsertParam1 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam1);
                    _upsertParam1.ParameterName = "$identityId";
                    _upsertParam2 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam2);
                    _upsertParam2.ParameterName = "$type";
                    _upsertParam3 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam3);
                    _upsertParam3.ParameterName = "$data";
                    _upsertParam4 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam4);
                    _upsertParam4.ParameterName = "$runCount";
                    _upsertParam5 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam5);
                    _upsertParam5.ParameterName = "$nextRun";
                    _upsertParam6 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam6);
                    _upsertParam6.ParameterName = "$lastRun";
                    _upsertParam7 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam7);
                    _upsertParam7.ParameterName = "$popStamp";
                    _upsertParam8 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam8);
                    _upsertParam8.ParameterName = "$created";
                    _upsertParam9 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam9);
                    _upsertParam9.ParameterName = "$modified";
                    _upsertCommand.Prepare();
                }
                _upsertParam1.Value = item.identityId.ToByteArray();
                _upsertParam2.Value = item.type;
                _upsertParam3.Value = item.data;
                _upsertParam4.Value = item.runCount;
                _upsertParam5.Value = item.nextRun.milliseconds;
                _upsertParam6.Value = item.lastRun.milliseconds;
                _upsertParam7.Value = item.popStamp?.ToByteArray() ?? (object)DBNull.Value;
                _upsertParam8.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _upsertParam9.Value = UnixTimeUtcUnique.Now().uniqueTime;
                return _database.ExecuteNonQuery(_upsertCommand);
            } // Lock
        }

        public virtual int Update(CronRecord item)
        {
            lock (_updateLock)
            {
                if (_updateCommand == null)
                {
                    _updateCommand = _database.CreateCommand();
                    _updateCommand.CommandText = "UPDATE cron " +
                                                 "SET data = $data,runCount = $runCount,nextRun = $nextRun,lastRun = $lastRun,popStamp = $popStamp,modified = $modified "+
                                                 "WHERE (identityId = $identityId,type = $type)";
                    _updateParam1 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam1);
                    _updateParam1.ParameterName = "$identityId";
                    _updateParam2 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam2);
                    _updateParam2.ParameterName = "$type";
                    _updateParam3 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam3);
                    _updateParam3.ParameterName = "$data";
                    _updateParam4 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam4);
                    _updateParam4.ParameterName = "$runCount";
                    _updateParam5 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam5);
                    _updateParam5.ParameterName = "$nextRun";
                    _updateParam6 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam6);
                    _updateParam6.ParameterName = "$lastRun";
                    _updateParam7 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam7);
                    _updateParam7.ParameterName = "$popStamp";
                    _updateParam8 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam8);
                    _updateParam8.ParameterName = "$created";
                    _updateParam9 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam9);
                    _updateParam9.ParameterName = "$modified";
                    _updateCommand.Prepare();
                }
                _updateParam1.Value = item.identityId.ToByteArray();
                _updateParam2.Value = item.type;
                _updateParam3.Value = item.data;
                _updateParam4.Value = item.runCount;
                _updateParam5.Value = item.nextRun.milliseconds;
                _updateParam6.Value = item.lastRun.milliseconds;
                _updateParam7.Value = item.popStamp?.ToByteArray() ?? (object)DBNull.Value;
                _updateParam8.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _updateParam9.Value = UnixTimeUtcUnique.Now().uniqueTime;
                return _database.ExecuteNonQuery(_updateCommand);
            } // Lock
        }

        public int Delete(Guid identityId,Int32 type)
        {
            lock (_delete0Lock)
            {
                if (_delete0Command == null)
                {
                    _delete0Command = _database.CreateCommand();
                    _delete0Command.CommandText = "DELETE FROM cron " +
                                                 "WHERE identityId = $identityId AND type = $type";
                    _delete0Param1 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param1);
                    _delete0Param1.ParameterName = "$identityId";
                    _delete0Param2 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param2);
                    _delete0Param2.ParameterName = "$type";
                    _delete0Command.Prepare();
                }
                _delete0Param1.Value = identityId.ToByteArray();
                _delete0Param2.Value = type;
                return _database.ExecuteNonQuery(_delete0Command);
            } // Lock
        }

        public CronRecord Get(Guid identityId,Int32 type)
        {
            lock (_get0Lock)
            {
                if (_get0Command == null)
                {
                    _get0Command = _database.CreateCommand();
                    _get0Command.CommandText = "SELECT data,runCount,nextRun,lastRun,popStamp,created,modified FROM cron " +
                                                 "WHERE identityId = $identityId AND type = $type LIMIT 1;";
                    _get0Param1 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param1);
                    _get0Param1.ParameterName = "$identityId";
                    _get0Param2 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param2);
                    _get0Param2.ParameterName = "$type";
                    _get0Command.Prepare();
                }
                _get0Param1.Value = identityId.ToByteArray();
                _get0Param2.Value = type;
                using (SqliteDataReader rdr = _database.ExecuteReader(_get0Command, System.Data.CommandBehavior.SingleRow))
                {
                    var result = new CronRecord();
                    if (!rdr.Read())
                        return null;
                    byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
                    long bytesRead;
#pragma warning restore CS0168
                    var _guid = new byte[16];
                        var item = new CronRecord();
                        item.identityId = identityId;
                        item.type = type;

                        if (rdr.IsDBNull(0))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            bytesRead = rdr.GetBytes(0, 0, _tmpbuf, 0, 65535+1);
                            if (bytesRead > 65535)
                                throw new Exception("Too much data in data...");
                            if (bytesRead < 0)
                                throw new Exception("Too little data in data...");
                            item.data = new byte[bytesRead];
                            Buffer.BlockCopy(_tmpbuf, 0, item.data, 0, (int) bytesRead);
                        }

                        if (rdr.IsDBNull(1))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            item.runCount = rdr.GetInt32(1);
                        }

                        if (rdr.IsDBNull(2))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            item.nextRun = new UnixTimeUtc(rdr.GetInt64(2));
                        }

                        if (rdr.IsDBNull(3))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            item.lastRun = new UnixTimeUtc(rdr.GetInt64(3));
                        }

                        if (rdr.IsDBNull(4))
                            item.popStamp = null;
                        else
                        {
                            bytesRead = rdr.GetBytes(4, 0, _guid, 0, 16);
                            if (bytesRead != 16)
                                throw new Exception("Not a GUID in popStamp...");
                            item.popStamp = new Guid(_guid);
                        }

                        if (rdr.IsDBNull(5))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            item.created = new UnixTimeUtcUnique(rdr.GetInt64(5));
                        }

                        if (rdr.IsDBNull(6))
                            item.modified = null;
                        else
                        {
                            item.modified = new UnixTimeUtcUnique(rdr.GetInt64(6));
                        }
                    return item;
                } // using
            } // lock
        }

    }
}
