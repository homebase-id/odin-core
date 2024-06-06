using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;
using Odin.Core.Identity;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class CircleMemberRecord
    {
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
        private Guid _memberId;
        public Guid memberId
        {
           get {
                   return _memberId;
               }
           set {
                  _memberId = value;
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
                    if (value?.Length > 65535) throw new Exception("Too long");
                  _data = value;
               }
        }
    } // End of class CircleMemberRecord

    public class TableCircleMemberCRUD : TableBase
    {
        private bool _disposed = false;
        private readonly CacheHelper _cache;

        public TableCircleMemberCRUD(IdentityDatabase db, CacheHelper cache) : base(db)
        {
            _cache = cache;
        }

        ~TableCircleMemberCRUD()
        {
            if (_disposed == false) throw new Exception("TableCircleMemberCRUD Not disposed properly");
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
                       cmd.CommandText = "DROP TABLE IF EXISTS circleMember;";
                       conn.ExecuteNonQuery(cmd);
                    }
                    cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS circleMember("
                     +"circleId BLOB NOT NULL, "
                     +"memberId BLOB NOT NULL, "
                     +"data BLOB  "
                     +", PRIMARY KEY (circleId,memberId)"
                     +");"
                     ;
                    conn.ExecuteNonQuery(cmd);
            }
        }

        public virtual int Insert(DatabaseConnection conn, CircleMemberRecord item)
        {
                using (var _insertCommand = _database.CreateCommand())
                {
                    _insertCommand.CommandText = "INSERT INTO circleMember (circleId,memberId,data) " +
                                                 "VALUES ($circleId,$memberId,$data)";
                    var _insertParam1 = _insertCommand.CreateParameter();
                    _insertParam1.ParameterName = "$circleId";
                    _insertCommand.Parameters.Add(_insertParam1);
                    var _insertParam2 = _insertCommand.CreateParameter();
                    _insertParam2.ParameterName = "$memberId";
                    _insertCommand.Parameters.Add(_insertParam2);
                    var _insertParam3 = _insertCommand.CreateParameter();
                    _insertParam3.ParameterName = "$data";
                    _insertCommand.Parameters.Add(_insertParam3);
                _insertParam1.Value = item.circleId.ToByteArray();
                _insertParam2.Value = item.memberId.ToByteArray();
                _insertParam3.Value = item.data ?? (object)DBNull.Value;
                var count = conn.ExecuteNonQuery(_insertCommand);
                if (count > 0)
                 {
                    _cache.AddOrUpdate("TableCircleMemberCRUD", item.circleId.ToString()+item.memberId.ToString(), item);
                 }
                return count;
                } // Using
        }

        public virtual int TryInsert(DatabaseConnection conn, CircleMemberRecord item)
        {
            using (var _insertCommand = _database.CreateCommand())
            {
                _insertCommand.CommandText = "INSERT OR IGNORE INTO circleMember (circleId,memberId,data) " +
                                             "VALUES (@circleId,@memberId,@data)";
                var _insertParam1 = _insertCommand.CreateParameter();
                _insertParam1.ParameterName = "@circleId";
                _insertCommand.Parameters.Add(_insertParam1);
                var _insertParam2 = _insertCommand.CreateParameter();
                _insertParam2.ParameterName = "@memberId";
                _insertCommand.Parameters.Add(_insertParam2);
                var _insertParam3 = _insertCommand.CreateParameter();
                _insertParam3.ParameterName = "@data";
                _insertCommand.Parameters.Add(_insertParam3);
                _insertParam1.Value = item.circleId.ToByteArray();
                _insertParam2.Value = item.memberId.ToByteArray();
                _insertParam3.Value = item.data ?? (object)DBNull.Value;
                var count = conn.ExecuteNonQuery(_insertCommand);
                if (count > 0)
                 {
                   _cache.AddOrUpdate("TableCircleMemberCRUD", item.circleId.ToString()+item.memberId.ToString(), item);
                 }
                return count;
            } // Using
        }

        public virtual int Upsert(DatabaseConnection conn, CircleMemberRecord item)
        {
                using (var _upsertCommand = _database.CreateCommand())
                {
                    _upsertCommand.CommandText = "INSERT INTO circleMember (circleId,memberId,data) " +
                                                 "VALUES ($circleId,$memberId,$data)"+
                                                 "ON CONFLICT (circleId,memberId) DO UPDATE "+
                                                 "SET data = $data "+
                                                 ";";
                    var _upsertParam1 = _upsertCommand.CreateParameter();
                    _upsertParam1.ParameterName = "$circleId";
                    _upsertCommand.Parameters.Add(_upsertParam1);
                    var _upsertParam2 = _upsertCommand.CreateParameter();
                    _upsertParam2.ParameterName = "$memberId";
                    _upsertCommand.Parameters.Add(_upsertParam2);
                    var _upsertParam3 = _upsertCommand.CreateParameter();
                    _upsertParam3.ParameterName = "$data";
                    _upsertCommand.Parameters.Add(_upsertParam3);
                _upsertParam1.Value = item.circleId.ToByteArray();
                _upsertParam2.Value = item.memberId.ToByteArray();
                _upsertParam3.Value = item.data ?? (object)DBNull.Value;
                var count = conn.ExecuteNonQuery(_upsertCommand);
                if (count > 0)
                    _cache.AddOrUpdate("TableCircleMemberCRUD", item.circleId.ToString()+item.memberId.ToString(), item);
                return count;
                } // Using
        }
        public virtual int Update(DatabaseConnection conn, CircleMemberRecord item)
        {
                using (var _updateCommand = _database.CreateCommand())
                {
                    _updateCommand.CommandText = "UPDATE circleMember " +
                                                 "SET data = $data "+
                                                 "WHERE (circleId = $circleId AND memberId = $memberId)";
                    var _updateParam1 = _updateCommand.CreateParameter();
                    _updateParam1.ParameterName = "$circleId";
                    _updateCommand.Parameters.Add(_updateParam1);
                    var _updateParam2 = _updateCommand.CreateParameter();
                    _updateParam2.ParameterName = "$memberId";
                    _updateCommand.Parameters.Add(_updateParam2);
                    var _updateParam3 = _updateCommand.CreateParameter();
                    _updateParam3.ParameterName = "$data";
                    _updateCommand.Parameters.Add(_updateParam3);
                _updateParam1.Value = item.circleId.ToByteArray();
                _updateParam2.Value = item.memberId.ToByteArray();
                _updateParam3.Value = item.data ?? (object)DBNull.Value;
                var count = conn.ExecuteNonQuery(_updateCommand);
                if (count > 0)
                {
                    _cache.AddOrUpdate("TableCircleMemberCRUD", item.circleId.ToString()+item.memberId.ToString(), item);
                }
                return count;
                } // Using
        }

        public virtual int GetCountDirty(DatabaseConnection conn)
        {
                using (var _getCountCommand = _database.CreateCommand())
                {
                    _getCountCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM circleMember; PRAGMA read_uncommitted = 0;";
                    var count = conn.ExecuteNonQuery(_getCountCommand);
                    return count;
                }
        }

        // SELECT circleId,memberId,data
        public CircleMemberRecord ReadRecordFromReaderAll(SqliteDataReader rdr)
        {
            var result = new List<CircleMemberRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new CircleMemberRecord();

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(0, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in circleId...");
                item.circleId = new Guid(_guid);
            }

            if (rdr.IsDBNull(1))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(1, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in memberId...");
                item.memberId = new Guid(_guid);
            }

            if (rdr.IsDBNull(2))
                item.data = null;
            else
            {
                bytesRead = rdr.GetBytes(2, 0, _tmpbuf, 0, 65535+1);
                if (bytesRead > 65535)
                    throw new Exception("Too much data in data...");
                if (bytesRead < 0)
                    throw new Exception("Too little data in data...");
                item.data = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.data, 0, (int) bytesRead);
            }
            return item;
       }

        public int Delete(DatabaseConnection conn, Guid circleId,Guid memberId)
        {
                using (var _delete0Command = _database.CreateCommand())
                {
                    _delete0Command.CommandText = "DELETE FROM circleMember " +
                                                 "WHERE circleId = $circleId AND memberId = $memberId";
                    var _delete0Param1 = _delete0Command.CreateParameter();
                    _delete0Param1.ParameterName = "$circleId";
                    _delete0Command.Parameters.Add(_delete0Param1);
                    var _delete0Param2 = _delete0Command.CreateParameter();
                    _delete0Param2.ParameterName = "$memberId";
                    _delete0Command.Parameters.Add(_delete0Param2);

                _delete0Param1.Value = circleId.ToByteArray();
                _delete0Param2.Value = memberId.ToByteArray();
                var count = conn.ExecuteNonQuery(_delete0Command);
                if (count > 0)
                    _cache.Remove("TableCircleMemberCRUD", circleId.ToString()+memberId.ToString());
                return count;
                } // Using
        }

        public CircleMemberRecord ReadRecordFromReader0(SqliteDataReader rdr, Guid circleId,Guid memberId)
        {
            var result = new List<CircleMemberRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new CircleMemberRecord();
            item.circleId = circleId;
            item.memberId = memberId;

            if (rdr.IsDBNull(0))
                item.data = null;
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
            return item;
       }

        public CircleMemberRecord Get(DatabaseConnection conn, Guid circleId,Guid memberId)
        {
            var (hit, cacheObject) = _cache.Get("TableCircleMemberCRUD", circleId.ToString()+memberId.ToString());
            if (hit)
                return (CircleMemberRecord)cacheObject;
                using (var _get0Command = _database.CreateCommand())
                {
                    _get0Command.CommandText = "SELECT data FROM circleMember " +
                                                 "WHERE circleId = $circleId AND memberId = $memberId LIMIT 1;";
                    var _get0Param1 = _get0Command.CreateParameter();
                    _get0Param1.ParameterName = "$circleId";
                    _get0Command.Parameters.Add(_get0Param1);
                    var _get0Param2 = _get0Command.CreateParameter();
                    _get0Param2.ParameterName = "$memberId";
                    _get0Command.Parameters.Add(_get0Param2);

                _get0Param1.Value = circleId.ToByteArray();
                _get0Param2.Value = memberId.ToByteArray();
                    lock (conn._lock)
                    {
                using (SqliteDataReader rdr = conn.ExecuteReader(_get0Command, System.Data.CommandBehavior.SingleRow))
                {
                    if (!rdr.Read())
                    {
                        _cache.AddOrUpdate("TableCircleMemberCRUD", circleId.ToString()+memberId.ToString(), null);
                        return null;
                    }
                    var r = ReadRecordFromReader0(rdr, circleId,memberId);
                    _cache.AddOrUpdate("TableCircleMemberCRUD", circleId.ToString()+memberId.ToString(), r);
                    return r;
                } // using
            } // lock
            } // using
        }

        public CircleMemberRecord ReadRecordFromReader1(SqliteDataReader rdr, Guid circleId)
        {
            var result = new List<CircleMemberRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new CircleMemberRecord();
            item.circleId = circleId;

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(0, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in memberId...");
                item.memberId = new Guid(_guid);
            }

            if (rdr.IsDBNull(1))
                item.data = null;
            else
            {
                bytesRead = rdr.GetBytes(1, 0, _tmpbuf, 0, 65535+1);
                if (bytesRead > 65535)
                    throw new Exception("Too much data in data...");
                if (bytesRead < 0)
                    throw new Exception("Too little data in data...");
                item.data = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.data, 0, (int) bytesRead);
            }
            return item;
       }

        public List<CircleMemberRecord> GetCircleMembers(DatabaseConnection conn, Guid circleId)
        {
                using (var _get1Command = _database.CreateCommand())
                {
                    _get1Command.CommandText = "SELECT memberId,data FROM circleMember " +
                                                 "WHERE circleId = $circleId;";
                    var _get1Param1 = _get1Command.CreateParameter();
                    _get1Param1.ParameterName = "$circleId";
                    _get1Command.Parameters.Add(_get1Param1);

                _get1Param1.Value = circleId.ToByteArray();
                    lock (conn._lock)
                    {
                using (SqliteDataReader rdr = conn.ExecuteReader(_get1Command, System.Data.CommandBehavior.Default))
                {
                    if (!rdr.Read())
                    {
                        _cache.AddOrUpdate("TableCircleMemberCRUD", circleId.ToString(), null);
                        return null;
                    }
                    var result = new List<CircleMemberRecord>();
                    while (true)
                    {
                        result.Add(ReadRecordFromReader1(rdr, circleId));
                        if (!rdr.Read())
                            break;
                    }
                    return result;
                } // using
            } // lock
            } // using
        }

        public CircleMemberRecord ReadRecordFromReader2(SqliteDataReader rdr, Guid memberId)
        {
            var result = new List<CircleMemberRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new CircleMemberRecord();
            item.memberId = memberId;

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(0, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in circleId...");
                item.circleId = new Guid(_guid);
            }

            if (rdr.IsDBNull(1))
                item.data = null;
            else
            {
                bytesRead = rdr.GetBytes(1, 0, _tmpbuf, 0, 65535+1);
                if (bytesRead > 65535)
                    throw new Exception("Too much data in data...");
                if (bytesRead < 0)
                    throw new Exception("Too little data in data...");
                item.data = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.data, 0, (int) bytesRead);
            }
            return item;
       }

        public List<CircleMemberRecord> GetMemberCirclesAndData(DatabaseConnection conn, Guid memberId)
        {
                using (var _get2Command = _database.CreateCommand())
                {
                    _get2Command.CommandText = "SELECT circleId,data FROM circleMember " +
                                                 "WHERE memberId = $memberId;";
                    var _get2Param1 = _get2Command.CreateParameter();
                    _get2Param1.ParameterName = "$memberId";
                    _get2Command.Parameters.Add(_get2Param1);

                _get2Param1.Value = memberId.ToByteArray();
                    lock (conn._lock)
                    {
                using (SqliteDataReader rdr = conn.ExecuteReader(_get2Command, System.Data.CommandBehavior.Default))
                {
                    if (!rdr.Read())
                    {
                        _cache.AddOrUpdate("TableCircleMemberCRUD", memberId.ToString(), null);
                        return null;
                    }
                    var result = new List<CircleMemberRecord>();
                    while (true)
                    {
                        result.Add(ReadRecordFromReader2(rdr, memberId));
                        if (!rdr.Read())
                            break;
                    }
                    return result;
                } // using
            } // lock
            } // using
        }

    }
}
