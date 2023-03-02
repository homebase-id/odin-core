using System;
using System.Collections.Generic;
using System.Data.SQLite;
using Youverse.Core.Identity;

namespace Youverse.Core.Storage.SQLite.IdentityDatabase
{
    public class CircleItem
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
    } // End of class CircleItem

    public class TableCircleCRUD : TableBase
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
        private SQLiteCommand _deleteCommand = null;
        private static Object _deleteLock = new Object();
        private SQLiteParameter _deleteParam1 = null;
        private SQLiteCommand _get0Command = null;
        private static Object _get0Lock = new Object();
        private SQLiteParameter _get0Param1 = null;
        private SQLiteCommand _getPaging2Command = null;
        private static Object _getPaging2Lock = new Object();
        private SQLiteParameter _getPaging2Param1 = null;
        private SQLiteParameter _getPaging2Param2 = null;

        public TableCircleCRUD(IdentityDatabase db) : base(db)
        {
        }

        ~TableCircleCRUD()
        {
            if (_disposed == false) throw new Exception("TableCircleCRUD Not disposed properly");
        }

        public override void Dispose()
        {
            _insertCommand?.Dispose();
            _insertCommand = null;
            _updateCommand?.Dispose();
            _updateCommand = null;
            _upsertCommand?.Dispose();
            _upsertCommand = null;
            _deleteCommand?.Dispose();
            _deleteCommand = null;
            _get0Command?.Dispose();
            _get0Command = null;
            _getPaging2Command?.Dispose();
            _getPaging2Command = null;
            _disposed = true;
        }

        public sealed override void EnsureTableExists(bool dropExisting = false)
        {
            using (var cmd = _database.CreateCommand())
            {
                if (dropExisting)
                {
                    cmd.CommandText = "DROP TABLE IF EXISTS circle;";
                    cmd.ExecuteNonQuery();
                }
                cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS circle("
                     +"circleName STRING NOT NULL, "
                     +"circleId BLOB NOT NULL UNIQUE, "
                     +"data BLOB  "
                     +", PRIMARY KEY (circleId)"
                     +");"
                     ;
                cmd.ExecuteNonQuery();
            }
        }

        public virtual int Insert(CircleItem item)
        {
            lock (_insertLock)
            {
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand();
                    _insertCommand.CommandText = "INSERT INTO circle (circleName,circleId,data) " +
                                                 "VALUES ($circleName,$circleId,$data)";
                    _insertParam1 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam1);
                    _insertParam1.ParameterName = "$circleName";
                    _insertParam2 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam2);
                    _insertParam2.ParameterName = "$circleId";
                    _insertParam3 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam3);
                    _insertParam3.ParameterName = "$data";
                    _insertCommand.Prepare();
                }
                _insertParam1.Value = item.circleName;
                _insertParam2.Value = item.circleId;
                _insertParam3.Value = item.data;
                _database.BeginTransaction();
                return _insertCommand.ExecuteNonQuery();
            } // Lock
        }

        public virtual int Upsert(CircleItem item)
        {
            lock (_upsertLock)
            {
                if (_upsertCommand == null)
                {
                    _upsertCommand = _database.CreateCommand();
                    _upsertCommand.CommandText = "INSERT INTO circle (circleName,circleId,data) " +
                                                 "VALUES ($circleName,$circleId,$data)"+
                                                 "ON CONFLICT (circleId) DO UPDATE "+
                                                 "SET circleName = $circleName,data = $data;";
                    _upsertParam1 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam1);
                    _upsertParam1.ParameterName = "$circleName";
                    _upsertParam2 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam2);
                    _upsertParam2.ParameterName = "$circleId";
                    _upsertParam3 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam3);
                    _upsertParam3.ParameterName = "$data";
                    _upsertCommand.Prepare();
                }
                _upsertParam1.Value = item.circleName;
                _upsertParam2.Value = item.circleId;
                _upsertParam3.Value = item.data;
                _database.BeginTransaction();
                return _upsertCommand.ExecuteNonQuery();
            } // Lock
        }

        public virtual int Update(CircleItem item)
        {
            lock (_updateLock)
            {
                if (_updateCommand == null)
                {
                    _updateCommand = _database.CreateCommand();
                    _updateCommand.CommandText = "UPDATE circle " +
                                                 "SET circleName = $circleName,data = $data "+
                                                 "WHERE (circleId = $circleId)";
                    _updateParam1 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam1);
                    _updateParam1.ParameterName = "$circleName";
                    _updateParam2 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam2);
                    _updateParam2.ParameterName = "$circleId";
                    _updateParam3 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam3);
                    _updateParam3.ParameterName = "$data";
                    _updateCommand.Prepare();
                }
                _updateParam1.Value = item.circleName;
                _updateParam2.Value = item.circleId;
                _updateParam3.Value = item.data;
                _database.BeginTransaction();
                return _updateCommand.ExecuteNonQuery();
            } // Lock
        }

        public int Delete(Guid circleId)
        {
            lock (_deleteLock)
            {
                if (_deleteCommand == null)
                {
                    _deleteCommand = _database.CreateCommand();
                    _deleteCommand.CommandText = "DELETE FROM circle " +
                                                 "WHERE circleId = $circleId";
                    _deleteParam1 = _deleteCommand.CreateParameter();
                    _deleteCommand.Parameters.Add(_deleteParam1);
                    _deleteParam1.ParameterName = "$circleId";
                    _deleteCommand.Prepare();
                }
                _deleteParam1.Value = circleId;
                _database.BeginTransaction();
                return _deleteCommand.ExecuteNonQuery();
            } // Lock
        }

        public CircleItem Get(Guid circleId)
        {
            lock (_get0Lock)
            {
                if (_get0Command == null)
                {
                    _get0Command = _database.CreateCommand();
                    _get0Command.CommandText = "SELECT circleName,data FROM circle " +
                                                 "WHERE circleId = $circleId LIMIT 1;";
                    _get0Param1 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param1);
                    _get0Param1.ParameterName = "$circleId";
                    _get0Command.Prepare();
                }
                _get0Param1.Value = circleId;
                using (SQLiteDataReader rdr = _get0Command.ExecuteReader(System.Data.CommandBehavior.SingleRow))
                {
                    var result = new CircleItem();
                    if (!rdr.Read())
                        return null;
                    byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
                    long bytesRead;
#pragma warning restore CS0168
                    var _guid = new byte[16];
                        var item = new CircleItem();
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

        public List<CircleItem> PagingByCircleId(int count, Guid? inCursor, out Guid? nextCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");
            if (inCursor == null)
                inCursor = Guid.Empty;

            lock (_getPaging2Lock)
            {
                if (_getPaging2Command == null)
                {
                    _getPaging2Command = _database.CreateCommand();
                    _getPaging2Command.CommandText = "SELECT rowid,circleName,circleId,data FROM circle " +
                                                 "WHERE circleId > $circleId ORDER BY circleId ASC LIMIT $_count;";
                    _getPaging2Param1 = _getPaging2Command.CreateParameter();
                    _getPaging2Command.Parameters.Add(_getPaging2Param1);
                    _getPaging2Param1.ParameterName = "$circleId";
                    _getPaging2Param2 = _getPaging2Command.CreateParameter();
                    _getPaging2Command.Parameters.Add(_getPaging2Param2);
                    _getPaging2Param2.ParameterName = "$_count";
                    _getPaging2Command.Prepare();
                }
                _getPaging2Param1.Value = inCursor;
                _getPaging2Param2.Value = count+1;

                using (SQLiteDataReader rdr = _getPaging2Command.ExecuteReader(System.Data.CommandBehavior.Default))
                {
                    var result = new List<CircleItem>();
                    int n = 0;
                    int rowid = 0;
                    while (rdr.Read() && (n < count))
                    {
                        n++;
                        var item = new CircleItem();
                        byte[] _tmpbuf = new byte[65535+1];
                        long bytesRead;
                        var _guid = new byte[16];

                        rowid = rdr.GetInt32(0);

                        if (rdr.IsDBNull(1))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            item.circleName = rdr.GetString(1);
                        }

                        if (rdr.IsDBNull(2))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            bytesRead = rdr.GetBytes(2, 0, _guid, 0, 16);
                            if (bytesRead != 16)
                                throw new Exception("Not a GUID in circleId...");
                            item.circleId = new Guid(_guid);
                        }

                        if (rdr.IsDBNull(3))
                            item.data = null;
                        else
                        {
                            bytesRead = rdr.GetBytes(3, 0, _tmpbuf, 0, 65000+1);
                            if (bytesRead > 65000)
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
                    } // while
                    if ((n > 0) && rdr.HasRows)
                    {
                            nextCursor = result[n - 1].circleId;
                    }
                    else
                    {
                        nextCursor = null;
                    }

                    return result;
                } // using
            } // lock
        } // PagingGet

    }
}
