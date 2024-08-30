using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;
using Odin.Core.Identity;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("DatabaseCommitTest")]

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class FollowsMeRecord
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
        private readonly CacheHelper _cache;

        public TableFollowsMeCRUD(IdentityDatabase db, CacheHelper cache) : base(db, "followsMe")
        {
            _cache = cache;
        }

        ~TableFollowsMeCRUD()
        {
            if (_disposed == false) throw new Exception("TableFollowsMeCRUD Not disposed properly");
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
                       cmd.CommandText = "DROP TABLE IF EXISTS followsMe;";
                       conn.ExecuteNonQuery(cmd);
                    }
                    cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS followsMe("
                     +"identityId BLOB NOT NULL, "
                     +"identity STRING NOT NULL, "
                     +"driveId BLOB NOT NULL, "
                     +"created INT NOT NULL, "
                     +"modified INT  "
                     +", PRIMARY KEY (identityId,identity,driveId)"
                     +");"
                     +"CREATE INDEX IF NOT EXISTS Idx0TableFollowsMeCRUD ON followsMe(identityId,identity);"
                     ;
                    conn.ExecuteNonQuery(cmd);
            }
        }

        internal virtual int Insert(DatabaseConnection conn, FollowsMeRecord item)
        {
            using (var _insertCommand = _database.CreateCommand())
            {
                _insertCommand.CommandText = "INSERT INTO followsMe (identityId,identity,driveId,created,modified) " +
                                             "VALUES (@identityId,@identity,@driveId,@created,@modified)";
                var _insertParam1 = _insertCommand.CreateParameter();
                _insertParam1.ParameterName = "@identityId";
                _insertCommand.Parameters.Add(_insertParam1);
                var _insertParam2 = _insertCommand.CreateParameter();
                _insertParam2.ParameterName = "@identity";
                _insertCommand.Parameters.Add(_insertParam2);
                var _insertParam3 = _insertCommand.CreateParameter();
                _insertParam3.ParameterName = "@driveId";
                _insertCommand.Parameters.Add(_insertParam3);
                var _insertParam4 = _insertCommand.CreateParameter();
                _insertParam4.ParameterName = "@created";
                _insertCommand.Parameters.Add(_insertParam4);
                var _insertParam5 = _insertCommand.CreateParameter();
                _insertParam5.ParameterName = "@modified";
                _insertCommand.Parameters.Add(_insertParam5);
                _insertParam1.Value = item.identityId.ToByteArray();
                _insertParam2.Value = item.identity;
                _insertParam3.Value = item.driveId.ToByteArray();
                var now = UnixTimeUtcUnique.Now();
                _insertParam4.Value = now.uniqueTime;
                item.modified = null;
                _insertParam5.Value = DBNull.Value;
                var count = conn.ExecuteNonQuery(_insertCommand);
                if (count > 0)
                {
                     item.created = now;
                    _cache.AddOrUpdate("TableFollowsMeCRUD", item.identityId.ToString()+item.identity+item.driveId.ToString(), item);
                }
                return count;
            } // Using
        }

        internal virtual int TryInsert(DatabaseConnection conn, FollowsMeRecord item)
        {
            using (var _insertCommand = _database.CreateCommand())
            {
                _insertCommand.CommandText = "INSERT OR IGNORE INTO followsMe (identityId,identity,driveId,created,modified) " +
                                             "VALUES (@identityId,@identity,@driveId,@created,@modified)";
                var _insertParam1 = _insertCommand.CreateParameter();
                _insertParam1.ParameterName = "@identityId";
                _insertCommand.Parameters.Add(_insertParam1);
                var _insertParam2 = _insertCommand.CreateParameter();
                _insertParam2.ParameterName = "@identity";
                _insertCommand.Parameters.Add(_insertParam2);
                var _insertParam3 = _insertCommand.CreateParameter();
                _insertParam3.ParameterName = "@driveId";
                _insertCommand.Parameters.Add(_insertParam3);
                var _insertParam4 = _insertCommand.CreateParameter();
                _insertParam4.ParameterName = "@created";
                _insertCommand.Parameters.Add(_insertParam4);
                var _insertParam5 = _insertCommand.CreateParameter();
                _insertParam5.ParameterName = "@modified";
                _insertCommand.Parameters.Add(_insertParam5);
                _insertParam1.Value = item.identityId.ToByteArray();
                _insertParam2.Value = item.identity;
                _insertParam3.Value = item.driveId.ToByteArray();
                var now = UnixTimeUtcUnique.Now();
                _insertParam4.Value = now.uniqueTime;
                item.modified = null;
                _insertParam5.Value = DBNull.Value;
                var count = conn.ExecuteNonQuery(_insertCommand);
                if (count > 0)
                {
                    item.created = now;
                   _cache.AddOrUpdate("TableFollowsMeCRUD", item.identityId.ToString()+item.identity+item.driveId.ToString(), item);
                }
                return count;
            } // Using
        }

        internal virtual int Upsert(DatabaseConnection conn, FollowsMeRecord item)
        {
            using (var _upsertCommand = _database.CreateCommand())
            {
                _upsertCommand.CommandText = "INSERT INTO followsMe (identityId,identity,driveId,created) " +
                                             "VALUES (@identityId,@identity,@driveId,@created)"+
                                             "ON CONFLICT (identityId,identity,driveId) DO UPDATE "+
                                             "SET modified = @modified "+
                                             "RETURNING created, modified;";
                var _upsertParam1 = _upsertCommand.CreateParameter();
                _upsertParam1.ParameterName = "@identityId";
                _upsertCommand.Parameters.Add(_upsertParam1);
                var _upsertParam2 = _upsertCommand.CreateParameter();
                _upsertParam2.ParameterName = "@identity";
                _upsertCommand.Parameters.Add(_upsertParam2);
                var _upsertParam3 = _upsertCommand.CreateParameter();
                _upsertParam3.ParameterName = "@driveId";
                _upsertCommand.Parameters.Add(_upsertParam3);
                var _upsertParam4 = _upsertCommand.CreateParameter();
                _upsertParam4.ParameterName = "@created";
                _upsertCommand.Parameters.Add(_upsertParam4);
                var _upsertParam5 = _upsertCommand.CreateParameter();
                _upsertParam5.ParameterName = "@modified";
                _upsertCommand.Parameters.Add(_upsertParam5);
                var now = UnixTimeUtcUnique.Now();
                _upsertParam1.Value = item.identityId.ToByteArray();
                _upsertParam2.Value = item.identity;
                _upsertParam3.Value = item.driveId.ToByteArray();
                _upsertParam4.Value = now.uniqueTime;
                _upsertParam5.Value = now.uniqueTime;
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
                      _cache.AddOrUpdate("TableFollowsMeCRUD", item.identityId.ToString()+item.identity+item.driveId.ToString(), item);
                      return 1;
                   }
                }
                return 0;
            } // Using
        }

        internal virtual int Update(DatabaseConnection conn, FollowsMeRecord item)
        {
            using (var _updateCommand = _database.CreateCommand())
            {
                _updateCommand.CommandText = "UPDATE followsMe " +
                                             "SET modified = @modified "+
                                             "WHERE (identityId = @identityId AND identity = @identity AND driveId = @driveId)";
                var _updateParam1 = _updateCommand.CreateParameter();
                _updateParam1.ParameterName = "@identityId";
                _updateCommand.Parameters.Add(_updateParam1);
                var _updateParam2 = _updateCommand.CreateParameter();
                _updateParam2.ParameterName = "@identity";
                _updateCommand.Parameters.Add(_updateParam2);
                var _updateParam3 = _updateCommand.CreateParameter();
                _updateParam3.ParameterName = "@driveId";
                _updateCommand.Parameters.Add(_updateParam3);
                var _updateParam4 = _updateCommand.CreateParameter();
                _updateParam4.ParameterName = "@created";
                _updateCommand.Parameters.Add(_updateParam4);
                var _updateParam5 = _updateCommand.CreateParameter();
                _updateParam5.ParameterName = "@modified";
                _updateCommand.Parameters.Add(_updateParam5);
             var now = UnixTimeUtcUnique.Now();
                _updateParam1.Value = item.identityId.ToByteArray();
                _updateParam2.Value = item.identity;
                _updateParam3.Value = item.driveId.ToByteArray();
                _updateParam4.Value = now.uniqueTime;
                _updateParam5.Value = now.uniqueTime;
                var count = conn.ExecuteNonQuery(_updateCommand);
                if (count > 0)
                {
                     item.modified = now;
                    _cache.AddOrUpdate("TableFollowsMeCRUD", item.identityId.ToString()+item.identity+item.driveId.ToString(), item);
                }
                return count;
            } // Using
        }

        internal virtual int GetCountDirty(DatabaseConnection conn)
        {
            using (var _getCountCommand = _database.CreateCommand())
            {
                _getCountCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM followsMe; PRAGMA read_uncommitted = 0;";
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
            sl.Add("identity");
            sl.Add("driveId");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        // SELECT identityId,identity,driveId,created,modified
        internal FollowsMeRecord ReadRecordFromReaderAll(SqliteDataReader rdr)
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
                bytesRead = rdr.GetBytes(0, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in identityId...");
                item.identityId = new Guid(_guid);
            }

            if (rdr.IsDBNull(1))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.identity = rdr.GetString(1);
            }

            if (rdr.IsDBNull(2))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(2, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in driveId...");
                item.driveId = new Guid(_guid);
            }

            if (rdr.IsDBNull(3))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.created = new UnixTimeUtcUnique(rdr.GetInt64(3));
            }

            if (rdr.IsDBNull(4))
                item.modified = null;
            else
            {
                item.modified = new UnixTimeUtcUnique(rdr.GetInt64(4));
            }
            return item;
       }

        internal int Delete(DatabaseConnection conn, Guid identityId,string identity,Guid driveId)
        {
            if (identity == null) throw new Exception("Cannot be null");
            if (identity?.Length < 3) throw new Exception("Too short");
            if (identity?.Length > 255) throw new Exception("Too long");
            using (var _delete0Command = _database.CreateCommand())
            {
                _delete0Command.CommandText = "DELETE FROM followsMe " +
                                             "WHERE identityId = @identityId AND identity = @identity AND driveId = @driveId";
                var _delete0Param1 = _delete0Command.CreateParameter();
                _delete0Param1.ParameterName = "@identityId";
                _delete0Command.Parameters.Add(_delete0Param1);
                var _delete0Param2 = _delete0Command.CreateParameter();
                _delete0Param2.ParameterName = "@identity";
                _delete0Command.Parameters.Add(_delete0Param2);
                var _delete0Param3 = _delete0Command.CreateParameter();
                _delete0Param3.ParameterName = "@driveId";
                _delete0Command.Parameters.Add(_delete0Param3);

                _delete0Param1.Value = identityId.ToByteArray();
                _delete0Param2.Value = identity;
                _delete0Param3.Value = driveId.ToByteArray();
                var count = conn.ExecuteNonQuery(_delete0Command);
                if (count > 0)
                    _cache.Remove("TableFollowsMeCRUD", identityId.ToString()+identity+driveId.ToString());
                return count;
            } // Using
        }

        internal FollowsMeRecord ReadRecordFromReader0(SqliteDataReader rdr, Guid identityId,string identity,Guid driveId)
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
            item.identityId = identityId;
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

        internal FollowsMeRecord Get(DatabaseConnection conn, Guid identityId,string identity,Guid driveId)
        {
            if (identity == null) throw new Exception("Cannot be null");
            if (identity?.Length < 3) throw new Exception("Too short");
            if (identity?.Length > 255) throw new Exception("Too long");
            var (hit, cacheObject) = _cache.Get("TableFollowsMeCRUD", identityId.ToString()+identity+driveId.ToString());
            if (hit)
                return (FollowsMeRecord)cacheObject;
            using (var _get0Command = _database.CreateCommand())
            {
                _get0Command.CommandText = "SELECT created,modified FROM followsMe " +
                                             "WHERE identityId = @identityId AND identity = @identity AND driveId = @driveId LIMIT 1;";
                var _get0Param1 = _get0Command.CreateParameter();
                _get0Param1.ParameterName = "@identityId";
                _get0Command.Parameters.Add(_get0Param1);
                var _get0Param2 = _get0Command.CreateParameter();
                _get0Param2.ParameterName = "@identity";
                _get0Command.Parameters.Add(_get0Param2);
                var _get0Param3 = _get0Command.CreateParameter();
                _get0Param3.ParameterName = "@driveId";
                _get0Command.Parameters.Add(_get0Param3);

                _get0Param1.Value = identityId.ToByteArray();
                _get0Param2.Value = identity;
                _get0Param3.Value = driveId.ToByteArray();
                lock (conn._lock)
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_get0Command, System.Data.CommandBehavior.SingleRow))
                    {
                        if (!rdr.Read())
                        {
                            _cache.AddOrUpdate("TableFollowsMeCRUD", identityId.ToString()+identity+driveId.ToString(), null);
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr, identityId,identity,driveId);
                        _cache.AddOrUpdate("TableFollowsMeCRUD", identityId.ToString()+identity+driveId.ToString(), r);
                        return r;
                    } // using
                } // lock
            } // using
        }

        internal FollowsMeRecord ReadRecordFromReader1(SqliteDataReader rdr, Guid identityId,string identity)
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
            item.identityId = identityId;
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

        internal List<FollowsMeRecord> Get(DatabaseConnection conn, Guid identityId,string identity)
        {
            if (identity == null) throw new Exception("Cannot be null");
            if (identity?.Length < 3) throw new Exception("Too short");
            if (identity?.Length > 255) throw new Exception("Too long");
            using (var _get1Command = _database.CreateCommand())
            {
                _get1Command.CommandText = "SELECT driveId,created,modified FROM followsMe " +
                                             "WHERE identityId = @identityId AND identity = @identity;";
                var _get1Param1 = _get1Command.CreateParameter();
                _get1Param1.ParameterName = "@identityId";
                _get1Command.Parameters.Add(_get1Param1);
                var _get1Param2 = _get1Command.CreateParameter();
                _get1Param2.ParameterName = "@identity";
                _get1Command.Parameters.Add(_get1Param2);

                _get1Param1.Value = identityId.ToByteArray();
                _get1Param2.Value = identity;
                lock (conn._lock)
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_get1Command, System.Data.CommandBehavior.Default))
                    {
                        if (!rdr.Read())
                        {
                            _cache.AddOrUpdate("TableFollowsMeCRUD", identityId.ToString()+identity, null);
                            return null;
                        }
                        var result = new List<FollowsMeRecord>();
                        while (true)
                        {
                            result.Add(ReadRecordFromReader1(rdr, identityId,identity));
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
