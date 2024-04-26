using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;
using Odin.Core.Identity;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class FollowsMeRecord
    {
        private string _identity;
        public string identity
        {
           get {
                   return _identity;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 3) throw new Exception("Too short");
                    if (value?.Length > 255) throw new Exception("Too long");
                  _identity = value;
               }
        }
        private Guid _driveId;
        public Guid driveId
        {
           get {
                   return _driveId;
               }
           set {
                  _driveId = value;
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
    } // End of class FollowsMeRecord

    public class TableFollowsMeCRUD : TableBase
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
        private SqliteCommand _get0Command = null;
        private static Object _get0Lock = new Object();
        private SqliteParameter _get0Param1 = null;
        private SqliteParameter _get0Param2 = null;
        private SqliteCommand _get1Command = null;
        private static Object _get1Lock = new Object();
        private SqliteParameter _get1Param1 = null;
        private readonly CacheHelper _cache;

        public TableFollowsMeCRUD(IdentityDatabase db, CacheHelper cache) : base(db)
        {
            _cache = cache;
        }

        ~TableFollowsMeCRUD()
        {
            if (_disposed == false) throw new Exception("TableFollowsMeCRUD Not disposed properly");
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
                       cmd.CommandText = "DROP TABLE IF EXISTS followsMe;";
                        _database.ExecuteNonQuery(conn, cmd);
                    }
                    cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS followsMe("
                     +"identity STRING NOT NULL, "
                     +"driveId BLOB NOT NULL, "
                     +"created INT NOT NULL, "
                     +"modified INT  "
                     +", PRIMARY KEY (identity,driveId)"
                     +");"
                     +"CREATE INDEX IF NOT EXISTS Idx0TableFollowsMeCRUD ON followsMe(identity);"
                     ;
                    _database.ExecuteNonQuery(conn, cmd);
            }
        }

        public virtual int Insert(DatabaseBase.DatabaseConnection conn, FollowsMeRecord item)
        {
            lock (_insertLock)
            {
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand(conn);
                    _insertCommand.CommandText = "INSERT INTO followsMe (identity,driveId,created,modified) " +
                                                 "VALUES ($identity,$driveId,$created,$modified)";
                    _insertParam1 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam1);
                    _insertParam1.ParameterName = "$identity";
                    _insertParam2 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam2);
                    _insertParam2.ParameterName = "$driveId";
                    _insertParam3 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam3);
                    _insertParam3.ParameterName = "$created";
                    _insertParam4 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam4);
                    _insertParam4.ParameterName = "$modified";
                    _insertCommand.Prepare();
                }
                _insertParam1.Value = item.identity;
                _insertParam2.Value = item.driveId.ToByteArray();
                var now = UnixTimeUtcUnique.Now();
                _insertParam3.Value = now.uniqueTime;
                item.modified = null;
                _insertParam4.Value = DBNull.Value;
                var count = _database.ExecuteNonQuery(conn, _insertCommand);
                if (count > 0)
                 {
                     item.created = now;
                    _cache.AddOrUpdate("TableFollowsMeCRUD", item.identity+item.driveId.ToString(), item);
                 }
                return count;
            } // Lock
        }

        public virtual int Upsert(DatabaseBase.DatabaseConnection conn, FollowsMeRecord item)
        {
            lock (_upsertLock)
            {
                if (_upsertCommand == null)
                {
                    _upsertCommand = _database.CreateCommand(conn);
                    _upsertCommand.CommandText = "INSERT INTO followsMe (identity,driveId,created) " +
                                                 "VALUES ($identity,$driveId,$created)"+
                                                 "ON CONFLICT (identity,driveId) DO UPDATE "+
                                                 "SET modified = $modified "+
                                                 "RETURNING created, modified;";
                    _upsertParam1 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam1);
                    _upsertParam1.ParameterName = "$identity";
                    _upsertParam2 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam2);
                    _upsertParam2.ParameterName = "$driveId";
                    _upsertParam3 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam3);
                    _upsertParam3.ParameterName = "$created";
                    _upsertParam4 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam4);
                    _upsertParam4.ParameterName = "$modified";
                    _upsertCommand.Prepare();
                }
                var now = UnixTimeUtcUnique.Now();
                _upsertParam1.Value = item.identity;
                _upsertParam2.Value = item.driveId.ToByteArray();
                _upsertParam3.Value = now.uniqueTime;
                _upsertParam4.Value = now.uniqueTime;
                using (SqliteDataReader rdr = _database.ExecuteReader(conn, _upsertCommand, System.Data.CommandBehavior.SingleRow))
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
                      _cache.AddOrUpdate("TableFollowsMeCRUD", item.identity+item.driveId.ToString(), item);
                      return 1;
                   }
                }
            } // Lock
            return 0;
        }

        public virtual int Update(DatabaseBase.DatabaseConnection conn, FollowsMeRecord item)
        {
            lock (_updateLock)
            {
                if (_updateCommand == null)
                {
                    _updateCommand = _database.CreateCommand(conn);
                    _updateCommand.CommandText = "UPDATE followsMe " +
                                                 "SET modified = $modified "+
                                                 "WHERE (identity = $identity,driveId = $driveId)";
                    _updateParam1 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam1);
                    _updateParam1.ParameterName = "$identity";
                    _updateParam2 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam2);
                    _updateParam2.ParameterName = "$driveId";
                    _updateParam3 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam3);
                    _updateParam3.ParameterName = "$created";
                    _updateParam4 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam4);
                    _updateParam4.ParameterName = "$modified";
                    _updateCommand.Prepare();
                }
                var now = UnixTimeUtcUnique.Now();
                _updateParam1.Value = item.identity;
                _updateParam2.Value = item.driveId.ToByteArray();
                _updateParam3.Value = now.uniqueTime;
                _updateParam4.Value = now.uniqueTime;
                var count = _database.ExecuteNonQuery(conn, _updateCommand);
                if (count > 0)
                {
                     item.modified = now;
                    _cache.AddOrUpdate("TableFollowsMeCRUD", item.identity+item.driveId.ToString(), item);
                }
                return count;
            } // Lock
        }

        // SELECT identity,driveId,created,modified
        public FollowsMeRecord ReadRecordFromReaderAll(SqliteDataReader rdr)
        {
            var result = new List<FollowsMeRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new FollowsMeRecord();

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.identity = rdr.GetString(0);
            }

            if (rdr.IsDBNull(1))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(1, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in driveId...");
                item.driveId = new Guid(_guid);
            }

            if (rdr.IsDBNull(2))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.created = new UnixTimeUtcUnique(rdr.GetInt64(2));
            }

            if (rdr.IsDBNull(3))
                item.modified = null;
            else
            {
                item.modified = new UnixTimeUtcUnique(rdr.GetInt64(3));
            }
            return item;
       }

        public int Delete(DatabaseBase.DatabaseConnection conn, string identity,Guid driveId)
        {
            if (identity == null) throw new Exception("Cannot be null");
            if (identity?.Length < 3) throw new Exception("Too short");
            if (identity?.Length > 255) throw new Exception("Too long");
            lock (_delete0Lock)
            {
                if (_delete0Command == null)
                {
                    _delete0Command = _database.CreateCommand(conn);
                    _delete0Command.CommandText = "DELETE FROM followsMe " +
                                                 "WHERE identity = $identity AND driveId = $driveId";
                    _delete0Param1 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param1);
                    _delete0Param1.ParameterName = "$identity";
                    _delete0Param2 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param2);
                    _delete0Param2.ParameterName = "$driveId";
                    _delete0Command.Prepare();
                }
                _delete0Param1.Value = identity;
                _delete0Param2.Value = driveId.ToByteArray();
                var count = _database.ExecuteNonQuery(conn, _delete0Command);
                if (count > 0)
                    _cache.Remove("TableFollowsMeCRUD", identity+driveId.ToString());
                return count;
            } // Lock
        }

        public FollowsMeRecord ReadRecordFromReader0(SqliteDataReader rdr, string identity,Guid driveId)
        {
            if (identity == null) throw new Exception("Cannot be null");
            if (identity?.Length < 3) throw new Exception("Too short");
            if (identity?.Length > 255) throw new Exception("Too long");
            var result = new List<FollowsMeRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new FollowsMeRecord();
            item.identity = identity;
            item.driveId = driveId;

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.created = new UnixTimeUtcUnique(rdr.GetInt64(0));
            }

            if (rdr.IsDBNull(1))
                item.modified = null;
            else
            {
                item.modified = new UnixTimeUtcUnique(rdr.GetInt64(1));
            }
            return item;
       }

        public FollowsMeRecord Get(DatabaseBase.DatabaseConnection conn, string identity,Guid driveId)
        {
            if (identity == null) throw new Exception("Cannot be null");
            if (identity?.Length < 3) throw new Exception("Too short");
            if (identity?.Length > 255) throw new Exception("Too long");
            var (hit, cacheObject) = _cache.Get("TableFollowsMeCRUD", identity+driveId.ToString());
            if (hit)
                return (FollowsMeRecord)cacheObject;
            lock (_get0Lock)
            {
                if (_get0Command == null)
                {
                    _get0Command = _database.CreateCommand(conn);
                    _get0Command.CommandText = "SELECT created,modified FROM followsMe " +
                                                 "WHERE identity = $identity AND driveId = $driveId LIMIT 1;";
                    _get0Param1 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param1);
                    _get0Param1.ParameterName = "$identity";
                    _get0Param2 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param2);
                    _get0Param2.ParameterName = "$driveId";
                    _get0Command.Prepare();
                }
                _get0Param1.Value = identity;
                _get0Param2.Value = driveId.ToByteArray();
                using (SqliteDataReader rdr = _database.ExecuteReader(conn, _get0Command, System.Data.CommandBehavior.SingleRow))
                {
                    if (!rdr.Read())
                    {
                        _cache.AddOrUpdate("TableFollowsMeCRUD", identity+driveId.ToString(), null);
                        return null;
                    }
                    var r = ReadRecordFromReader0(rdr, identity,driveId);
                    _cache.AddOrUpdate("TableFollowsMeCRUD", identity+driveId.ToString(), r);
                    return r;
                } // using
            } // lock
        }

        public FollowsMeRecord ReadRecordFromReader1(SqliteDataReader rdr, string identity)
        {
            if (identity == null) throw new Exception("Cannot be null");
            if (identity?.Length < 3) throw new Exception("Too short");
            if (identity?.Length > 255) throw new Exception("Too long");
            var result = new List<FollowsMeRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new FollowsMeRecord();
            item.identity = identity;

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(0, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in driveId...");
                item.driveId = new Guid(_guid);
            }

            if (rdr.IsDBNull(1))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.created = new UnixTimeUtcUnique(rdr.GetInt64(1));
            }

            if (rdr.IsDBNull(2))
                item.modified = null;
            else
            {
                item.modified = new UnixTimeUtcUnique(rdr.GetInt64(2));
            }
            return item;
       }

        public List<FollowsMeRecord> Get(DatabaseBase.DatabaseConnection conn, string identity)
        {
            if (identity == null) throw new Exception("Cannot be null");
            if (identity?.Length < 3) throw new Exception("Too short");
            if (identity?.Length > 255) throw new Exception("Too long");
            lock (_get1Lock)
            {
                if (_get1Command == null)
                {
                    _get1Command = _database.CreateCommand(conn);
                    _get1Command.CommandText = "SELECT driveId,created,modified FROM followsMe " +
                                                 "WHERE identity = $identity;";
                    _get1Param1 = _get1Command.CreateParameter();
                    _get1Command.Parameters.Add(_get1Param1);
                    _get1Param1.ParameterName = "$identity";
                    _get1Command.Prepare();
                }
                _get1Param1.Value = identity;
                using (SqliteDataReader rdr = _database.ExecuteReader(conn, _get1Command, System.Data.CommandBehavior.Default))
                {
                    if (!rdr.Read())
                    {
                        _cache.AddOrUpdate("TableFollowsMeCRUD", identity, null);
                        return null;
                    }
                    var result = new List<FollowsMeRecord>();
                    while (true)
                    {
                        result.Add(ReadRecordFromReader1(rdr, identity));
                        if (!rdr.Read())
                            break;
                    }
                    return result;
                } // using
            } // lock
        }

    }
}
