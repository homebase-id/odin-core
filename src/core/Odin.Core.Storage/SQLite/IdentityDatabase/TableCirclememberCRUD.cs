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
        private SqliteCommand _insertCommand = null;
        private static Object _insertLock = new Object();
        private SqliteParameter _insertParam1 = null;
        private SqliteParameter _insertParam2 = null;
        private SqliteParameter _insertParam3 = null;
        private SqliteCommand _updateCommand = null;
        private static Object _updateLock = new Object();
        private SqliteParameter _updateParam1 = null;
        private SqliteParameter _updateParam2 = null;
        private SqliteParameter _updateParam3 = null;
        private SqliteCommand _upsertCommand = null;
        private static Object _upsertLock = new Object();
        private SqliteParameter _upsertParam1 = null;
        private SqliteParameter _upsertParam2 = null;
        private SqliteParameter _upsertParam3 = null;
        private SqliteCommand _delete0Command = null;
        private static Object _delete0Lock = new Object();
        private SqliteParameter _delete0Param1 = null;
        private SqliteParameter _delete0Param2 = null;
        private SqliteCommand _get0Command = null;
        private static Object _get0Lock = new Object();
        private SqliteParameter _get0Param1 = null;
        private SqliteParameter _get0Param2 = null;
        private SqliteCommand _get1Command = null;
        private static Object _get1Lock = new Object();
        private SqliteParameter _get1Param1 = null;
        private SqliteCommand _get2Command = null;
        private static Object _get2Lock = new Object();
        private SqliteParameter _get2Param1 = null;
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
            _get1Command?.Dispose();
            _get1Command = null;
            _get2Command?.Dispose();
            _get2Command = null;
            _disposed = true;
        }

        public sealed override void EnsureTableExists(DatabaseBase.DatabaseConnection conn, bool dropExisting = false)
        {
                using (var cmd = _database.CreateCommand(conn))
                {
                    if (dropExisting)
                    {
                       cmd.CommandText = "DROP TABLE IF EXISTS circleMember;";
                        _database.ExecuteNonQuery(conn, cmd);
                    }
                    cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS circleMember("
                     +"circleId BLOB NOT NULL, "
                     +"memberId BLOB NOT NULL, "
                     +"data BLOB  "
                     +", PRIMARY KEY (circleId,memberId)"
                     +");"
                     ;
                    _database.ExecuteNonQuery(conn, cmd);
                    conn.Commit();
            }
        }

        public virtual int Insert(DatabaseBase.DatabaseConnection conn, CircleMemberRecord item)
        {
            lock (_insertLock)
            {
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand(conn);
                    _insertCommand.CommandText = "INSERT INTO circleMember (circleId,memberId,data) " +
                                                 "VALUES ($circleId,$memberId,$data)";
                    _insertParam1 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam1);
                    _insertParam1.ParameterName = "$circleId";
                    _insertParam2 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam2);
                    _insertParam2.ParameterName = "$memberId";
                    _insertParam3 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam3);
                    _insertParam3.ParameterName = "$data";
                    _insertCommand.Prepare();
                }
                _insertParam1.Value = item.circleId.ToByteArray();
                _insertParam2.Value = item.memberId.ToByteArray();
                _insertParam3.Value = item.data ?? (object)DBNull.Value;
                var count = _database.ExecuteNonQuery(conn, _insertCommand);
                if (count > 0)
                 {
                    _cache.AddOrUpdate("TableCircleMemberCRUD", item.circleId.ToString()+item.memberId.ToString(), item);
                 }
                return count;
            } // Lock
        }

        public virtual int Upsert(DatabaseBase.DatabaseConnection conn, CircleMemberRecord item)
        {
            lock (_upsertLock)
            {
                if (_upsertCommand == null)
                {
                    _upsertCommand = _database.CreateCommand(conn);
                    _upsertCommand.CommandText = "INSERT INTO circleMember (circleId,memberId,data) " +
                                                 "VALUES ($circleId,$memberId,$data)"+
                                                 "ON CONFLICT (circleId,memberId) DO UPDATE "+
                                                 "SET data = $data "+
                                                 ";";
                    _upsertParam1 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam1);
                    _upsertParam1.ParameterName = "$circleId";
                    _upsertParam2 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam2);
                    _upsertParam2.ParameterName = "$memberId";
                    _upsertParam3 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam3);
                    _upsertParam3.ParameterName = "$data";
                    _upsertCommand.Prepare();
                }
                _upsertParam1.Value = item.circleId.ToByteArray();
                _upsertParam2.Value = item.memberId.ToByteArray();
                _upsertParam3.Value = item.data ?? (object)DBNull.Value;
                var count = _database.ExecuteNonQuery(conn, _upsertCommand);
                if (count > 0)
                    _cache.AddOrUpdate("TableCircleMemberCRUD", item.circleId.ToString()+item.memberId.ToString(), item);
                return count;
            } // Lock
        }
        public virtual int Update(DatabaseBase.DatabaseConnection conn, CircleMemberRecord item)
        {
            lock (_updateLock)
            {
                if (_updateCommand == null)
                {
                    _updateCommand = _database.CreateCommand(conn);
                    _updateCommand.CommandText = "UPDATE circleMember " +
                                                 "SET data = $data "+
                                                 "WHERE (circleId = $circleId,memberId = $memberId)";
                    _updateParam1 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam1);
                    _updateParam1.ParameterName = "$circleId";
                    _updateParam2 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam2);
                    _updateParam2.ParameterName = "$memberId";
                    _updateParam3 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam3);
                    _updateParam3.ParameterName = "$data";
                    _updateCommand.Prepare();
                }
                _updateParam1.Value = item.circleId.ToByteArray();
                _updateParam2.Value = item.memberId.ToByteArray();
                _updateParam3.Value = item.data ?? (object)DBNull.Value;
                var count = _database.ExecuteNonQuery(conn, _updateCommand);
                if (count > 0)
                {
                    _cache.AddOrUpdate("TableCircleMemberCRUD", item.circleId.ToString()+item.memberId.ToString(), item);
                }
                return count;
            } // Lock
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

        public int Delete(DatabaseBase.DatabaseConnection conn, Guid circleId,Guid memberId)
        {
            lock (_delete0Lock)
            {
                if (_delete0Command == null)
                {
                    _delete0Command = _database.CreateCommand(conn);
                    _delete0Command.CommandText = "DELETE FROM circleMember " +
                                                 "WHERE circleId = $circleId AND memberId = $memberId";
                    _delete0Param1 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param1);
                    _delete0Param1.ParameterName = "$circleId";
                    _delete0Param2 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param2);
                    _delete0Param2.ParameterName = "$memberId";
                    _delete0Command.Prepare();
                }
                _delete0Param1.Value = circleId.ToByteArray();
                _delete0Param2.Value = memberId.ToByteArray();
                var count = _database.ExecuteNonQuery(conn, _delete0Command);
                if (count > 0)
                    _cache.Remove("TableCircleMemberCRUD", circleId.ToString()+memberId.ToString());
                return count;
            } // Lock
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

        public CircleMemberRecord Get(DatabaseBase.DatabaseConnection conn, Guid circleId,Guid memberId)
        {
            var (hit, cacheObject) = _cache.Get("TableCircleMemberCRUD", circleId.ToString()+memberId.ToString());
            if (hit)
                return (CircleMemberRecord)cacheObject;
            lock (_get0Lock)
            {
                if (_get0Command == null)
                {
                    _get0Command = _database.CreateCommand(conn);
                    _get0Command.CommandText = "SELECT data FROM circleMember " +
                                                 "WHERE circleId = $circleId AND memberId = $memberId LIMIT 1;";
                    _get0Param1 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param1);
                    _get0Param1.ParameterName = "$circleId";
                    _get0Param2 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param2);
                    _get0Param2.ParameterName = "$memberId";
                    _get0Command.Prepare();
                }
                _get0Param1.Value = circleId.ToByteArray();
                _get0Param2.Value = memberId.ToByteArray();
                using (SqliteDataReader rdr = _database.ExecuteReader(conn, _get0Command, System.Data.CommandBehavior.SingleRow))
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

        public List<CircleMemberRecord> GetCircleMembers(DatabaseBase.DatabaseConnection conn, Guid circleId)
        {
            lock (_get1Lock)
            {
                if (_get1Command == null)
                {
                    _get1Command = _database.CreateCommand(conn);
                    _get1Command.CommandText = "SELECT memberId,data FROM circleMember " +
                                                 "WHERE circleId = $circleId;";
                    _get1Param1 = _get1Command.CreateParameter();
                    _get1Command.Parameters.Add(_get1Param1);
                    _get1Param1.ParameterName = "$circleId";
                    _get1Command.Prepare();
                }
                _get1Param1.Value = circleId.ToByteArray();
                using (SqliteDataReader rdr = _database.ExecuteReader(conn, _get1Command, System.Data.CommandBehavior.Default))
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

        public List<CircleMemberRecord> GetMemberCirclesAndData(DatabaseBase.DatabaseConnection conn, Guid memberId)
        {
            lock (_get2Lock)
            {
                if (_get2Command == null)
                {
                    _get2Command = _database.CreateCommand(conn);
                    _get2Command.CommandText = "SELECT circleId,data FROM circleMember " +
                                                 "WHERE memberId = $memberId;";
                    _get2Param1 = _get2Command.CreateParameter();
                    _get2Command.Parameters.Add(_get2Param1);
                    _get2Param1.ParameterName = "$memberId";
                    _get2Command.Prepare();
                }
                _get2Param1.Value = memberId.ToByteArray();
                using (SqliteDataReader rdr = _database.ExecuteReader(conn, _get2Command, System.Data.CommandBehavior.Default))
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
        }

    }
}
