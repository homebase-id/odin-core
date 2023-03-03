using System;
using System.Collections.Generic;
using System.Data.SQLite;
using Youverse.Core.Identity;

namespace Youverse.Core.Storage.SQLite.IdentityDatabase
{
    public class CircleMemberItem
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
    } // End of class CircleMemberItem

    public class TableCircleMemberCRUD : TableBase
    {
        private bool _disposed = false;
        private SQLiteCommand _insertCommand = null;
        private static Object _insertLock = new Object();
        private SQLiteParameter _insertParam1 = null;
        private SQLiteParameter _insertParam2 = null;
        private SQLiteParameter _insertParam3 = null;
        private SQLiteCommand _updateCommand = null;
        private static Object _updateLock = new Object();
        private SQLiteParameter _updateParam1 = null;
        private SQLiteParameter _updateParam2 = null;
        private SQLiteParameter _updateParam3 = null;
        private SQLiteCommand _upsertCommand = null;
        private static Object _upsertLock = new Object();
        private SQLiteParameter _upsertParam1 = null;
        private SQLiteParameter _upsertParam2 = null;
        private SQLiteParameter _upsertParam3 = null;
        private SQLiteCommand _delete0Command = null;
        private static Object _delete0Lock = new Object();
        private SQLiteParameter _delete0Param1 = null;
        private SQLiteParameter _delete0Param2 = null;
        private SQLiteCommand _delete1Command = null;
        private static Object _delete1Lock = new Object();
        private SQLiteParameter _delete1Param1 = null;
        private SQLiteCommand _get0Command = null;
        private static Object _get0Lock = new Object();
        private SQLiteParameter _get0Param1 = null;
        private SQLiteParameter _get0Param2 = null;
        private SQLiteCommand _get1Command = null;
        private static Object _get1Lock = new Object();
        private SQLiteParameter _get1Param1 = null;
        private SQLiteCommand _get2Command = null;
        private static Object _get2Lock = new Object();
        private SQLiteParameter _get2Param1 = null;

        public TableCircleMemberCRUD(IdentityDatabase db) : base(db)
        {
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
            _delete1Command?.Dispose();
            _delete1Command = null;
            _get0Command?.Dispose();
            _get0Command = null;
            _get1Command?.Dispose();
            _get1Command = null;
            _get2Command?.Dispose();
            _get2Command = null;
            _disposed = true;
        }

        public sealed override void EnsureTableExists(bool dropExisting = false)
        {
            using (var cmd = _database.CreateCommand())
            {
                if (dropExisting)
                {
                    cmd.CommandText = "DROP TABLE IF EXISTS circleMember;";
                    cmd.ExecuteNonQuery();
                }
                cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS circleMember("
                     +"circleId BLOB NOT NULL, "
                     +"memberId BLOB NOT NULL, "
                     +"data BLOB  "
                     +", PRIMARY KEY (circleId,memberId)"
                     +");"
                     ;
                cmd.ExecuteNonQuery();
            }
        }

        public virtual int Insert(CircleMemberItem item)
        {
            lock (_insertLock)
            {
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand();
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
                _insertParam1.Value = item.circleId;
                _insertParam2.Value = item.memberId;
                _insertParam3.Value = item.data;
                _database.BeginTransaction();
                return _insertCommand.ExecuteNonQuery();
            } // Lock
        }

        public virtual int Upsert(CircleMemberItem item)
        {
            lock (_upsertLock)
            {
                if (_upsertCommand == null)
                {
                    _upsertCommand = _database.CreateCommand();
                    _upsertCommand.CommandText = "INSERT INTO circleMember (circleId,memberId,data) " +
                                                 "VALUES ($circleId,$memberId,$data)"+
                                                 "ON CONFLICT (circleId,memberId) DO UPDATE "+
                                                 "SET data = $data;";
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
                _upsertParam1.Value = item.circleId;
                _upsertParam2.Value = item.memberId;
                _upsertParam3.Value = item.data;
                _database.BeginTransaction();
                return _upsertCommand.ExecuteNonQuery();
            } // Lock
        }

        public virtual int Update(CircleMemberItem item)
        {
            lock (_updateLock)
            {
                if (_updateCommand == null)
                {
                    _updateCommand = _database.CreateCommand();
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
                _updateParam1.Value = item.circleId;
                _updateParam2.Value = item.memberId;
                _updateParam3.Value = item.data;
                _database.BeginTransaction();
                return _updateCommand.ExecuteNonQuery();
            } // Lock
        }

        public int Delete(Guid circleId,Guid memberId)
        {
            lock (_delete0Lock)
            {
                if (_delete0Command == null)
                {
                    _delete0Command = _database.CreateCommand();
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
                _delete0Param1.Value = circleId;
                _delete0Param2.Value = memberId;
                _database.BeginTransaction();
                return _delete0Command.ExecuteNonQuery();
            } // Lock
        }

        public int DeleteByCircleMember(Guid memberId)
        {
            lock (_delete1Lock)
            {
                if (_delete1Command == null)
                {
                    _delete1Command = _database.CreateCommand();
                    _delete1Command.CommandText = "DELETE FROM circleMember " +
                                                 "WHERE memberId = $memberId";
                    _delete1Param1 = _delete1Command.CreateParameter();
                    _delete1Command.Parameters.Add(_delete1Param1);
                    _delete1Param1.ParameterName = "$memberId";
                    _delete1Command.Prepare();
                }
                _delete1Param1.Value = memberId;
                _database.BeginTransaction();
                return _delete1Command.ExecuteNonQuery();
            } // Lock
        }

        public CircleMemberItem Get(Guid circleId,Guid memberId)
        {
            lock (_get0Lock)
            {
                if (_get0Command == null)
                {
                    _get0Command = _database.CreateCommand();
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
                _get0Param1.Value = circleId;
                _get0Param2.Value = memberId;
                using (SQLiteDataReader rdr = _get0Command.ExecuteReader(System.Data.CommandBehavior.SingleRow))
                {
                    var result = new CircleMemberItem();
                    if (!rdr.Read())
                        return null;
                    byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
                    long bytesRead;
#pragma warning restore CS0168
                    var _guid = new byte[16];
                        var item = new CircleMemberItem();
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
                            if (bytesRead > 0)
                            {
                                item.data = new byte[bytesRead];
                                Buffer.BlockCopy(_tmpbuf, 0, item.data, 0, (int) bytesRead);
                            }
                        }
                    return item;
                } // using
            } // lock
        }

        public List<CircleMemberItem> GetCircleMembers(Guid circleId)
        {
            lock (_get1Lock)
            {
                if (_get1Command == null)
                {
                    _get1Command = _database.CreateCommand();
                    _get1Command.CommandText = "SELECT memberId,data FROM circleMember " +
                                                 "WHERE circleId = $circleId;";
                    _get1Param1 = _get1Command.CreateParameter();
                    _get1Command.Parameters.Add(_get1Param1);
                    _get1Param1.ParameterName = "$circleId";
                    _get1Command.Prepare();
                }
                _get1Param1.Value = circleId;
                using (SQLiteDataReader rdr = _get1Command.ExecuteReader(System.Data.CommandBehavior.Default))
                {
                    var result = new List<CircleMemberItem>();
                    if (!rdr.Read())
                        return null;
                    byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
                    long bytesRead;
#pragma warning restore CS0168
                    var _guid = new byte[16];
                    while (true)
                    {
                        var item = new CircleMemberItem();
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
                            if (bytesRead > 0)
                            {
                                item.data = new byte[bytesRead];
                                Buffer.BlockCopy(_tmpbuf, 0, item.data, 0, (int) bytesRead);
                            }
                        }
                        result.Add(item);
                        if (!rdr.Read())
                           break;
                    } // while
                    return result;
                } // using
            } // lock
        }

        public List<CircleMemberItem> GetMemberCirclesAndData(Guid memberId)
        {
            lock (_get2Lock)
            {
                if (_get2Command == null)
                {
                    _get2Command = _database.CreateCommand();
                    _get2Command.CommandText = "SELECT circleId,data FROM circleMember " +
                                                 "WHERE memberId = $memberId;";
                    _get2Param1 = _get2Command.CreateParameter();
                    _get2Command.Parameters.Add(_get2Param1);
                    _get2Param1.ParameterName = "$memberId";
                    _get2Command.Prepare();
                }
                _get2Param1.Value = memberId;
                using (SQLiteDataReader rdr = _get2Command.ExecuteReader(System.Data.CommandBehavior.Default))
                {
                    var result = new List<CircleMemberItem>();
                    if (!rdr.Read())
                        return null;
                    byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
                    long bytesRead;
#pragma warning restore CS0168
                    var _guid = new byte[16];
                    while (true)
                    {
                        var item = new CircleMemberItem();
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
                            if (bytesRead > 0)
                            {
                                item.data = new byte[bytesRead];
                                Buffer.BlockCopy(_tmpbuf, 0, item.data, 0, (int) bytesRead);
                            }
                        }
                        result.Add(item);
                        if (!rdr.Read())
                           break;
                    } // while
                    return result;
                } // using
            } // lock
        }

    }
}
