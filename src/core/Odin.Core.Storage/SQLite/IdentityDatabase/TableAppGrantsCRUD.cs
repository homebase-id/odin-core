using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;
using Odin.Core.Identity;
using System.Runtime.CompilerServices;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class AppGrantsRecord
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
        private readonly CacheHelper _cache;

        public TableAppGrantsCRUD(CacheHelper cache) : base("appGrants")
        {
            _cache = cache;
        }

        ~TableAppGrantsCRUD()
        {
            if (_disposed == false) throw new Exception("TableAppGrantsCRUD Not disposed properly");
        }

        public override void Dispose()
        {
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        public sealed override void EnsureTableExists(DatabaseConnection conn, bool dropExisting = false)
        {
                using (var cmd = conn.db.CreateCommand())
                {
                    if (dropExisting)
                    {
                       cmd.CommandText = "DROP TABLE IF EXISTS appGrants;";
                       conn.ExecuteNonQuery(cmd);
                    }
                    cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS appGrants("
                     +"identityId BLOB NOT NULL, "
                     +"odinHashId BLOB NOT NULL, "
                     +"appId BLOB NOT NULL, "
                     +"circleId BLOB NOT NULL, "
                     +"data BLOB  "
                     +", PRIMARY KEY (identityId,odinHashId,appId,circleId)"
                     +");"
                     ;
                    conn.ExecuteNonQuery(cmd);
            }
        }

        internal virtual int Insert(DatabaseConnection conn, AppGrantsRecord item)
        {
            DatabaseBase.AssertGuidNotEmpty(item.identityId, "Guid parameter identityId cannot be set to Empty GUID.");
            DatabaseBase.AssertGuidNotEmpty(item.odinHashId, "Guid parameter odinHashId cannot be set to Empty GUID.");
            DatabaseBase.AssertGuidNotEmpty(item.appId, "Guid parameter appId cannot be set to Empty GUID.");
            DatabaseBase.AssertGuidNotEmpty(item.circleId, "Guid parameter circleId cannot be set to Empty GUID.");
            using (var _insertCommand = conn.db.CreateCommand())
            {
                _insertCommand.CommandText = "INSERT INTO appGrants (identityId,odinHashId,appId,circleId,data) " +
                                             "VALUES (@identityId,@odinHashId,@appId,@circleId,@data)";
                var _insertParam1 = _insertCommand.CreateParameter();
                _insertParam1.ParameterName = "@identityId";
                _insertCommand.Parameters.Add(_insertParam1);
                var _insertParam2 = _insertCommand.CreateParameter();
                _insertParam2.ParameterName = "@odinHashId";
                _insertCommand.Parameters.Add(_insertParam2);
                var _insertParam3 = _insertCommand.CreateParameter();
                _insertParam3.ParameterName = "@appId";
                _insertCommand.Parameters.Add(_insertParam3);
                var _insertParam4 = _insertCommand.CreateParameter();
                _insertParam4.ParameterName = "@circleId";
                _insertCommand.Parameters.Add(_insertParam4);
                var _insertParam5 = _insertCommand.CreateParameter();
                _insertParam5.ParameterName = "@data";
                _insertCommand.Parameters.Add(_insertParam5);
                _insertParam1.Value = item.identityId.ToByteArray();
                _insertParam2.Value = item.odinHashId.ToByteArray();
                _insertParam3.Value = item.appId.ToByteArray();
                _insertParam4.Value = item.circleId.ToByteArray();
                _insertParam5.Value = item.data ?? (object)DBNull.Value;
                var count = conn.ExecuteNonQuery(_insertCommand);
                if (count > 0)
                {
                    _cache.AddOrUpdate("TableAppGrantsCRUD", item.identityId.ToString()+item.odinHashId.ToString()+item.appId.ToString()+item.circleId.ToString(), item);
                }
                return count;
            } // Using
        }

        internal virtual int TryInsert(DatabaseConnection conn, AppGrantsRecord item)
        {
            DatabaseBase.AssertGuidNotEmpty(item.identityId, "Guid parameter identityId cannot be set to Empty GUID.");
            DatabaseBase.AssertGuidNotEmpty(item.odinHashId, "Guid parameter odinHashId cannot be set to Empty GUID.");
            DatabaseBase.AssertGuidNotEmpty(item.appId, "Guid parameter appId cannot be set to Empty GUID.");
            DatabaseBase.AssertGuidNotEmpty(item.circleId, "Guid parameter circleId cannot be set to Empty GUID.");
            using (var _insertCommand = conn.db.CreateCommand())
            {
                _insertCommand.CommandText = "INSERT OR IGNORE INTO appGrants (identityId,odinHashId,appId,circleId,data) " +
                                             "VALUES (@identityId,@odinHashId,@appId,@circleId,@data)";
                var _insertParam1 = _insertCommand.CreateParameter();
                _insertParam1.ParameterName = "@identityId";
                _insertCommand.Parameters.Add(_insertParam1);
                var _insertParam2 = _insertCommand.CreateParameter();
                _insertParam2.ParameterName = "@odinHashId";
                _insertCommand.Parameters.Add(_insertParam2);
                var _insertParam3 = _insertCommand.CreateParameter();
                _insertParam3.ParameterName = "@appId";
                _insertCommand.Parameters.Add(_insertParam3);
                var _insertParam4 = _insertCommand.CreateParameter();
                _insertParam4.ParameterName = "@circleId";
                _insertCommand.Parameters.Add(_insertParam4);
                var _insertParam5 = _insertCommand.CreateParameter();
                _insertParam5.ParameterName = "@data";
                _insertCommand.Parameters.Add(_insertParam5);
                _insertParam1.Value = item.identityId.ToByteArray();
                _insertParam2.Value = item.odinHashId.ToByteArray();
                _insertParam3.Value = item.appId.ToByteArray();
                _insertParam4.Value = item.circleId.ToByteArray();
                _insertParam5.Value = item.data ?? (object)DBNull.Value;
                var count = conn.ExecuteNonQuery(_insertCommand);
                if (count > 0)
                {
                   _cache.AddOrUpdate("TableAppGrantsCRUD", item.identityId.ToString()+item.odinHashId.ToString()+item.appId.ToString()+item.circleId.ToString(), item);
                }
                return count;
            } // Using
        }

        internal virtual int Upsert(DatabaseConnection conn, AppGrantsRecord item)
        {
            DatabaseBase.AssertGuidNotEmpty(item.identityId, "Guid parameter identityId cannot be set to Empty GUID.");
            DatabaseBase.AssertGuidNotEmpty(item.odinHashId, "Guid parameter odinHashId cannot be set to Empty GUID.");
            DatabaseBase.AssertGuidNotEmpty(item.appId, "Guid parameter appId cannot be set to Empty GUID.");
            DatabaseBase.AssertGuidNotEmpty(item.circleId, "Guid parameter circleId cannot be set to Empty GUID.");
            using (var _upsertCommand = conn.db.CreateCommand())
            {
                _upsertCommand.CommandText = "INSERT INTO appGrants (identityId,odinHashId,appId,circleId,data) " +
                                             "VALUES (@identityId,@odinHashId,@appId,@circleId,@data)"+
                                             "ON CONFLICT (identityId,odinHashId,appId,circleId) DO UPDATE "+
                                             "SET data = @data "+
                                             ";";
                var _upsertParam1 = _upsertCommand.CreateParameter();
                _upsertParam1.ParameterName = "@identityId";
                _upsertCommand.Parameters.Add(_upsertParam1);
                var _upsertParam2 = _upsertCommand.CreateParameter();
                _upsertParam2.ParameterName = "@odinHashId";
                _upsertCommand.Parameters.Add(_upsertParam2);
                var _upsertParam3 = _upsertCommand.CreateParameter();
                _upsertParam3.ParameterName = "@appId";
                _upsertCommand.Parameters.Add(_upsertParam3);
                var _upsertParam4 = _upsertCommand.CreateParameter();
                _upsertParam4.ParameterName = "@circleId";
                _upsertCommand.Parameters.Add(_upsertParam4);
                var _upsertParam5 = _upsertCommand.CreateParameter();
                _upsertParam5.ParameterName = "@data";
                _upsertCommand.Parameters.Add(_upsertParam5);
                _upsertParam1.Value = item.identityId.ToByteArray();
                _upsertParam2.Value = item.odinHashId.ToByteArray();
                _upsertParam3.Value = item.appId.ToByteArray();
                _upsertParam4.Value = item.circleId.ToByteArray();
                _upsertParam5.Value = item.data ?? (object)DBNull.Value;
                var count = conn.ExecuteNonQuery(_upsertCommand);
                if (count > 0)
                    _cache.AddOrUpdate("TableAppGrantsCRUD", item.identityId.ToString()+item.odinHashId.ToString()+item.appId.ToString()+item.circleId.ToString(), item);
                return count;
            } // Using
        }
        internal virtual int Update(DatabaseConnection conn, AppGrantsRecord item)
        {
            DatabaseBase.AssertGuidNotEmpty(item.identityId, "Guid parameter identityId cannot be set to Empty GUID.");
            DatabaseBase.AssertGuidNotEmpty(item.odinHashId, "Guid parameter odinHashId cannot be set to Empty GUID.");
            DatabaseBase.AssertGuidNotEmpty(item.appId, "Guid parameter appId cannot be set to Empty GUID.");
            DatabaseBase.AssertGuidNotEmpty(item.circleId, "Guid parameter circleId cannot be set to Empty GUID.");
            using (var _updateCommand = conn.db.CreateCommand())
            {
                _updateCommand.CommandText = "UPDATE appGrants " +
                                             "SET data = @data "+
                                             "WHERE (identityId = @identityId AND odinHashId = @odinHashId AND appId = @appId AND circleId = @circleId)";
                var _updateParam1 = _updateCommand.CreateParameter();
                _updateParam1.ParameterName = "@identityId";
                _updateCommand.Parameters.Add(_updateParam1);
                var _updateParam2 = _updateCommand.CreateParameter();
                _updateParam2.ParameterName = "@odinHashId";
                _updateCommand.Parameters.Add(_updateParam2);
                var _updateParam3 = _updateCommand.CreateParameter();
                _updateParam3.ParameterName = "@appId";
                _updateCommand.Parameters.Add(_updateParam3);
                var _updateParam4 = _updateCommand.CreateParameter();
                _updateParam4.ParameterName = "@circleId";
                _updateCommand.Parameters.Add(_updateParam4);
                var _updateParam5 = _updateCommand.CreateParameter();
                _updateParam5.ParameterName = "@data";
                _updateCommand.Parameters.Add(_updateParam5);
                _updateParam1.Value = item.identityId.ToByteArray();
                _updateParam2.Value = item.odinHashId.ToByteArray();
                _updateParam3.Value = item.appId.ToByteArray();
                _updateParam4.Value = item.circleId.ToByteArray();
                _updateParam5.Value = item.data ?? (object)DBNull.Value;
                var count = conn.ExecuteNonQuery(_updateCommand);
                if (count > 0)
                {
                    _cache.AddOrUpdate("TableAppGrantsCRUD", item.identityId.ToString()+item.odinHashId.ToString()+item.appId.ToString()+item.circleId.ToString(), item);
                }
                return count;
            } // Using
        }

        internal virtual int GetCountDirty(DatabaseConnection conn)
        {
            using (var _getCountCommand = conn.db.CreateCommand())
            {
                _getCountCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM appGrants; PRAGMA read_uncommitted = 0;";
                var count = conn.ExecuteScalar(_getCountCommand);
                if (count == null || count == DBNull.Value || !(count is int || count is long))
                    return -1;
                else
                    return Convert.ToInt32(count);
            }
        }

        public override List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("identityId");
            sl.Add("odinHashId");
            sl.Add("appId");
            sl.Add("circleId");
            sl.Add("data");
            return sl;
        }

        // SELECT identityId,odinHashId,appId,circleId,data
        internal AppGrantsRecord ReadRecordFromReaderAll(SqliteDataReader rdr)
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
                    throw new Exception("Not a GUID in identityId...");
                item.identityId = new Guid(_guid);
            }

            if (rdr.IsDBNull(1))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(1, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in odinHashId...");
                item.odinHashId = new Guid(_guid);
            }

            if (rdr.IsDBNull(2))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(2, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in appId...");
                item.appId = new Guid(_guid);
            }

            if (rdr.IsDBNull(3))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(3, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in circleId...");
                item.circleId = new Guid(_guid);
            }

            if (rdr.IsDBNull(4))
                item.data = null;
            else
            {
                bytesRead = rdr.GetBytes(4, 0, _tmpbuf, 0, 65535+1);
                if (bytesRead > 65535)
                    throw new Exception("Too much data in data...");
                if (bytesRead < 0)
                    throw new Exception("Too little data in data...");
                item.data = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.data, 0, (int) bytesRead);
            }
            return item;
       }

        internal int Delete(DatabaseConnection conn, Guid identityId,Guid odinHashId,Guid appId,Guid circleId)
        {
            using (var _delete0Command = conn.db.CreateCommand())
            {
                _delete0Command.CommandText = "DELETE FROM appGrants " +
                                             "WHERE identityId = @identityId AND odinHashId = @odinHashId AND appId = @appId AND circleId = @circleId";
                var _delete0Param1 = _delete0Command.CreateParameter();
                _delete0Param1.ParameterName = "@identityId";
                _delete0Command.Parameters.Add(_delete0Param1);
                var _delete0Param2 = _delete0Command.CreateParameter();
                _delete0Param2.ParameterName = "@odinHashId";
                _delete0Command.Parameters.Add(_delete0Param2);
                var _delete0Param3 = _delete0Command.CreateParameter();
                _delete0Param3.ParameterName = "@appId";
                _delete0Command.Parameters.Add(_delete0Param3);
                var _delete0Param4 = _delete0Command.CreateParameter();
                _delete0Param4.ParameterName = "@circleId";
                _delete0Command.Parameters.Add(_delete0Param4);

                _delete0Param1.Value = identityId.ToByteArray();
                _delete0Param2.Value = odinHashId.ToByteArray();
                _delete0Param3.Value = appId.ToByteArray();
                _delete0Param4.Value = circleId.ToByteArray();
                var count = conn.ExecuteNonQuery(_delete0Command);
                if (count > 0)
                    _cache.Remove("TableAppGrantsCRUD", identityId.ToString()+odinHashId.ToString()+appId.ToString()+circleId.ToString());
                return count;
            } // Using
        }

        internal AppGrantsRecord ReadRecordFromReader0(SqliteDataReader rdr, Guid identityId,Guid odinHashId)
        {
            var result = new List<AppGrantsRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new AppGrantsRecord();
            item.identityId = identityId;
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

        internal List<AppGrantsRecord> GetByOdinHashId(DatabaseConnection conn, Guid identityId,Guid odinHashId)
        {
            using (var _get0Command = conn.db.CreateCommand())
            {
                _get0Command.CommandText = "SELECT appId,circleId,data FROM appGrants " +
                                             "WHERE identityId = @identityId AND odinHashId = @odinHashId;";
                var _get0Param1 = _get0Command.CreateParameter();
                _get0Param1.ParameterName = "@identityId";
                _get0Command.Parameters.Add(_get0Param1);
                var _get0Param2 = _get0Command.CreateParameter();
                _get0Param2.ParameterName = "@odinHashId";
                _get0Command.Parameters.Add(_get0Param2);

                _get0Param1.Value = identityId.ToByteArray();
                _get0Param2.Value = odinHashId.ToByteArray();
                lock (conn._lock)
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_get0Command, System.Data.CommandBehavior.Default))
                    {
                        if (!rdr.Read())
                        {
                            _cache.AddOrUpdate("TableAppGrantsCRUD", identityId.ToString()+odinHashId.ToString(), null);
                            return new List<AppGrantsRecord>();
                        }
                        var result = new List<AppGrantsRecord>();
                        while (true)
                        {
                            result.Add(ReadRecordFromReader0(rdr, identityId,odinHashId));
                            if (!rdr.Read())
                                break;
                        }
                        return result;
                    } // using
                } // lock
            } // using
        }

        internal AppGrantsRecord ReadRecordFromReader1(SqliteDataReader rdr, Guid identityId,Guid odinHashId,Guid appId,Guid circleId)
        {
            var result = new List<AppGrantsRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new AppGrantsRecord();
            item.identityId = identityId;
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

        internal AppGrantsRecord Get(DatabaseConnection conn, Guid identityId,Guid odinHashId,Guid appId,Guid circleId)
        {
            var (hit, cacheObject) = _cache.Get("TableAppGrantsCRUD", identityId.ToString()+odinHashId.ToString()+appId.ToString()+circleId.ToString());
            if (hit)
                return (AppGrantsRecord)cacheObject;
            using (var _get1Command = conn.db.CreateCommand())
            {
                _get1Command.CommandText = "SELECT data FROM appGrants " +
                                             "WHERE identityId = @identityId AND odinHashId = @odinHashId AND appId = @appId AND circleId = @circleId LIMIT 1;";
                var _get1Param1 = _get1Command.CreateParameter();
                _get1Param1.ParameterName = "@identityId";
                _get1Command.Parameters.Add(_get1Param1);
                var _get1Param2 = _get1Command.CreateParameter();
                _get1Param2.ParameterName = "@odinHashId";
                _get1Command.Parameters.Add(_get1Param2);
                var _get1Param3 = _get1Command.CreateParameter();
                _get1Param3.ParameterName = "@appId";
                _get1Command.Parameters.Add(_get1Param3);
                var _get1Param4 = _get1Command.CreateParameter();
                _get1Param4.ParameterName = "@circleId";
                _get1Command.Parameters.Add(_get1Param4);

                _get1Param1.Value = identityId.ToByteArray();
                _get1Param2.Value = odinHashId.ToByteArray();
                _get1Param3.Value = appId.ToByteArray();
                _get1Param4.Value = circleId.ToByteArray();
                lock (conn._lock)
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_get1Command, System.Data.CommandBehavior.SingleRow))
                    {
                        if (!rdr.Read())
                        {
                            _cache.AddOrUpdate("TableAppGrantsCRUD", identityId.ToString()+odinHashId.ToString()+appId.ToString()+circleId.ToString(), null);
                            return null;
                        }
                        var r = ReadRecordFromReader1(rdr, identityId,odinHashId,appId,circleId);
                        _cache.AddOrUpdate("TableAppGrantsCRUD", identityId.ToString()+odinHashId.ToString()+appId.ToString()+circleId.ToString(), r);
                        return r;
                    } // using
                } // lock
            } // using
        }

    }
}
