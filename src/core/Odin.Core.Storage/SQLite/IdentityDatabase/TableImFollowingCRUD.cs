using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;
using Odin.Core.Identity;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class ImFollowingRecord
    {
        private OdinId _identity;
        public OdinId identity
        {
           get {
                   return _identity;
               }
           set {
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
    } // End of class ImFollowingRecord

    public class TableImFollowingCRUD : TableBase
    {
        private bool _disposed = false;
        private readonly CacheHelper _cache;

        public TableImFollowingCRUD(IdentityDatabase db, CacheHelper cache) : base(db)
        {
            _cache = cache;
        }

        ~TableImFollowingCRUD()
        {
            if (_disposed == false) throw new Exception("TableImFollowingCRUD Not disposed properly");
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
                       cmd.CommandText = "DROP TABLE IF EXISTS imFollowing;";
                       conn.ExecuteNonQuery(cmd);
                    }
                    cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS imFollowing("
                     +"identity BLOB NOT NULL, "
                     +"driveId BLOB NOT NULL, "
                     +"created INT NOT NULL, "
                     +"modified INT  "
                     +", PRIMARY KEY (identity,driveId)"
                     +");"
                     +"CREATE INDEX IF NOT EXISTS Idx0TableImFollowingCRUD ON imFollowing(identity);"
                     ;
                    conn.ExecuteNonQuery(cmd);
            }
        }

        public virtual int Insert(DatabaseConnection conn, ImFollowingRecord item)
        {
                using (var _insertCommand = _database.CreateCommand())
                {
                    _insertCommand.CommandText = "INSERT INTO imFollowing (identity,driveId,created,modified) " +
                                                 "VALUES ($identity,$driveId,$created,$modified)";
                    var _insertParam1 = _insertCommand.CreateParameter();
                    _insertParam1.ParameterName = "$identity";
                    _insertCommand.Parameters.Add(_insertParam1);
                    var _insertParam2 = _insertCommand.CreateParameter();
                    _insertParam2.ParameterName = "$driveId";
                    _insertCommand.Parameters.Add(_insertParam2);
                    var _insertParam3 = _insertCommand.CreateParameter();
                    _insertParam3.ParameterName = "$created";
                    _insertCommand.Parameters.Add(_insertParam3);
                    var _insertParam4 = _insertCommand.CreateParameter();
                    _insertParam4.ParameterName = "$modified";
                    _insertCommand.Parameters.Add(_insertParam4);
                _insertParam1.Value = item.identity.DomainName;
                _insertParam2.Value = item.driveId.ToByteArray();
                var now = UnixTimeUtcUnique.Now();
                _insertParam3.Value = now.uniqueTime;
                item.modified = null;
                _insertParam4.Value = DBNull.Value;
                var count = conn.ExecuteNonQuery(_insertCommand);
                if (count > 0)
                 {
                     item.created = now;
                    _cache.AddOrUpdate("TableImFollowingCRUD", item.identity.DomainName+item.driveId.ToString(), item);
                 }
                return count;
                } // Using
        }

        public virtual int Upsert(DatabaseConnection conn, ImFollowingRecord item)
        {
                using (var _upsertCommand = _database.CreateCommand())
                {
                    _upsertCommand.CommandText = "INSERT INTO imFollowing (identity,driveId,created) " +
                                                 "VALUES ($identity,$driveId,$created)"+
                                                 "ON CONFLICT (identity,driveId) DO UPDATE "+
                                                 "SET modified = $modified "+
                                                 "RETURNING created, modified;";
                    var _upsertParam1 = _upsertCommand.CreateParameter();
                    _upsertParam1.ParameterName = "$identity";
                    _upsertCommand.Parameters.Add(_upsertParam1);
                    var _upsertParam2 = _upsertCommand.CreateParameter();
                    _upsertParam2.ParameterName = "$driveId";
                    _upsertCommand.Parameters.Add(_upsertParam2);
                    var _upsertParam3 = _upsertCommand.CreateParameter();
                    _upsertParam3.ParameterName = "$created";
                    _upsertCommand.Parameters.Add(_upsertParam3);
                    var _upsertParam4 = _upsertCommand.CreateParameter();
                    _upsertParam4.ParameterName = "$modified";
                    _upsertCommand.Parameters.Add(_upsertParam4);
                var now = UnixTimeUtcUnique.Now();
                _upsertParam1.Value = item.identity.DomainName;
                _upsertParam2.Value = item.driveId.ToByteArray();
                _upsertParam3.Value = now.uniqueTime;
                _upsertParam4.Value = now.uniqueTime;
                using (SqliteDataReader rdr = conn.ExecuteReader(_upsertCommand, System.Data.CommandBehavior.SingleRow))
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
                      _cache.AddOrUpdate("TableImFollowingCRUD", item.identity.DomainName+item.driveId.ToString(), item);
                      return 1;
                   }
                }
                return 0;
            } // Using
        }

        public virtual int Update(DatabaseConnection conn, ImFollowingRecord item)
        {
                using (var _updateCommand = _database.CreateCommand())
                {
                    _updateCommand.CommandText = "UPDATE imFollowing " +
                                                 "SET modified = $modified "+
                                                 "WHERE (identity = $identity,driveId = $driveId)";
                    var _updateParam1 = _updateCommand.CreateParameter();
                    _updateParam1.ParameterName = "$identity";
                    _updateCommand.Parameters.Add(_updateParam1);
                    var _updateParam2 = _updateCommand.CreateParameter();
                    _updateParam2.ParameterName = "$driveId";
                    _updateCommand.Parameters.Add(_updateParam2);
                    var _updateParam3 = _updateCommand.CreateParameter();
                    _updateParam3.ParameterName = "$created";
                    _updateCommand.Parameters.Add(_updateParam3);
                    var _updateParam4 = _updateCommand.CreateParameter();
                    _updateParam4.ParameterName = "$modified";
                    _updateCommand.Parameters.Add(_updateParam4);
                var now = UnixTimeUtcUnique.Now();
                _updateParam1.Value = item.identity.DomainName;
                _updateParam2.Value = item.driveId.ToByteArray();
                _updateParam3.Value = now.uniqueTime;
                _updateParam4.Value = now.uniqueTime;
                var count = conn.ExecuteNonQuery(_updateCommand);
                if (count > 0)
                {
                     item.modified = now;
                    _cache.AddOrUpdate("TableImFollowingCRUD", item.identity.DomainName+item.driveId.ToString(), item);
                }
                return count;
                } // Using
        }

        public virtual int GetCount(DatabaseConnection conn)
        {
                using (var _getCountCommand = _database.CreateCommand())
                {
                    _getCountCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM imFollowing; PRAGMA read_uncommitted = 0;";
                    var count = conn.ExecuteNonQuery(_getCountCommand);
                    return count;
                }
        }

        // SELECT identity,driveId,created,modified
        public ImFollowingRecord ReadRecordFromReaderAll(SqliteDataReader rdr)
        {
            var result = new List<ImFollowingRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new ImFollowingRecord();

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.identity = new OdinId(rdr.GetString(0));
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

        public int Delete(DatabaseConnection conn, OdinId identity,Guid driveId)
        {
                using (var _delete0Command = _database.CreateCommand())
                {
                    _delete0Command.CommandText = "DELETE FROM imFollowing " +
                                                 "WHERE identity = $identity AND driveId = $driveId";
                    var _delete0Param1 = _delete0Command.CreateParameter();
                    _delete0Param1.ParameterName = "$identity";
                    _delete0Command.Parameters.Add(_delete0Param1);
                    var _delete0Param2 = _delete0Command.CreateParameter();
                    _delete0Param2.ParameterName = "$driveId";
                    _delete0Command.Parameters.Add(_delete0Param2);

                _delete0Param1.Value = identity.DomainName;
                _delete0Param2.Value = driveId.ToByteArray();
                var count = conn.ExecuteNonQuery(_delete0Command);
                if (count > 0)
                    _cache.Remove("TableImFollowingCRUD", identity.DomainName+driveId.ToString());
                return count;
                } // Using
        }

        public ImFollowingRecord ReadRecordFromReader0(SqliteDataReader rdr, OdinId identity,Guid driveId)
        {
            var result = new List<ImFollowingRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new ImFollowingRecord();
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

        public ImFollowingRecord Get(DatabaseConnection conn, OdinId identity,Guid driveId)
        {
            var (hit, cacheObject) = _cache.Get("TableImFollowingCRUD", identity.DomainName+driveId.ToString());
            if (hit)
                return (ImFollowingRecord)cacheObject;
                using (var _get0Command = _database.CreateCommand())
                {
                    _get0Command.CommandText = "SELECT created,modified FROM imFollowing " +
                                                 "WHERE identity = $identity AND driveId = $driveId LIMIT 1;";
                    var _get0Param1 = _get0Command.CreateParameter();
                    _get0Param1.ParameterName = "$identity";
                    _get0Command.Parameters.Add(_get0Param1);
                    var _get0Param2 = _get0Command.CreateParameter();
                    _get0Param2.ParameterName = "$driveId";
                    _get0Command.Parameters.Add(_get0Param2);

                _get0Param1.Value = identity.DomainName;
                _get0Param2.Value = driveId.ToByteArray();
                    lock (conn._lock)
                    {
                using (SqliteDataReader rdr = conn.ExecuteReader(_get0Command, System.Data.CommandBehavior.SingleRow))
                {
                    if (!rdr.Read())
                    {
                        _cache.AddOrUpdate("TableImFollowingCRUD", identity.DomainName+driveId.ToString(), null);
                        return null;
                    }
                    var r = ReadRecordFromReader0(rdr, identity,driveId);
                    _cache.AddOrUpdate("TableImFollowingCRUD", identity.DomainName+driveId.ToString(), r);
                    return r;
                } // using
            } // lock
            } // using
        }

        public ImFollowingRecord ReadRecordFromReader1(SqliteDataReader rdr, OdinId identity)
        {
            var result = new List<ImFollowingRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new ImFollowingRecord();
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

        public List<ImFollowingRecord> Get(DatabaseConnection conn, OdinId identity)
        {
                using (var _get1Command = _database.CreateCommand())
                {
                    _get1Command.CommandText = "SELECT driveId,created,modified FROM imFollowing " +
                                                 "WHERE identity = $identity;";
                    var _get1Param1 = _get1Command.CreateParameter();
                    _get1Param1.ParameterName = "$identity";
                    _get1Command.Parameters.Add(_get1Param1);

                _get1Param1.Value = identity.DomainName;
                    lock (conn._lock)
                    {
                using (SqliteDataReader rdr = conn.ExecuteReader(_get1Command, System.Data.CommandBehavior.Default))
                {
                    if (!rdr.Read())
                    {
                        _cache.AddOrUpdate("TableImFollowingCRUD", identity.DomainName, null);
                        return null;
                    }
                    var result = new List<ImFollowingRecord>();
                    while (true)
                    {
                        result.Add(ReadRecordFromReader1(rdr, identity));
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
