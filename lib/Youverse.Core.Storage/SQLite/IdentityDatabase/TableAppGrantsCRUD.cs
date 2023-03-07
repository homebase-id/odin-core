using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Youverse.Core.Identity;

namespace Youverse.Core.Storage.Sqlite.IdentityDatabase
{
    public class AppGrantsItem
    {
        private Guid _odinHashId;
        public Guid odinHashId
        {
           get {
                   return _odinHashId;
               }
           set {
                  _odinHashId = value;
               }
        }
        private Guid _appId;
        public Guid appId
        {
           get {
                   return _appId;
               }
           set {
                  _appId = value;
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
                  if (value?.Length > 65535) throw new Exception("Too long");
                  _data = value;
               }
        }
    } // End of class AppGrantsItem

    public class TableAppGrantsCRUD : TableBase
    {
        private bool _disposed = false;
        private SqliteCommand _insertCommand = null;
        private static Object _insertLock = new Object();
        private SqliteParameter _insertParam1 = null;
        private SqliteParameter _insertParam2 = null;
        private SqliteParameter _insertParam3 = null;
        private SqliteParameter _insertParam4 = null;
        private SqliteCommand _updateCommand = null;
        private static Object _updateLock = new Object();
        private SqliteParameter _updateParam1 = null;
        private SqliteParameter _updateParam2 = null;
        private SqliteParameter _updateParam3 = null;
        private SqliteParameter _updateParam4 = null;
        private SqliteCommand _upsertCommand = null;
        private static Object _upsertLock = new Object();
        private SqliteParameter _upsertParam1 = null;
        private SqliteParameter _upsertParam2 = null;
        private SqliteParameter _upsertParam3 = null;
        private SqliteParameter _upsertParam4 = null;
        private SqliteCommand _delete0Command = null;
        private static Object _delete0Lock = new Object();
        private SqliteParameter _delete0Param1 = null;
        private SqliteCommand _get0Command = null;
        private static Object _get0Lock = new Object();
        private SqliteParameter _get0Param1 = null;

        public TableAppGrantsCRUD(IdentityDatabase db) : base(db)
        {
        }

        ~TableAppGrantsCRUD()
        {
            if (_disposed == false) throw new Exception("TableAppGrantsCRUD Not disposed properly");
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
                    cmd.CommandText = "DROP TABLE IF EXISTS appGrants;";
                    cmd.ExecuteNonQuery(_database);
                }
                cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS appGrants("
                     +"odinHashId BLOB NOT NULL, "
                     +"appId BLOB NOT NULL, "
                     +"circleId BLOB NOT NULL, "
                     +"data BLOB  "
                     +", PRIMARY KEY (odinHashId)"
                     +");"
                     ;
                cmd.ExecuteNonQuery(_database);
            }
        }

