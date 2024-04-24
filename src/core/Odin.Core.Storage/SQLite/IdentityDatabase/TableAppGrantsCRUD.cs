using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;
using Odin.Core.Identity;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class AppGrantsRecord
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
    } // End of class AppGrantsRecord

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
        private SqliteParameter _delete0Param2 = null;
        private SqliteParameter _delete0Param3 = null;
        private SqliteCommand _get0Command = null;
        private static Object _get0Lock = new Object();
        private SqliteParameter _get0Param1 = null;
        private SqliteCommand _get1Command = null;
        private static Object _get1Lock = new Object();
        private SqliteParameter _get1Param1 = null;
        private SqliteParameter _get1Param2 = null;
        private SqliteParameter _get1Param3 = null;
        private readonly CacheHelper _cache;

        public TableAppGrantsCRUD(IdentityDatabase db, CacheHelper cache) : base(db)
        {
            _cache = cache;
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
            _get1Command?.Dispose();
            _get1Command = null;
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        public sealed override void EnsureTableExists(DatabaseBase.DatabaseConnection conn, bool dropExisting = false)
        {
                using (var cmd = _database.CreateCommand(conn))
                {
                    if (dropExisting)
                    {
                       cmd.CommandText = "DROP TABLE IF EXISTS appGrants;";
                        _database.ExecuteNonQuery(conn, cmd);
                    }
                    cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS appGrants("
                     +"odinHashId BLOB NOT NULL, "
                     +"appId BLOB NOT NULL, "
                     +"circleId BLOB NOT NULL, "
                     +"data BLOB  "
                     +", PRIMARY KEY (odinHashId,appId,circleId)"
                     +");"
                     ;
                    _database.ExecuteNonQuery(conn, cmd);
                    conn.Commit();
            }
        }

        public virtual int Insert(DatabaseBase.DatabaseConnection conn, AppGrantsRecord item)
        {
            lock (_insertLock)
            {
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand(conn);
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
                var count = _database.ExecuteNonQuery(conn, _insertCommand);
                if (count > 0)
                 {
                    _cache.AddOrUpdate("TableAppGrantsCRUD", item.odinHashId.ToString()+item.appId.ToString()+item.circleId.ToString(), item);
                 }
                return count;
            } // Lock
        }

        public virtual int Upsert(DatabaseBase.DatabaseConnection conn, AppGrantsRecord item)
        {
            lock (_upsertLock)
            {
                if (_upsertCommand == null)
                {
                    _upsertCommand = _database.CreateCommand(conn);
                    _upsertCommand.CommandText = "INSERT INTO appGrants (odinHashId,appId,circleId,data) " +
                                                 "VALUES ($odinHashId,$appId,$circleId,$data)"+
                                                 "ON CONFLICT (odinHashId,appId,circleId) DO UPDATE "+
                                                 "SET data = $data "+
                                                 ";";
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
                var count = _database.ExecuteNonQuery(conn, _upsertCommand);
                if (count > 0)
                    _cache.AddOrUpdate("TableAppGrantsCRUD", item.odinHashId.ToString()+item.appId.ToString()+item.circleId.ToString(), item);
                return count;
            } // Lock
        }
        public virtual int Update(DatabaseBase.DatabaseConnection conn, AppGrantsRecord item)
        {
            lock (_updateLock)
            {
                if (_updateCommand == null)
                {
                    _updateCommand = _database.CreateCommand(conn);
                    _updateCommand.CommandText = "UPDATE appGrants " +
                                                 "SET data = $data "+
                                                 "WHERE (odinHashId = $odinHashId,appId = $appId,circleId = $circleId)";
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
                var count = _database.ExecuteNonQuery(conn, _updateCommand);
                if (count > 0)
                {
                    _cache.AddOrUpdate("TableAppGrantsCRUD", item.odinHashId.ToString()+item.appId.ToString()+item.circleId.ToString(), item);
                }
                return count;
            } // Lock
        }

        // SELECT odinHashId,appId,circleId,data
        public AppGrantsRecord ReadRecordFromReaderAll(SqliteDataReader rdr)
        {
            var result = new List<AppGrantsRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new AppGrantsRecord();

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(0, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in odinHashId...");
                item.odinHashId = new Guid(_guid);
            }

            if (rdr.IsDBNull(1))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(1, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in appId...");
                item.appId = new Guid(_guid);
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
                bytesRead = rdr.GetBytes(3, 0, _tmpbuf, 0, 65535+1);
                if (bytesRead > 65535)
                    throw new Exception("Too much data in data...");
                if (bytesRead < 0)
                    throw new Exception("Too little data in data...");
                item.data = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.data, 0, (int) bytesRead);
            }
            return item;
       }

        public int Delete(DatabaseBase.DatabaseConnection conn, Guid odinHashId,Guid appId,Guid circleId)
        {
            lock (_delete0Lock)
            {
                if (_delete0Command == null)
                {
                    _delete0Command = _database.CreateCommand(conn);
                    _delete0Command.CommandText = "DELETE FROM appGrants " +
                                                 "WHERE odinHashId = $odinHashId AND appId = $appId AND circleId = $circleId";
                    _delete0Param1 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param1);
                    _delete0Param1.ParameterName = "$odinHashId";
                    _delete0Param2 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param2);
                    _delete0Param2.ParameterName = "$appId";
                    _delete0Param3 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param3);
                    _delete0Param3.ParameterName = "$circleId";
                    _delete0Command.Prepare();
                }
                _delete0Param1.Value = odinHashId.ToByteArray();
                _delete0Param2.Value = appId.ToByteArray();
                _delete0Param3.Value = circleId.ToByteArray();
                var count = _database.ExecuteNonQuery(conn, _delete0Command);
                if (count > 0)
                    _cache.Remove("TableAppGrantsCRUD", odinHashId.ToString()+appId.ToString()+circleId.ToString());
                return count;
            } // Lock
        }

        public AppGrantsRecord ReadRecordFromReader0(SqliteDataReader rdr, Guid odinHashId)
        {
            var result = new List<AppGrantsRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new AppGrantsRecord();
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
                item.data = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.data, 0, (int) bytesRead);
            }
            return item;
       }

        public List<AppGrantsRecord> GetByOdinHashId(DatabaseBase.DatabaseConnection conn, Guid odinHashId)
        {
            lock (_get0Lock)
            {
                if (_get0Command == null)
                {
                    _get0Command = _database.CreateCommand(conn);
                    _get0Command.CommandText = "SELECT appId,circleId,data FROM appGrants " +
                                                 "WHERE odinHashId = $odinHashId;";
                    _get0Param1 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param1);
                    _get0Param1.ParameterName = "$odinHashId";
                    _get0Command.Prepare();
                }
                _get0Param1.Value = odinHashId.ToByteArray();
                using (SqliteDataReader rdr = _database.ExecuteReader(conn, _get0Command, System.Data.CommandBehavior.Default))
                {
                    if (!rdr.Read())
                    {
                        _cache.AddOrUpdate("TableAppGrantsCRUD", odinHashId.ToString(), null);
                        return null;
                    }
                    var result = new List<AppGrantsRecord>();
                    while (true)
                    {
                        result.Add(ReadRecordFromReader0(rdr, odinHashId));
                        if (!rdr.Read())
                            break;
                    }
                    return result;
                } // using
            } // lock
        }

        public AppGrantsRecord ReadRecordFromReader1(SqliteDataReader rdr, Guid odinHashId,Guid appId,Guid circleId)
        {
            var result = new List<AppGrantsRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new AppGrantsRecord();
            item.odinHashId = odinHashId;
            item.appId = appId;
            item.circleId = circleId;

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

        public AppGrantsRecord Get(DatabaseBase.DatabaseConnection conn, Guid odinHashId,Guid appId,Guid circleId)
        {
            var (hit, cacheObject) = _cache.Get("TableAppGrantsCRUD", odinHashId.ToString()+appId.ToString()+circleId.ToString());
            if (hit)
                return (AppGrantsRecord)cacheObject;
            lock (_get1Lock)
            {
                if (_get1Command == null)
                {
                    _get1Command = _database.CreateCommand(conn);
                    _get1Command.CommandText = "SELECT data FROM appGrants " +
                                                 "WHERE odinHashId = $odinHashId AND appId = $appId AND circleId = $circleId LIMIT 1;";
                    _get1Param1 = _get1Command.CreateParameter();
                    _get1Command.Parameters.Add(_get1Param1);
                    _get1Param1.ParameterName = "$odinHashId";
                    _get1Param2 = _get1Command.CreateParameter();
                    _get1Command.Parameters.Add(_get1Param2);
                    _get1Param2.ParameterName = "$appId";
                    _get1Param3 = _get1Command.CreateParameter();
                    _get1Command.Parameters.Add(_get1Param3);
                    _get1Param3.ParameterName = "$circleId";
                    _get1Command.Prepare();
                }
                _get1Param1.Value = odinHashId.ToByteArray();
                _get1Param2.Value = appId.ToByteArray();
                _get1Param3.Value = circleId.ToByteArray();
                using (SqliteDataReader rdr = _database.ExecuteReader(conn, _get1Command, System.Data.CommandBehavior.SingleRow))
                {
                    if (!rdr.Read())
                    {
                        _cache.AddOrUpdate("TableAppGrantsCRUD", odinHashId.ToString()+appId.ToString()+circleId.ToString(), null);
                        return null;
                    }
                    var r = ReadRecordFromReader1(rdr, odinHashId,appId,circleId);
                    _cache.AddOrUpdate("TableAppGrantsCRUD", odinHashId.ToString()+appId.ToString()+circleId.ToString(), r);
                    return r;
                } // using
            } // lock
        }

    }
}
