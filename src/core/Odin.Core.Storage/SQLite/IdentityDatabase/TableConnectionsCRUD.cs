using System;
using System.Data.Common;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Odin.Core.Time;
using Odin.Core.Identity;

// THIS FILE IS AUTO GENERATED - DO NOT EDIT

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class ConnectionsRecord
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
        private string _displayName;
        public string displayName
        {
           get {
                   return _displayName;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 0) throw new Exception("Too short");
                    if (value?.Length > 80) throw new Exception("Too long");
                  _displayName = value;
               }
        }
        private Int32 _status;
        public Int32 status
        {
           get {
                   return _status;
               }
           set {
                  _status = value;
               }
        }
        private Int32 _accessIsRevoked;
        public Int32 accessIsRevoked
        {
           get {
                   return _accessIsRevoked;
               }
           set {
                  _accessIsRevoked = value;
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
    } // End of class ConnectionsRecord

    public class TableConnectionsCRUD
    {
        private readonly CacheHelper _cache;

        public TableConnectionsCRUD(CacheHelper cache)
        {
            _cache = cache;
        }


        public async Task EnsureTableExistsAsync(DatabaseConnection conn, bool dropExisting = false)
        {
                using (var cmd = conn.db.CreateCommand())
                {
                    if (dropExisting)
                    {
                       cmd.CommandText = "DROP TABLE IF EXISTS connections;";
                       await conn.ExecuteNonQueryAsync(cmd);
                    }
                    cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS connections("
                     +"identityId BLOB NOT NULL, "
                     +"identity STRING NOT NULL UNIQUE, "
                     +"displayName STRING NOT NULL, "
                     +"status INT NOT NULL, "
                     +"accessIsRevoked INT NOT NULL, "
                     +"data BLOB , "
                     +"created INT NOT NULL, "
                     +"modified INT  "
                     +", PRIMARY KEY (identityId,identity)"
                     +");"
                     +"CREATE INDEX IF NOT EXISTS Idx0TableConnectionsCRUD ON connections(identityId,created);"
                     ;
                    await conn.ExecuteNonQueryAsync(cmd);
            }
        }

        internal virtual async Task<int> InsertAsync(DatabaseConnection conn, ConnectionsRecord item)
        {
            DatabaseBase.AssertGuidNotEmpty(item.identityId, "Guid parameter identityId cannot be set to Empty GUID.");
            using (var insertCommand = conn.db.CreateCommand())
            {
                insertCommand.CommandText = "INSERT INTO connections (identityId,identity,displayName,status,accessIsRevoked,data,created,modified) " +
                                             "VALUES (@identityId,@identity,@displayName,@status,@accessIsRevoked,@data,@created,@modified)";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.ParameterName = "@identity";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.ParameterName = "@displayName";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.ParameterName = "@status";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.ParameterName = "@accessIsRevoked";
                insertCommand.Parameters.Add(insertParam5);
                var insertParam6 = insertCommand.CreateParameter();
                insertParam6.ParameterName = "@data";
                insertCommand.Parameters.Add(insertParam6);
                var insertParam7 = insertCommand.CreateParameter();
                insertParam7.ParameterName = "@created";
                insertCommand.Parameters.Add(insertParam7);
                var insertParam8 = insertCommand.CreateParameter();
                insertParam8.ParameterName = "@modified";
                insertCommand.Parameters.Add(insertParam8);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.identity.DomainName;
                insertParam3.Value = item.displayName;
                insertParam4.Value = item.status;
                insertParam5.Value = item.accessIsRevoked;
                insertParam6.Value = item.data ?? (object)DBNull.Value;
                var now = UnixTimeUtcUnique.Now();
                insertParam7.Value = now.uniqueTime;
                item.modified = null;
                insertParam8.Value = DBNull.Value;
                var count = await conn.ExecuteNonQueryAsync(insertCommand);
                if (count > 0)
                {
                     item.created = now;
                    _cache.AddOrUpdate("TableConnectionsCRUD", item.identityId.ToString()+item.identity.DomainName, item);
                }
                return count;
            } // Using
        }

        internal virtual async Task<int> TryInsertAsync(DatabaseConnection conn, ConnectionsRecord item)
        {
            DatabaseBase.AssertGuidNotEmpty(item.identityId, "Guid parameter identityId cannot be set to Empty GUID.");
            using (var insertCommand = conn.db.CreateCommand())
            {
                insertCommand.CommandText = "INSERT OR IGNORE INTO connections (identityId,identity,displayName,status,accessIsRevoked,data,created,modified) " +
                                             "VALUES (@identityId,@identity,@displayName,@status,@accessIsRevoked,@data,@created,@modified)";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.ParameterName = "@identity";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.ParameterName = "@displayName";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.ParameterName = "@status";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.ParameterName = "@accessIsRevoked";
                insertCommand.Parameters.Add(insertParam5);
                var insertParam6 = insertCommand.CreateParameter();
                insertParam6.ParameterName = "@data";
                insertCommand.Parameters.Add(insertParam6);
                var insertParam7 = insertCommand.CreateParameter();
                insertParam7.ParameterName = "@created";
                insertCommand.Parameters.Add(insertParam7);
                var insertParam8 = insertCommand.CreateParameter();
                insertParam8.ParameterName = "@modified";
                insertCommand.Parameters.Add(insertParam8);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.identity.DomainName;
                insertParam3.Value = item.displayName;
                insertParam4.Value = item.status;
                insertParam5.Value = item.accessIsRevoked;
                insertParam6.Value = item.data ?? (object)DBNull.Value;
                var now = UnixTimeUtcUnique.Now();
                insertParam7.Value = now.uniqueTime;
                item.modified = null;
                insertParam8.Value = DBNull.Value;
                var count = await conn.ExecuteNonQueryAsync(insertCommand);
                if (count > 0)
                {
                    item.created = now;
                   _cache.AddOrUpdate("TableConnectionsCRUD", item.identityId.ToString()+item.identity.DomainName, item);
                }
                return count;
            } // Using
        }

        internal virtual async Task<int> UpsertAsync(DatabaseConnection conn, ConnectionsRecord item)
        {
            DatabaseBase.AssertGuidNotEmpty(item.identityId, "Guid parameter identityId cannot be set to Empty GUID.");
            using (var upsertCommand = conn.db.CreateCommand())
            {
                upsertCommand.CommandText = "INSERT INTO connections (identityId,identity,displayName,status,accessIsRevoked,data,created) " +
                                             "VALUES (@identityId,@identity,@displayName,@status,@accessIsRevoked,@data,@created)"+
                                             "ON CONFLICT (identityId,identity) DO UPDATE "+
                                             "SET displayName = @displayName,status = @status,accessIsRevoked = @accessIsRevoked,data = @data,modified = @modified "+
                                             "RETURNING created, modified;";
                var upsertParam1 = upsertCommand.CreateParameter();
                upsertParam1.ParameterName = "@identityId";
                upsertCommand.Parameters.Add(upsertParam1);
                var upsertParam2 = upsertCommand.CreateParameter();
                upsertParam2.ParameterName = "@identity";
                upsertCommand.Parameters.Add(upsertParam2);
                var upsertParam3 = upsertCommand.CreateParameter();
                upsertParam3.ParameterName = "@displayName";
                upsertCommand.Parameters.Add(upsertParam3);
                var upsertParam4 = upsertCommand.CreateParameter();
                upsertParam4.ParameterName = "@status";
                upsertCommand.Parameters.Add(upsertParam4);
                var upsertParam5 = upsertCommand.CreateParameter();
                upsertParam5.ParameterName = "@accessIsRevoked";
                upsertCommand.Parameters.Add(upsertParam5);
                var upsertParam6 = upsertCommand.CreateParameter();
                upsertParam6.ParameterName = "@data";
                upsertCommand.Parameters.Add(upsertParam6);
                var upsertParam7 = upsertCommand.CreateParameter();
                upsertParam7.ParameterName = "@created";
                upsertCommand.Parameters.Add(upsertParam7);
                var upsertParam8 = upsertCommand.CreateParameter();
                upsertParam8.ParameterName = "@modified";
                upsertCommand.Parameters.Add(upsertParam8);
                var now = UnixTimeUtcUnique.Now();
                upsertParam1.Value = item.identityId.ToByteArray();
                upsertParam2.Value = item.identity.DomainName;
                upsertParam3.Value = item.displayName;
                upsertParam4.Value = item.status;
                upsertParam5.Value = item.accessIsRevoked;
                upsertParam6.Value = item.data ?? (object)DBNull.Value;
                upsertParam7.Value = now.uniqueTime;
                upsertParam8.Value = now.uniqueTime;
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
                      _cache.AddOrUpdate("TableConnectionsCRUD", item.identityId.ToString()+item.identity.DomainName, item);
                      return 1;
                   }
                }
                return 0;
            } // Using
        }

        internal virtual async Task<int> UpdateAsync(DatabaseConnection conn, ConnectionsRecord item)
        {
            DatabaseBase.AssertGuidNotEmpty(item.identityId, "Guid parameter identityId cannot be set to Empty GUID.");
            using (var updateCommand = conn.db.CreateCommand())
            {
                updateCommand.CommandText = "UPDATE connections " +
                                             "SET displayName = @displayName,status = @status,accessIsRevoked = @accessIsRevoked,data = @data,modified = @modified "+
                                             "WHERE (identityId = @identityId AND identity = @identity)";
                var updateParam1 = updateCommand.CreateParameter();
                updateParam1.ParameterName = "@identityId";
                updateCommand.Parameters.Add(updateParam1);
                var updateParam2 = updateCommand.CreateParameter();
                updateParam2.ParameterName = "@identity";
                updateCommand.Parameters.Add(updateParam2);
                var updateParam3 = updateCommand.CreateParameter();
                updateParam3.ParameterName = "@displayName";
                updateCommand.Parameters.Add(updateParam3);
                var updateParam4 = updateCommand.CreateParameter();
                updateParam4.ParameterName = "@status";
                updateCommand.Parameters.Add(updateParam4);
                var updateParam5 = updateCommand.CreateParameter();
                updateParam5.ParameterName = "@accessIsRevoked";
                updateCommand.Parameters.Add(updateParam5);
                var updateParam6 = updateCommand.CreateParameter();
                updateParam6.ParameterName = "@data";
                updateCommand.Parameters.Add(updateParam6);
                var updateParam7 = updateCommand.CreateParameter();
                updateParam7.ParameterName = "@created";
                updateCommand.Parameters.Add(updateParam7);
                var updateParam8 = updateCommand.CreateParameter();
                updateParam8.ParameterName = "@modified";
                updateCommand.Parameters.Add(updateParam8);
             var now = UnixTimeUtcUnique.Now();
                updateParam1.Value = item.identityId.ToByteArray();
                updateParam2.Value = item.identity.DomainName;
                updateParam3.Value = item.displayName;
                updateParam4.Value = item.status;
                updateParam5.Value = item.accessIsRevoked;
                updateParam6.Value = item.data ?? (object)DBNull.Value;
                updateParam7.Value = now.uniqueTime;
                updateParam8.Value = now.uniqueTime;
                var count = await conn.ExecuteNonQueryAsync(updateCommand);
                if (count > 0)
                {
                     item.modified = now;
                    _cache.AddOrUpdate("TableConnectionsCRUD", item.identityId.ToString()+item.identity.DomainName, item);
                }
                return count;
            } // Using
        }

        internal virtual async Task<int> GetCountDirtyAsync(DatabaseConnection conn)
        {
            using (var getCountCommand = conn.db.CreateCommand())
            {
                 // TODO: this is SQLite specific
                getCountCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM connections; PRAGMA read_uncommitted = 0;";
                var count = await conn.ExecuteScalarAsync(getCountCommand);
                if (count == null || count == DBNull.Value || !(count is int || count is long))
                    return -1;
                else
                    return Convert.ToInt32(count);
            }
        }

        public List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("identityId");
            sl.Add("identity");
            sl.Add("displayName");
            sl.Add("status");
            sl.Add("accessIsRevoked");
            sl.Add("data");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        // SELECT identityId,identity,displayName,status,accessIsRevoked,data,created,modified
        internal ConnectionsRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<ConnectionsRecord>();
            byte[] tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new ConnectionsRecord();

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
                item.identity = new OdinId(rdr.GetString(1));
            }

            if (rdr.IsDBNull(2))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.displayName = rdr.GetString(2);
            }

            if (rdr.IsDBNull(3))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.status = rdr.GetInt32(3);
            }

            if (rdr.IsDBNull(4))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.accessIsRevoked = rdr.GetInt32(4);
            }

            if (rdr.IsDBNull(5))
                item.data = null;
            else
            {
                bytesRead = rdr.GetBytes(5, 0, tmpbuf, 0, 65535+1);
                if (bytesRead > 65535)
                    throw new Exception("Too much data in data...");
                if (bytesRead < 0)
                    throw new Exception("Too little data in data...");
                item.data = new byte[bytesRead];
                Buffer.BlockCopy(tmpbuf, 0, item.data, 0, (int) bytesRead);
            }

            if (rdr.IsDBNull(6))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.created = new UnixTimeUtcUnique(rdr.GetInt64(6));
            }

            if (rdr.IsDBNull(7))
                item.modified = null;
            else
            {
                item.modified = new UnixTimeUtcUnique(rdr.GetInt64(7));
            }
            return item;
       }

        internal async Task<int> DeleteAsync(DatabaseConnection conn, Guid identityId,OdinId identity)
        {
            using (var delete0Command = conn.db.CreateCommand())
            {
                delete0Command.CommandText = "DELETE FROM connections " +
                                             "WHERE identityId = @identityId AND identity = @identity";
                var delete0Param1 = delete0Command.CreateParameter();
                delete0Param1.ParameterName = "@identityId";
                delete0Command.Parameters.Add(delete0Param1);
                var delete0Param2 = delete0Command.CreateParameter();
                delete0Param2.ParameterName = "@identity";
                delete0Command.Parameters.Add(delete0Param2);

                delete0Param1.Value = identityId.ToByteArray();
                delete0Param2.Value = identity.DomainName;
                var count = await conn.ExecuteNonQueryAsync(delete0Command);
                if (count > 0)
                    _cache.Remove("TableConnectionsCRUD", identityId.ToString()+identity.DomainName);
                return count;
            } // Using
        }

        internal ConnectionsRecord ReadRecordFromReader0(DbDataReader rdr, Guid identityId,OdinId identity)
        {
            var result = new List<ConnectionsRecord>();
            byte[] tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new ConnectionsRecord();
            item.identityId = identityId;
            item.identity = identity;

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.displayName = rdr.GetString(0);
            }

            if (rdr.IsDBNull(1))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.status = rdr.GetInt32(1);
            }

            if (rdr.IsDBNull(2))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.accessIsRevoked = rdr.GetInt32(2);
            }

            if (rdr.IsDBNull(3))
                item.data = null;
            else
            {
                bytesRead = rdr.GetBytes(3, 0, tmpbuf, 0, 65535+1);
                if (bytesRead > 65535)
                    throw new Exception("Too much data in data...");
                if (bytesRead < 0)
                    throw new Exception("Too little data in data...");
                item.data = new byte[bytesRead];
                Buffer.BlockCopy(tmpbuf, 0, item.data, 0, (int) bytesRead);
            }

            if (rdr.IsDBNull(4))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.created = new UnixTimeUtcUnique(rdr.GetInt64(4));
            }

            if (rdr.IsDBNull(5))
                item.modified = null;
            else
            {
                item.modified = new UnixTimeUtcUnique(rdr.GetInt64(5));
            }
            return item;
       }

        internal async Task<ConnectionsRecord> GetAsync(DatabaseConnection conn, Guid identityId,OdinId identity)
        {
            var (hit, cacheObject) = _cache.Get("TableConnectionsCRUD", identityId.ToString()+identity.DomainName);
            if (hit)
                return (ConnectionsRecord)cacheObject;
            using (var get0Command = conn.db.CreateCommand())
            {
                get0Command.CommandText = "SELECT displayName,status,accessIsRevoked,data,created,modified FROM connections " +
                                             "WHERE identityId = @identityId AND identity = @identity LIMIT 1;";
                var get0Param1 = get0Command.CreateParameter();
                get0Param1.ParameterName = "@identityId";
                get0Command.Parameters.Add(get0Param1);
                var get0Param2 = get0Command.CreateParameter();
                get0Param2.ParameterName = "@identity";
                get0Command.Parameters.Add(get0Param2);

                get0Param1.Value = identityId.ToByteArray();
                get0Param2.Value = identity.DomainName;
                {
                    using (var rdr = await conn.ExecuteReaderAsync(get0Command, System.Data.CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            _cache.AddOrUpdate("TableConnectionsCRUD", identityId.ToString()+identity.DomainName, null);
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr, identityId,identity);
                        _cache.AddOrUpdate("TableConnectionsCRUD", identityId.ToString()+identity.DomainName, r);
                        return r;
                    } // using
                } //
            } // using
        }

        internal async Task<(List<ConnectionsRecord>, string nextCursor)> PagingByIdentityAsync(DatabaseConnection conn, int count, Guid identityId, string inCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");
            if (inCursor == null)
                inCursor = "";

            using (var getPaging2Command = conn.db.CreateCommand())
            {
                getPaging2Command.CommandText = "SELECT identityId,identity,displayName,status,accessIsRevoked,data,created,modified FROM connections " +
                                            "WHERE (identityId = @identityId) AND identity > @identity ORDER BY identity ASC LIMIT $_count;";
                var getPaging2Param1 = getPaging2Command.CreateParameter();
                getPaging2Param1.ParameterName = "@identity";
                getPaging2Command.Parameters.Add(getPaging2Param1);
                var getPaging2Param2 = getPaging2Command.CreateParameter();
                getPaging2Param2.ParameterName = "$_count";
                getPaging2Command.Parameters.Add(getPaging2Param2);
                var getPaging2Param3 = getPaging2Command.CreateParameter();
                getPaging2Param3.ParameterName = "@identityId";
                getPaging2Command.Parameters.Add(getPaging2Param3);

                getPaging2Param1.Value = inCursor;
                getPaging2Param2.Value = count+1;
                getPaging2Param3.Value = identityId.ToByteArray();

                {
                    using (var rdr = await conn.ExecuteReaderAsync(getPaging2Command, System.Data.CommandBehavior.Default))
                    {
                        var result = new List<ConnectionsRecord>();
                        string nextCursor;
                        int n = 0;
                        while ((n < count) && await rdr.ReadAsync())
                        {
                            n++;
                            result.Add(ReadRecordFromReaderAll(rdr));
                        } // while
                        if ((n > 0) && await rdr.ReadAsync())
                        {
                                nextCursor = result[n - 1].identity;
                        }
                        else
                        {
                            nextCursor = null;
                        }
                        return (result, nextCursor);
                    } // using
                } //
            } // using 
        } // PagingGet

        internal async Task<(List<ConnectionsRecord>, string nextCursor)> PagingByIdentityAsync(DatabaseConnection conn, int count, Guid identityId,Int32 status, string inCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");
            if (inCursor == null)
                inCursor = "";

            using (var getPaging2Command = conn.db.CreateCommand())
            {
                getPaging2Command.CommandText = "SELECT identityId,identity,displayName,status,accessIsRevoked,data,created,modified FROM connections " +
                                            "WHERE (identityId = @identityId AND status = @status) AND identity > @identity ORDER BY identity ASC LIMIT $_count;";
                var getPaging2Param1 = getPaging2Command.CreateParameter();
                getPaging2Param1.ParameterName = "@identity";
                getPaging2Command.Parameters.Add(getPaging2Param1);
                var getPaging2Param2 = getPaging2Command.CreateParameter();
                getPaging2Param2.ParameterName = "$_count";
                getPaging2Command.Parameters.Add(getPaging2Param2);
                var getPaging2Param3 = getPaging2Command.CreateParameter();
                getPaging2Param3.ParameterName = "@identityId";
                getPaging2Command.Parameters.Add(getPaging2Param3);
                var getPaging2Param4 = getPaging2Command.CreateParameter();
                getPaging2Param4.ParameterName = "@status";
                getPaging2Command.Parameters.Add(getPaging2Param4);

                getPaging2Param1.Value = inCursor;
                getPaging2Param2.Value = count+1;
                getPaging2Param3.Value = identityId.ToByteArray();
                getPaging2Param4.Value = status;

                {
                    using (var rdr = await conn.ExecuteReaderAsync(getPaging2Command, System.Data.CommandBehavior.Default))
                    {
                        var result = new List<ConnectionsRecord>();
                        string nextCursor;
                        int n = 0;
                        while ((n < count) && await rdr.ReadAsync())
                        {
                            n++;
                            result.Add(ReadRecordFromReaderAll(rdr));
                        } // while
                        if ((n > 0) && await rdr.ReadAsync())
                        {
                                nextCursor = result[n - 1].identity;
                        }
                        else
                        {
                            nextCursor = null;
                        }
                        return (result, nextCursor);
                    } // using
                } //
            } // using 
        } // PagingGet

        internal async Task<(List<ConnectionsRecord>, UnixTimeUtcUnique? nextCursor)> PagingByCreatedAsync(DatabaseConnection conn, int count, Guid identityId,Int32 status, UnixTimeUtcUnique? inCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");
            if (inCursor == null)
                inCursor = new UnixTimeUtcUnique(long.MaxValue);

            using (var getPaging7Command = conn.db.CreateCommand())
            {
                getPaging7Command.CommandText = "SELECT identityId,identity,displayName,status,accessIsRevoked,data,created,modified FROM connections " +
                                            "WHERE (identityId = @identityId AND status = @status) AND created < @created ORDER BY created DESC LIMIT $_count;";
                var getPaging7Param1 = getPaging7Command.CreateParameter();
                getPaging7Param1.ParameterName = "@created";
                getPaging7Command.Parameters.Add(getPaging7Param1);
                var getPaging7Param2 = getPaging7Command.CreateParameter();
                getPaging7Param2.ParameterName = "$_count";
                getPaging7Command.Parameters.Add(getPaging7Param2);
                var getPaging7Param3 = getPaging7Command.CreateParameter();
                getPaging7Param3.ParameterName = "@identityId";
                getPaging7Command.Parameters.Add(getPaging7Param3);
                var getPaging7Param4 = getPaging7Command.CreateParameter();
                getPaging7Param4.ParameterName = "@status";
                getPaging7Command.Parameters.Add(getPaging7Param4);

                getPaging7Param1.Value = inCursor?.uniqueTime;
                getPaging7Param2.Value = count+1;
                getPaging7Param3.Value = identityId.ToByteArray();
                getPaging7Param4.Value = status;

                {
                    using (var rdr = await conn.ExecuteReaderAsync(getPaging7Command, System.Data.CommandBehavior.Default))
                    {
                        var result = new List<ConnectionsRecord>();
                        UnixTimeUtcUnique? nextCursor;
                        int n = 0;
                        while ((n < count) && await rdr.ReadAsync())
                        {
                            n++;
                            result.Add(ReadRecordFromReaderAll(rdr));
                        } // while
                        if ((n > 0) && await rdr.ReadAsync())
                        {
                                nextCursor = result[n - 1].created;
                        }
                        else
                        {
                            nextCursor = null;
                        }
                        return (result, nextCursor);
                    } // using
                } //
            } // using 
        } // PagingGet

        internal async Task<(List<ConnectionsRecord>, UnixTimeUtcUnique? nextCursor)> PagingByCreatedAsync(DatabaseConnection conn, int count, Guid identityId, UnixTimeUtcUnique? inCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");
            if (inCursor == null)
                inCursor = new UnixTimeUtcUnique(long.MaxValue);

            using (var getPaging7Command = conn.db.CreateCommand())
            {
                getPaging7Command.CommandText = "SELECT identityId,identity,displayName,status,accessIsRevoked,data,created,modified FROM connections " +
                                            "WHERE (identityId = @identityId) AND created < @created ORDER BY created DESC LIMIT $_count;";
                var getPaging7Param1 = getPaging7Command.CreateParameter();
                getPaging7Param1.ParameterName = "@created";
                getPaging7Command.Parameters.Add(getPaging7Param1);
                var getPaging7Param2 = getPaging7Command.CreateParameter();
                getPaging7Param2.ParameterName = "$_count";
                getPaging7Command.Parameters.Add(getPaging7Param2);
                var getPaging7Param3 = getPaging7Command.CreateParameter();
                getPaging7Param3.ParameterName = "@identityId";
                getPaging7Command.Parameters.Add(getPaging7Param3);

                getPaging7Param1.Value = inCursor?.uniqueTime;
                getPaging7Param2.Value = count+1;
                getPaging7Param3.Value = identityId.ToByteArray();

                {
                    using (var rdr = await conn.ExecuteReaderAsync(getPaging7Command, System.Data.CommandBehavior.Default))
                    {
                        var result = new List<ConnectionsRecord>();
                        UnixTimeUtcUnique? nextCursor;
                        int n = 0;
                        while ((n < count) && await rdr.ReadAsync())
                        {
                            n++;
                            result.Add(ReadRecordFromReaderAll(rdr));
                        } // while
                        if ((n > 0) && await rdr.ReadAsync())
                        {
                                nextCursor = result[n - 1].created;
                        }
                        else
                        {
                            nextCursor = null;
                        }
                        return (result, nextCursor);
                    } // using
                } //
            } // using 
        } // PagingGet

    }
}
