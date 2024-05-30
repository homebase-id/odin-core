using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;
using Odin.Core.Identity;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class CircleRecord
    {
        private string _circleName;
        public string circleName
        {
           get {
                   return _circleName;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 2) throw new Exception("Too short");
                    if (value?.Length > 80) throw new Exception("Too long");
                  _circleName = value;
               }
        }
        private Guid _circleId;
        public Guid circleId
        {
           get {
                   return _circleId;
               }
           set {
                  _circleId = value;
               }
        }
        private byte[] _data;
        public byte[] data
        {
           get {
                   return _data;
               }
           set {
                    if (value?.Length < 0) throw new Exception("Too short");
                    if (value?.Length > 65000) throw new Exception("Too long");
                  _data = value;
               }
        }
    } // End of class CircleRecord

    public class TableCircleCRUD : TableBase
    {
        private bool _disposed = false;
        private readonly CacheHelper _cache;

        public TableCircleCRUD(IdentityDatabase db, CacheHelper cache) : base(db)
        {
            _cache = cache;
        }

        ~TableCircleCRUD()
        {
            if (_disposed == false) throw new Exception("TableCircleCRUD Not disposed properly");
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
                       cmd.CommandText = "DROP TABLE IF EXISTS circle;";
                       conn.ExecuteNonQuery(cmd);
                    }
                    cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS circle("
                     +"circleName STRING NOT NULL, "
                     +"circleId BLOB NOT NULL UNIQUE, "
                     +"data BLOB  "
                     +", PRIMARY KEY (circleId)"
                     +");"
                     ;
                    conn.ExecuteNonQuery(cmd);
            }
        }

        public virtual int Insert(DatabaseConnection conn, CircleRecord item)
        {
                using (var _insertCommand = _database.CreateCommand())
                {
                    _insertCommand.CommandText = "INSERT INTO circle (circleName,circleId,data) " +
                                                 "VALUES ($circleName,$circleId,$data)";
                    var _insertParam1 = _insertCommand.CreateParameter();
                    _insertParam1.ParameterName = "$circleName";
                    _insertCommand.Parameters.Add(_insertParam1);
                    var _insertParam2 = _insertCommand.CreateParameter();
                    _insertParam2.ParameterName = "$circleId";
                    _insertCommand.Parameters.Add(_insertParam2);
                    var _insertParam3 = _insertCommand.CreateParameter();
                    _insertParam3.ParameterName = "$data";
                    _insertCommand.Parameters.Add(_insertParam3);
                _insertParam1.Value = item.circleName;
                _insertParam2.Value = item.circleId.ToByteArray();
                _insertParam3.Value = item.data ?? (object)DBNull.Value;
                var count = conn.ExecuteNonQuery(_insertCommand);
                if (count > 0)
                 {
                    _cache.AddOrUpdate("TableCircleCRUD", item.circleId.ToString(), item);
                 }
                return count;
                } // Using
        }

        public virtual int Upsert(DatabaseConnection conn, CircleRecord item)
        {
                using (var _upsertCommand = _database.CreateCommand())
                {
                    _upsertCommand.CommandText = "INSERT INTO circle (circleName,circleId,data) " +
                                                 "VALUES ($circleName,$circleId,$data)"+
                                                 "ON CONFLICT (circleId) DO UPDATE "+
                                                 "SET circleName = $circleName,data = $data "+
                                                 ";";
                    var _upsertParam1 = _upsertCommand.CreateParameter();
                    _upsertParam1.ParameterName = "$circleName";
                    _upsertCommand.Parameters.Add(_upsertParam1);
                    var _upsertParam2 = _upsertCommand.CreateParameter();
                    _upsertParam2.ParameterName = "$circleId";
                    _upsertCommand.Parameters.Add(_upsertParam2);
                    var _upsertParam3 = _upsertCommand.CreateParameter();
                    _upsertParam3.ParameterName = "$data";
                    _upsertCommand.Parameters.Add(_upsertParam3);
                _upsertParam1.Value = item.circleName;
                _upsertParam2.Value = item.circleId.ToByteArray();
                _upsertParam3.Value = item.data ?? (object)DBNull.Value;
                var count = conn.ExecuteNonQuery(_upsertCommand);
                if (count > 0)
                    _cache.AddOrUpdate("TableCircleCRUD", item.circleId.ToString(), item);
                return count;
                } // Using
        }
        public virtual int Update(DatabaseConnection conn, CircleRecord item)
        {
                using (var _updateCommand = _database.CreateCommand())
                {
                    _updateCommand.CommandText = "UPDATE circle " +
                                                 "SET circleName = $circleName,data = $data "+
                                                 "WHERE (circleId = $circleId)";
                    var _updateParam1 = _updateCommand.CreateParameter();
                    _updateParam1.ParameterName = "$circleName";
                    _updateCommand.Parameters.Add(_updateParam1);
                    var _updateParam2 = _updateCommand.CreateParameter();
                    _updateParam2.ParameterName = "$circleId";
                    _updateCommand.Parameters.Add(_updateParam2);
                    var _updateParam3 = _updateCommand.CreateParameter();
                    _updateParam3.ParameterName = "$data";
                    _updateCommand.Parameters.Add(_updateParam3);
                _updateParam1.Value = item.circleName;
                _updateParam2.Value = item.circleId.ToByteArray();
                _updateParam3.Value = item.data ?? (object)DBNull.Value;
                var count = conn.ExecuteNonQuery(_updateCommand);
                if (count > 0)
                {
                    _cache.AddOrUpdate("TableCircleCRUD", item.circleId.ToString(), item);
                }
                return count;
                } // Using
        }

        public virtual int GetCountDirty(DatabaseConnection conn)
        {
                using (var _getCountCommand = _database.CreateCommand())
                {
                    _getCountCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM circle; PRAGMA read_uncommitted = 0;";
                    var count = conn.ExecuteNonQuery(_getCountCommand);
                    return count;
                }
        }

        // SELECT circleName,circleId,data
        public CircleRecord ReadRecordFromReaderAll(SqliteDataReader rdr)
        {
            var result = new List<CircleRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new CircleRecord();

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.circleName = rdr.GetString(0);
            }

            if (rdr.IsDBNull(1))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(1, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in circleId...");
                item.circleId = new Guid(_guid);
            }

            if (rdr.IsDBNull(2))
                item.data = null;
            else
            {
                bytesRead = rdr.GetBytes(2, 0, _tmpbuf, 0, 65000+1);
                if (bytesRead > 65000)
                    throw new Exception("Too much data in data...");
                if (bytesRead < 0)
                    throw new Exception("Too little data in data...");
                item.data = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.data, 0, (int) bytesRead);
            }
            return item;
       }

        public int Delete(DatabaseConnection conn, Guid circleId)
        {
                using (var _delete0Command = _database.CreateCommand())
                {
                    _delete0Command.CommandText = "DELETE FROM circle " +
                                                 "WHERE circleId = $circleId";
                    var _delete0Param1 = _delete0Command.CreateParameter();
                    _delete0Param1.ParameterName = "$circleId";
                    _delete0Command.Parameters.Add(_delete0Param1);

                _delete0Param1.Value = circleId.ToByteArray();
                var count = conn.ExecuteNonQuery(_delete0Command);
                if (count > 0)
                    _cache.Remove("TableCircleCRUD", circleId.ToString());
                return count;
                } // Using
        }

        public CircleRecord ReadRecordFromReader0(SqliteDataReader rdr, Guid circleId)
        {
            var result = new List<CircleRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new CircleRecord();
            item.circleId = circleId;

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.circleName = rdr.GetString(0);
            }

            if (rdr.IsDBNull(1))
                item.data = null;
            else
            {
                bytesRead = rdr.GetBytes(1, 0, _tmpbuf, 0, 65000+1);
                if (bytesRead > 65000)
                    throw new Exception("Too much data in data...");
                if (bytesRead < 0)
                    throw new Exception("Too little data in data...");
                item.data = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.data, 0, (int) bytesRead);
            }
            return item;
       }

        public CircleRecord Get(DatabaseConnection conn, Guid circleId)
        {
            var (hit, cacheObject) = _cache.Get("TableCircleCRUD", circleId.ToString());
            if (hit)
                return (CircleRecord)cacheObject;
                using (var _get0Command = _database.CreateCommand())
                {
                    _get0Command.CommandText = "SELECT circleName,data FROM circle " +
                                                 "WHERE circleId = $circleId LIMIT 1;";
                    var _get0Param1 = _get0Command.CreateParameter();
                    _get0Param1.ParameterName = "$circleId";
                    _get0Command.Parameters.Add(_get0Param1);

                _get0Param1.Value = circleId.ToByteArray();
                    lock (conn._lock)
                    {
                using (SqliteDataReader rdr = conn.ExecuteReader(_get0Command, System.Data.CommandBehavior.SingleRow))
                {
                    if (!rdr.Read())
                    {
                        _cache.AddOrUpdate("TableCircleCRUD", circleId.ToString(), null);
                        return null;
                    }
                    var r = ReadRecordFromReader0(rdr, circleId);
                    _cache.AddOrUpdate("TableCircleCRUD", circleId.ToString(), r);
                    return r;
                } // using
            } // lock
            } // using
        }

        public List<CircleRecord> PagingByCircleId(DatabaseConnection conn, int count, Guid? inCursor, out Guid? nextCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");
            if (inCursor == null)
                inCursor = Guid.Empty;

                using (var _getPaging2Command = _database.CreateCommand())
                {
                    _getPaging2Command.CommandText = "SELECT circleName,circleId,data FROM circle " +
                                                 "WHERE circleId > $circleId ORDER BY circleId ASC LIMIT $_count;";
                    var _getPaging2Param1 = _getPaging2Command.CreateParameter();
                    _getPaging2Param1.ParameterName = "$circleId";
                    _getPaging2Command.Parameters.Add(_getPaging2Param1);
                    var _getPaging2Param2 = _getPaging2Command.CreateParameter();
                    _getPaging2Param2.ParameterName = "$_count";
                    _getPaging2Command.Parameters.Add(_getPaging2Param2);

                _getPaging2Param1.Value = inCursor?.ToByteArray();
                _getPaging2Param2.Value = count+1;

            lock (conn._lock)
            {
                using (SqliteDataReader rdr = conn.ExecuteReader(_getPaging2Command, System.Data.CommandBehavior.Default))
                {
                    var result = new List<CircleRecord>();
                    int n = 0;
                    while ((n < count) && rdr.Read())
                    {
                        n++;
                        result.Add(ReadRecordFromReaderAll(rdr));
                    } // while
                    if ((n > 0) && rdr.Read())
                    {
                            nextCursor = result[n - 1].circleId;
                    }
                    else
                    {
                        nextCursor = null;
                    }
                    return result;
                } // using
                } // Lock
            } // using 
        } // PagingGet

    }
}