        public virtual int Insert(AppGrantsItem item)
        {
            lock (_insertLock)
            {
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand();
                    _insertCommand.CommandText = "INSERT INTO appGrants (odinHashId,appId,circleId,data) " +
                                                 "VALUES ($odinHashId,$appId,$circleId,$data)";
                    _insertParam1 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam1);
                    _insertParam1.ParameterName = "$odinHashId";
                    _insertParam2 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam2);
                    _insertParam2.ParameterName = "$appId";
                    _insertParam3 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam3);
                    _insertParam3.ParameterName = "$circleId";
                    _insertParam4 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam4);
                    _insertParam4.ParameterName = "$data";
                    _insertCommand.Prepare();
                }
                _insertParam1.Value = item.odinHashId.ToByteArray();
                _insertParam2.Value = item.appId.ToByteArray();
                _insertParam3.Value = item.circleId.ToByteArray();
                _insertParam4.Value = item.data ?? (object)DBNull.Value;
                _database.BeginTransaction();
                return _insertCommand.ExecuteNonQuery(_database);
            } // Lock
        }

        public virtual int Upsert(AppGrantsItem item)
        {
            lock (_upsertLock)
            {
                if (_upsertCommand == null)
                {
                    _upsertCommand = _database.CreateCommand();
                    _upsertCommand.CommandText = "INSERT INTO appGrants (odinHashId,appId,circleId,data) " +
                                                 "VALUES ($odinHashId,$appId,$circleId,$data)"+
                                                 "ON CONFLICT (odinHashId) DO UPDATE "+
                                                 "SET appId = $appId,circleId = $circleId,data = $data;";
                    _upsertParam1 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam1);
                    _upsertParam1.ParameterName = "$odinHashId";
                    _upsertParam2 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam2);
                    _upsertParam2.ParameterName = "$appId";
                    _upsertParam3 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam3);
                    _upsertParam3.ParameterName = "$circleId";
                    _upsertParam4 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam4);
                    _upsertParam4.ParameterName = "$data";
                    _upsertCommand.Prepare();
                }
                _upsertParam1.Value = item.odinHashId.ToByteArray();
                _upsertParam2.Value = item.appId.ToByteArray();
                _upsertParam3.Value = item.circleId.ToByteArray();
                _upsertParam4.Value = item.data ?? (object)DBNull.Value;
                _database.BeginTransaction();
                return _upsertCommand.ExecuteNonQuery(_database);
            } // Lock
        }

        public virtual int Update(AppGrantsItem item)
        {
            lock (_updateLock)
            {
                if (_updateCommand == null)
                {
                    _updateCommand = _database.CreateCommand();
                    _updateCommand.CommandText = "UPDATE appGrants " +
                                                 "SET appId = $appId,circleId = $circleId,data = $data "+
                                                 "WHERE (odinHashId = $odinHashId)";
                    _updateParam1 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam1);
                    _updateParam1.ParameterName = "$odinHashId";
                    _updateParam2 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam2);
                    _updateParam2.ParameterName = "$appId";
                    _updateParam3 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam3);
                    _updateParam3.ParameterName = "$circleId";
                    _updateParam4 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam4);
                    _updateParam4.ParameterName = "$data";
                    _updateCommand.Prepare();
                }
                _updateParam1.Value = item.odinHashId.ToByteArray();
                _updateParam2.Value = item.appId.ToByteArray();
                _updateParam3.Value = item.circleId.ToByteArray();
                _updateParam4.Value = item.data ?? (object)DBNull.Value;
                _database.BeginTransaction();
                return _updateCommand.ExecuteNonQuery(_database);
            } // Lock
        }

        public int Delete(Guid odinHashId)
        {
            lock (_delete0Lock)
            {
                if (_delete0Command == null)
                {
                    _delete0Command = _database.CreateCommand();
                    _delete0Command.CommandText = "DELETE FROM appGrants " +
                                                 "WHERE odinHashId = $odinHashId";
                    _delete0Param1 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param1);
                    _delete0Param1.ParameterName = "$odinHashId";
                    _delete0Command.Prepare();
                }
                _delete0Param1.Value = odinHashId.ToByteArray();
                _database.BeginTransaction();
                return _delete0Command.ExecuteNonQuery(_database);
            } // Lock
        }

        public AppGrantsItem Get(Guid odinHashId)
        {
            lock (_get0Lock)
            {
                if (_get0Command == null)
                {
                    _get0Command = _database.CreateCommand();
                    _get0Command.CommandText = "SELECT appId,circleId,data FROM appGrants " +
                                                 "WHERE odinHashId = $odinHashId LIMIT 1;";
                    _get0Param1 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param1);
                    _get0Param1.ParameterName = "$odinHashId";
                    _get0Command.Prepare();
                }
                _get0Param1.Value = odinHashId.ToByteArray();
                using (SqliteDataReader rdr = _get0Command.ExecuteReader(System.Data.CommandBehavior.SingleRow, _database))
                {
                    var result = new AppGrantsItem();
                    if (!rdr.Read())
                        return null;
                    byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
                    long bytesRead;
#pragma warning restore CS0168
                    var _guid = new byte[16];
                        var item = new AppGrantsItem();
                        item.odinHashId = odinHashId;

                        if (rdr.IsDBNull(0))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            bytesRead = rdr.GetBytes(0, 0, _guid, 0, 16);
                            if (bytesRead != 16)
                                throw new Exception("Not a GUID in appId...");
                            item.appId = new Guid(_guid);
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
                            bytesRead = rdr.GetBytes(2, 0, _tmpbuf, 0, 65535+1);
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

    }
}
