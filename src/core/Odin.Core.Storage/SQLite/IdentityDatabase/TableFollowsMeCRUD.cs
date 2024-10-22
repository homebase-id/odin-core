using System;
using System.Data.Common;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Odin.Core.Time;
using Odin.Core.Identity;

// THIS FILE IS AUTO GENERATED - DO NOT EDIT

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

        public TableFollowsMeCRUD(CacheHelper cache) : base("followsMe")
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

        public sealed override async Task EnsureTableExistsAsync(DatabaseConnection conn, bool dropExisting = false)
        {
                using (var cmd = conn.db.CreateCommand())
                {
                    if (dropExisting)
                    {
                       cmd.CommandText = "DROP TABLE IF EXISTS followsMe;";
                       await conn.ExecuteNonQueryAsync(cmd);
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
                    await conn.ExecuteNonQueryAsync(cmd);
            }
        }

        internal virtual async Task<int> InsertAsync(DatabaseConnection conn, FollowsMeRecord item)
        {
            DatabaseBase.AssertGuidNotEmpty(item.identityId, "Guid parameter identityId cannot be set to Empty GUID.");
            using (var insertCommand = conn.db.CreateCommand())
            {
                insertCommand.CommandText = "INSERT INTO followsMe (identityId,identity,driveId,created,modified) " +
                                             "VALUES (@identityId,@identity,@driveId,@created,@modified)";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.ParameterName = "@identity";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.ParameterName = "@driveId";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.ParameterName = "@created";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.ParameterName = "@modified";
                insertCommand.Parameters.Add(insertParam5);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.identity;
                insertParam3.Value = item.driveId.ToByteArray();
                var now = UnixTimeUtcUnique.Now();
                insertParam4.Value = now.uniqueTime;
                item.modified = null;
                insertParam5.Value = DBNull.Value;
                var count = await conn.ExecuteNonQueryAsync(insertCommand);
                if (count > 0)
                {
                     item.created = now;
                    _cache.AddOrUpdate("TableFollowsMeCRUD", item.identityId.ToString()+item.identity+item.driveId.ToString(), item);
                }
                return count;
            } // Using
        }

        internal virtual async Task<int> TryInsertAsync(DatabaseConnection conn, FollowsMeRecord item)
        {
            DatabaseBase.AssertGuidNotEmpty(item.identityId, "Guid parameter identityId cannot be set to Empty GUID.");
            using (var insertCommand = conn.db.CreateCommand())
            {
                insertCommand.CommandText = "INSERT OR IGNORE INTO followsMe (identityId,identity,driveId,created,modified) " +
                                             "VALUES (@identityId,@identity,@driveId,@created,@modified)";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.ParameterName = "@identity";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.ParameterName = "@driveId";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.ParameterName = "@created";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.ParameterName = "@modified";
                insertCommand.Parameters.Add(insertParam5);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.identity;
                insertParam3.Value = item.driveId.ToByteArray();
                var now = UnixTimeUtcUnique.Now();
                insertParam4.Value = now.uniqueTime;
                item.modified = null;
                insertParam5.Value = DBNull.Value;
                var count = await conn.ExecuteNonQueryAsync(insertCommand);
                if (count > 0)
                {
                    item.created = now;
                   _cache.AddOrUpdate("TableFollowsMeCRUD", item.identityId.ToString()+item.identity+item.driveId.ToString(), item);
                }
                return count;
            } // Using
        }

        internal virtual async Task<int> UpsertAsync(DatabaseConnection conn, FollowsMeRecord item)
        {
            DatabaseBase.AssertGuidNotEmpty(item.identityId, "Guid parameter identityId cannot be set to Empty GUID.");
            using (var upsertCommand = conn.db.CreateCommand())
            {
                upsertCommand.CommandText = "INSERT INTO followsMe (identityId,identity,driveId,created) " +
                                             "VALUES (@identityId,@identity,@driveId,@created)"+
                                             "ON CONFLICT (identityId,identity,driveId) DO UPDATE "+
                                             "SET modified = @modified "+
                                             "RETURNING created, modified;";
                var upsertParam1 = upsertCommand.CreateParameter();
                upsertParam1.ParameterName = "@identityId";
                upsertCommand.Parameters.Add(upsertParam1);
                var upsertParam2 = upsertCommand.CreateParameter();
                upsertParam2.ParameterName = "@identity";
                upsertCommand.Parameters.Add(upsertParam2);
                var upsertParam3 = upsertCommand.CreateParameter();
                upsertParam3.ParameterName = "@driveId";
                upsertCommand.Parameters.Add(upsertParam3);
                var upsertParam4 = upsertCommand.CreateParameter();
                upsertParam4.ParameterName = "@created";
                upsertCommand.Parameters.Add(upsertParam4);
                var upsertParam5 = upsertCommand.CreateParameter();
                upsertParam5.ParameterName = "@modified";
                upsertCommand.Parameters.Add(upsertParam5);
                var now = UnixTimeUtcUnique.Now();
                upsertParam1.Value = item.identityId.ToByteArray();
                upsertParam2.Value = item.identity;
                upsertParam3.Value = item.driveId.ToByteArray();
                upsertParam4.Value = now.uniqueTime;
                upsertParam5.Value = now.uniqueTime;
                using (var rdr = await conn.ExecuteReaderAsync(upsertCommand, System.Data.CommandBehavior.SingleRow))
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

        internal virtual async Task<int> UpdateAsync(DatabaseConnection conn, FollowsMeRecord item)
        {
            DatabaseBase.AssertGuidNotEmpty(item.identityId, "Guid parameter identityId cannot be set to Empty GUID.");
            using (var updateCommand = conn.db.CreateCommand())
            {
                updateCommand.CommandText = "UPDATE followsMe " +
                                             "SET modified = @modified "+
                                             "WHERE (identityId = @identityId AND identity = @identity AND driveId = @driveId)";
                var updateParam1 = updateCommand.CreateParameter();
                updateParam1.ParameterName = "@identityId";
                updateCommand.Parameters.Add(updateParam1);
                var updateParam2 = updateCommand.CreateParameter();
                updateParam2.ParameterName = "@identity";
                updateCommand.Parameters.Add(updateParam2);
                var updateParam3 = updateCommand.CreateParameter();
                updateParam3.ParameterName = "@driveId";
                updateCommand.Parameters.Add(updateParam3);
                var updateParam4 = updateCommand.CreateParameter();
                updateParam4.ParameterName = "@created";
                updateCommand.Parameters.Add(updateParam4);
                var updateParam5 = updateCommand.CreateParameter();
                updateParam5.ParameterName = "@modified";
                updateCommand.Parameters.Add(updateParam5);
             var now = UnixTimeUtcUnique.Now();
                updateParam1.Value = item.identityId.ToByteArray();
                updateParam2.Value = item.identity;
                updateParam3.Value = item.driveId.ToByteArray();
                updateParam4.Value = now.uniqueTime;
                updateParam5.Value = now.uniqueTime;
                var count = await conn.ExecuteNonQueryAsync(updateCommand);
                if (count > 0)
                {
                     item.modified = now;
                    _cache.AddOrUpdate("TableFollowsMeCRUD", item.identityId.ToString()+item.identity+item.driveId.ToString(), item);
                }
                return count;
            } // Using
        }

        internal virtual async Task<int> GetCountDirtyAsync(DatabaseConnection conn)
        {
            using (var getCountCommand = conn.db.CreateCommand())
            {
                 // TODO: this is SQLite specific
                getCountCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM followsMe; PRAGMA read_uncommitted = 0;";
                var count = await conn.ExecuteScalarAsync(getCountCommand);
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
        internal FollowsMeRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<FollowsMeRecord>();
            byte[] tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new FollowsMeRecord();

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(0, 0, guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in identityId...");
                item.identityId = new Guid(guid);
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
                bytesRead = rdr.GetBytes(2, 0, guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in driveId...");
                item.driveId = new Guid(guid);
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

        internal async Task<int> DeleteAsync(DatabaseConnection conn, Guid identityId,string identity,Guid driveId)
        {
            if (identity == null) throw new Exception("Cannot be null");
            if (identity?.Length < 3) throw new Exception("Too short");
            if (identity?.Length > 255) throw new Exception("Too long");
            using (var delete0Command = conn.db.CreateCommand())
            {
                delete0Command.CommandText = "DELETE FROM followsMe " +
                                             "WHERE identityId = @identityId AND identity = @identity AND driveId = @driveId";
                var delete0Param1 = delete0Command.CreateParameter();
                delete0Param1.ParameterName = "@identityId";
                delete0Command.Parameters.Add(delete0Param1);
                var delete0Param2 = delete0Command.CreateParameter();
                delete0Param2.ParameterName = "@identity";
                delete0Command.Parameters.Add(delete0Param2);
                var delete0Param3 = delete0Command.CreateParameter();
                delete0Param3.ParameterName = "@driveId";
                delete0Command.Parameters.Add(delete0Param3);

                delete0Param1.Value = identityId.ToByteArray();
                delete0Param2.Value = identity;
                delete0Param3.Value = driveId.ToByteArray();
                var count = await conn.ExecuteNonQueryAsync(delete0Command);
                if (count > 0)
                    _cache.Remove("TableFollowsMeCRUD", identityId.ToString()+identity+driveId.ToString());
                return count;
            } // Using
        }

        internal FollowsMeRecord ReadRecordFromReader0(DbDataReader rdr, Guid identityId,string identity,Guid driveId)
        {
            if (identity == null) throw new Exception("Cannot be null");
            if (identity?.Length < 3) throw new Exception("Too short");
            if (identity?.Length > 255) throw new Exception("Too long");
            var result = new List<FollowsMeRecord>();
            byte[] tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
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

        internal async Task<FollowsMeRecord> GetAsync(DatabaseConnection conn, Guid identityId,string identity,Guid driveId)
        {
            if (identity == null) throw new Exception("Cannot be null");
            if (identity?.Length < 3) throw new Exception("Too short");
            if (identity?.Length > 255) throw new Exception("Too long");
            var (hit, cacheObject) = _cache.Get("TableFollowsMeCRUD", identityId.ToString()+identity+driveId.ToString());
            if (hit)
                return (FollowsMeRecord)cacheObject;
            using (var get0Command = conn.db.CreateCommand())
            {
                get0Command.CommandText = "SELECT created,modified FROM followsMe " +
                                             "WHERE identityId = @identityId AND identity = @identity AND driveId = @driveId LIMIT 1;";
                var get0Param1 = get0Command.CreateParameter();
                get0Param1.ParameterName = "@identityId";
                get0Command.Parameters.Add(get0Param1);
                var get0Param2 = get0Command.CreateParameter();
                get0Param2.ParameterName = "@identity";
                get0Command.Parameters.Add(get0Param2);
                var get0Param3 = get0Command.CreateParameter();
                get0Param3.ParameterName = "@driveId";
                get0Command.Parameters.Add(get0Param3);

                get0Param1.Value = identityId.ToByteArray();
                get0Param2.Value = identity;
                get0Param3.Value = driveId.ToByteArray();
                {
                    using (var rdr = await conn.ExecuteReaderAsync(get0Command, System.Data.CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            _cache.AddOrUpdate("TableFollowsMeCRUD", identityId.ToString()+identity+driveId.ToString(), null);
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr, identityId,identity,driveId);
                        _cache.AddOrUpdate("TableFollowsMeCRUD", identityId.ToString()+identity+driveId.ToString(), r);
                        return r;
                    } // using
                } //
            } // using
        }

        internal FollowsMeRecord ReadRecordFromReader1(DbDataReader rdr, Guid identityId,string identity)
        {
            if (identity == null) throw new Exception("Cannot be null");
            if (identity?.Length < 3) throw new Exception("Too short");
            if (identity?.Length > 255) throw new Exception("Too long");
            var result = new List<FollowsMeRecord>();
            byte[] tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new FollowsMeRecord();
            item.identityId = identityId;
            item.identity = identity;

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(0, 0, guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in driveId...");
                item.driveId = new Guid(guid);
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

        internal async Task<List<FollowsMeRecord>> GetAsync(DatabaseConnection conn, Guid identityId,string identity)
        {
            if (identity == null) throw new Exception("Cannot be null");
            if (identity?.Length < 3) throw new Exception("Too short");
            if (identity?.Length > 255) throw new Exception("Too long");
            using (var get1Command = conn.db.CreateCommand())
            {
                get1Command.CommandText = "SELECT driveId,created,modified FROM followsMe " +
                                             "WHERE identityId = @identityId AND identity = @identity;";
                var get1Param1 = get1Command.CreateParameter();
                get1Param1.ParameterName = "@identityId";
                get1Command.Parameters.Add(get1Param1);
                var get1Param2 = get1Command.CreateParameter();
                get1Param2.ParameterName = "@identity";
                get1Command.Parameters.Add(get1Param2);

                get1Param1.Value = identityId.ToByteArray();
                get1Param2.Value = identity;
                {
                    using (var rdr = await conn.ExecuteReaderAsync(get1Command, System.Data.CommandBehavior.Default))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            _cache.AddOrUpdate("TableFollowsMeCRUD", identityId.ToString()+identity, null);
                            return new List<FollowsMeRecord>();
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
                } //
            } // using
        }

    }
}
