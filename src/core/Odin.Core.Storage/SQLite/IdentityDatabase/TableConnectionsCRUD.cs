using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;
using Odin.Core.Identity;
using Odin.Core.Exceptions;

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

    public class TableConnectionsCRUD : TableBase
    {
        private bool _disposed = false;
        private readonly CacheHelper _cache;

        public TableConnectionsCRUD(IdentityDatabase db, CacheHelper cache) : base(db, "connections")
        {
            _cache = cache;
        }

        ~TableConnectionsCRUD()
        {
            if (_disposed == false) throw new Exception("TableConnectionsCRUD Not disposed properly");
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
                       cmd.CommandText = "DROP TABLE IF EXISTS connections;";
                       conn.ExecuteNonQuery(cmd);
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
                    conn.ExecuteNonQuery(cmd);
            }
        }

        protected virtual int Insert(DatabaseConnection conn, ConnectionsRecord item)
        {
           if (item.identityId == Guid.Empty)
              throw new OdinSystemException("Guid parameter identityId cannot be set to Empty GUID.");
            using (var _insertCommand = _database.CreateCommand())
            {
                _insertCommand.CommandText = "INSERT INTO connections (identityId,identity,displayName,status,accessIsRevoked,data,created,modified) " +
                                             "VALUES (@identityId,@identity,@displayName,@status,@accessIsRevoked,@data,@created,@modified)";
                var _insertParam1 = _insertCommand.CreateParameter();
                _insertParam1.ParameterName = "@identityId";
                _insertCommand.Parameters.Add(_insertParam1);
                var _insertParam2 = _insertCommand.CreateParameter();
                _insertParam2.ParameterName = "@identity";
                _insertCommand.Parameters.Add(_insertParam2);
                var _insertParam3 = _insertCommand.CreateParameter();
                _insertParam3.ParameterName = "@displayName";
                _insertCommand.Parameters.Add(_insertParam3);
                var _insertParam4 = _insertCommand.CreateParameter();
                _insertParam4.ParameterName = "@status";
                _insertCommand.Parameters.Add(_insertParam4);
                var _insertParam5 = _insertCommand.CreateParameter();
                _insertParam5.ParameterName = "@accessIsRevoked";
                _insertCommand.Parameters.Add(_insertParam5);
                var _insertParam6 = _insertCommand.CreateParameter();
                _insertParam6.ParameterName = "@data";
                _insertCommand.Parameters.Add(_insertParam6);
                var _insertParam7 = _insertCommand.CreateParameter();
                _insertParam7.ParameterName = "@created";
                _insertCommand.Parameters.Add(_insertParam7);
                var _insertParam8 = _insertCommand.CreateParameter();
                _insertParam8.ParameterName = "@modified";
                _insertCommand.Parameters.Add(_insertParam8);
                _insertParam1.Value = item.identityId.ToByteArray();
                _insertParam2.Value = item.identity.DomainName;
                _insertParam3.Value = item.displayName;
                _insertParam4.Value = item.status;
                _insertParam5.Value = item.accessIsRevoked;
                _insertParam6.Value = item.data ?? (object)DBNull.Value;
                var now = UnixTimeUtcUnique.Now();
                _insertParam7.Value = now.uniqueTime;
                item.modified = null;
                _insertParam8.Value = DBNull.Value;
                var count = conn.ExecuteNonQuery(_insertCommand);
                if (count > 0)
                {
                     item.created = now;
                    _cache.AddOrUpdate("TableConnectionsCRUD", item.identityId.ToString()+item.identity.DomainName, item);
                }
                return count;
            } // Using
        }

        public virtual int TryInsert(DatabaseConnection conn, ConnectionsRecord item)
        {
           if (item.identityId == Guid.Empty)
              throw new OdinSystemException("Guid parameter identityId cannot be set to Empty GUID.");
            using (var _insertCommand = _database.CreateCommand())
            {
                _insertCommand.CommandText = "INSERT OR IGNORE INTO connections (identityId,identity,displayName,status,accessIsRevoked,data,created,modified) " +
                                             "VALUES (@identityId,@identity,@displayName,@status,@accessIsRevoked,@data,@created,@modified)";
                var _insertParam1 = _insertCommand.CreateParameter();
                _insertParam1.ParameterName = "@identityId";
                _insertCommand.Parameters.Add(_insertParam1);
                var _insertParam2 = _insertCommand.CreateParameter();
                _insertParam2.ParameterName = "@identity";
                _insertCommand.Parameters.Add(_insertParam2);
                var _insertParam3 = _insertCommand.CreateParameter();
                _insertParam3.ParameterName = "@displayName";
                _insertCommand.Parameters.Add(_insertParam3);
                var _insertParam4 = _insertCommand.CreateParameter();
                _insertParam4.ParameterName = "@status";
                _insertCommand.Parameters.Add(_insertParam4);
                var _insertParam5 = _insertCommand.CreateParameter();
                _insertParam5.ParameterName = "@accessIsRevoked";
                _insertCommand.Parameters.Add(_insertParam5);
                var _insertParam6 = _insertCommand.CreateParameter();
                _insertParam6.ParameterName = "@data";
                _insertCommand.Parameters.Add(_insertParam6);
                var _insertParam7 = _insertCommand.CreateParameter();
                _insertParam7.ParameterName = "@created";
                _insertCommand.Parameters.Add(_insertParam7);
                var _insertParam8 = _insertCommand.CreateParameter();
                _insertParam8.ParameterName = "@modified";
                _insertCommand.Parameters.Add(_insertParam8);
                _insertParam1.Value = item.identityId.ToByteArray();
                _insertParam2.Value = item.identity.DomainName;
                _insertParam3.Value = item.displayName;
                _insertParam4.Value = item.status;
                _insertParam5.Value = item.accessIsRevoked;
                _insertParam6.Value = item.data ?? (object)DBNull.Value;
                var now = UnixTimeUtcUnique.Now();
                _insertParam7.Value = now.uniqueTime;
                item.modified = null;
                _insertParam8.Value = DBNull.Value;
                var count = conn.ExecuteNonQuery(_insertCommand);
                if (count > 0)
                {
                    item.created = now;
                   _cache.AddOrUpdate("TableConnectionsCRUD", item.identityId.ToString()+item.identity.DomainName, item);
                }
                return count;
            } // Using
        }

        protected virtual int Upsert(DatabaseConnection conn, ConnectionsRecord item)
        {
           if (item.identityId == Guid.Empty)
              throw new OdinSystemException("Guid parameter identityId cannot be set to Empty GUID.");
            using (var _upsertCommand = _database.CreateCommand())
            {
                _upsertCommand.CommandText = "INSERT INTO connections (identityId,identity,displayName,status,accessIsRevoked,data,created) " +
                                             "VALUES (@identityId,@identity,@displayName,@status,@accessIsRevoked,@data,@created)"+
                                             "ON CONFLICT (identityId,identity) DO UPDATE "+
                                             "SET displayName = @displayName,status = @status,accessIsRevoked = @accessIsRevoked,data = @data,modified = @modified "+
                                             "RETURNING created, modified;";
                var _upsertParam1 = _upsertCommand.CreateParameter();
                _upsertParam1.ParameterName = "@identityId";
                _upsertCommand.Parameters.Add(_upsertParam1);
                var _upsertParam2 = _upsertCommand.CreateParameter();
                _upsertParam2.ParameterName = "@identity";
                _upsertCommand.Parameters.Add(_upsertParam2);
                var _upsertParam3 = _upsertCommand.CreateParameter();
                _upsertParam3.ParameterName = "@displayName";
                _upsertCommand.Parameters.Add(_upsertParam3);
                var _upsertParam4 = _upsertCommand.CreateParameter();
                _upsertParam4.ParameterName = "@status";
                _upsertCommand.Parameters.Add(_upsertParam4);
                var _upsertParam5 = _upsertCommand.CreateParameter();
                _upsertParam5.ParameterName = "@accessIsRevoked";
                _upsertCommand.Parameters.Add(_upsertParam5);
                var _upsertParam6 = _upsertCommand.CreateParameter();
                _upsertParam6.ParameterName = "@data";
                _upsertCommand.Parameters.Add(_upsertParam6);
                var _upsertParam7 = _upsertCommand.CreateParameter();
                _upsertParam7.ParameterName = "@created";
                _upsertCommand.Parameters.Add(_upsertParam7);
                var _upsertParam8 = _upsertCommand.CreateParameter();
                _upsertParam8.ParameterName = "@modified";
                _upsertCommand.Parameters.Add(_upsertParam8);
                var now = UnixTimeUtcUnique.Now();
                _upsertParam1.Value = item.identityId.ToByteArray();
                _upsertParam2.Value = item.identity.DomainName;
                _upsertParam3.Value = item.displayName;
                _upsertParam4.Value = item.status;
                _upsertParam5.Value = item.accessIsRevoked;
                _upsertParam6.Value = item.data ?? (object)DBNull.Value;
                _upsertParam7.Value = now.uniqueTime;
                _upsertParam8.Value = now.uniqueTime;
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
                      _cache.AddOrUpdate("TableConnectionsCRUD", item.identityId.ToString()+item.identity.DomainName, item);
                      return 1;
                   }
                }
                return 0;
            } // Using
        }

        protected virtual int Update(DatabaseConnection conn, ConnectionsRecord item)
        {
           if (item.identityId == Guid.Empty)
              throw new OdinSystemException("Guid parameter identityId cannot be set to Empty GUID.");
            using (var _updateCommand = _database.CreateCommand())
            {
                _updateCommand.CommandText = "UPDATE connections " +
                                             "SET displayName = @displayName,status = @status,accessIsRevoked = @accessIsRevoked,data = @data,modified = @modified "+
                                             "WHERE (identityId = @identityId AND identity = @identity)";
                var _updateParam1 = _updateCommand.CreateParameter();
                _updateParam1.ParameterName = "@identityId";
                _updateCommand.Parameters.Add(_updateParam1);
                var _updateParam2 = _updateCommand.CreateParameter();
                _updateParam2.ParameterName = "@identity";
                _updateCommand.Parameters.Add(_updateParam2);
                var _updateParam3 = _updateCommand.CreateParameter();
                _updateParam3.ParameterName = "@displayName";
                _updateCommand.Parameters.Add(_updateParam3);
                var _updateParam4 = _updateCommand.CreateParameter();
                _updateParam4.ParameterName = "@status";
                _updateCommand.Parameters.Add(_updateParam4);
                var _updateParam5 = _updateCommand.CreateParameter();
                _updateParam5.ParameterName = "@accessIsRevoked";
                _updateCommand.Parameters.Add(_updateParam5);
                var _updateParam6 = _updateCommand.CreateParameter();
                _updateParam6.ParameterName = "@data";
                _updateCommand.Parameters.Add(_updateParam6);
                var _updateParam7 = _updateCommand.CreateParameter();
                _updateParam7.ParameterName = "@created";
                _updateCommand.Parameters.Add(_updateParam7);
                var _updateParam8 = _updateCommand.CreateParameter();
                _updateParam8.ParameterName = "@modified";
                _updateCommand.Parameters.Add(_updateParam8);
             var now = UnixTimeUtcUnique.Now();
                _updateParam1.Value = item.identityId.ToByteArray();
                _updateParam2.Value = item.identity.DomainName;
                _updateParam3.Value = item.displayName;
                _updateParam4.Value = item.status;
                _updateParam5.Value = item.accessIsRevoked;
                _updateParam6.Value = item.data ?? (object)DBNull.Value;
                _updateParam7.Value = now.uniqueTime;
                _updateParam8.Value = now.uniqueTime;
                var count = conn.ExecuteNonQuery(_updateCommand);
                if (count > 0)
                {
                     item.modified = now;
                    _cache.AddOrUpdate("TableConnectionsCRUD", item.identityId.ToString()+item.identity.DomainName, item);
                }
                return count;
            } // Using
        }

        protected virtual int GetCountDirty(DatabaseConnection conn)
        {
            using (var _getCountCommand = _database.CreateCommand())
            {
                _getCountCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM connections; PRAGMA read_uncommitted = 0;";
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
            sl.Add("displayName");
            sl.Add("status");
            sl.Add("accessIsRevoked");
            sl.Add("data");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        // SELECT identityId,identity,displayName,status,accessIsRevoked,data,created,modified
        protected ConnectionsRecord ReadRecordFromReaderAll(SqliteDataReader rdr)
        {
            var result = new List<ConnectionsRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new ConnectionsRecord();

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
                bytesRead = rdr.GetBytes(5, 0, _tmpbuf, 0, 65535+1);
                if (bytesRead > 65535)
                    throw new Exception("Too much data in data...");
                if (bytesRead < 0)
                    throw new Exception("Too little data in data...");
                item.data = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.data, 0, (int) bytesRead);
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

        protected int Delete(DatabaseConnection conn, Guid identityId,OdinId identity)
        {
            using (var _delete0Command = _database.CreateCommand())
            {
                _delete0Command.CommandText = "DELETE FROM connections " +
                                             "WHERE identityId = @identityId AND identity = @identity";
                var _delete0Param1 = _delete0Command.CreateParameter();
                _delete0Param1.ParameterName = "@identityId";
                _delete0Command.Parameters.Add(_delete0Param1);
                var _delete0Param2 = _delete0Command.CreateParameter();
                _delete0Param2.ParameterName = "@identity";
                _delete0Command.Parameters.Add(_delete0Param2);

                _delete0Param1.Value = identityId.ToByteArray();
                _delete0Param2.Value = identity.DomainName;
                var count = conn.ExecuteNonQuery(_delete0Command);
                if (count > 0)
                    _cache.Remove("TableConnectionsCRUD", identityId.ToString()+identity.DomainName);
                return count;
            } // Using
        }

        protected ConnectionsRecord ReadRecordFromReader0(SqliteDataReader rdr, Guid identityId,OdinId identity)
        {
            var result = new List<ConnectionsRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
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
                bytesRead = rdr.GetBytes(3, 0, _tmpbuf, 0, 65535+1);
                if (bytesRead > 65535)
                    throw new Exception("Too much data in data...");
                if (bytesRead < 0)
                    throw new Exception("Too little data in data...");
                item.data = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.data, 0, (int) bytesRead);
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

        protected ConnectionsRecord Get(DatabaseConnection conn, Guid identityId,OdinId identity)
        {
            var (hit, cacheObject) = _cache.Get("TableConnectionsCRUD", identityId.ToString()+identity.DomainName);
            if (hit)
                return (ConnectionsRecord)cacheObject;
            using (var _get0Command = _database.CreateCommand())
            {
                _get0Command.CommandText = "SELECT displayName,status,accessIsRevoked,data,created,modified FROM connections " +
                                             "WHERE identityId = @identityId AND identity = @identity LIMIT 1;";
                var _get0Param1 = _get0Command.CreateParameter();
                _get0Param1.ParameterName = "@identityId";
                _get0Command.Parameters.Add(_get0Param1);
                var _get0Param2 = _get0Command.CreateParameter();
                _get0Param2.ParameterName = "@identity";
                _get0Command.Parameters.Add(_get0Param2);

                _get0Param1.Value = identityId.ToByteArray();
                _get0Param2.Value = identity.DomainName;
                lock (conn._lock)
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_get0Command, System.Data.CommandBehavior.SingleRow))
                    {
                        if (!rdr.Read())
                        {
                            _cache.AddOrUpdate("TableConnectionsCRUD", identityId.ToString()+identity.DomainName, null);
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr, identityId,identity);
                        _cache.AddOrUpdate("TableConnectionsCRUD", identityId.ToString()+identity.DomainName, r);
                        return r;
                    } // using
                } // lock
            } // using
        }

        protected List<ConnectionsRecord> PagingByIdentity(DatabaseConnection conn, int count, Guid identityId, string inCursor, out string nextCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");
            if (inCursor == null)
                inCursor = "";

            using (var _getPaging2Command = _database.CreateCommand())
            {
                _getPaging2Command.CommandText = "SELECT identityId,identity,displayName,status,accessIsRevoked,data,created,modified FROM connections " +
                                            "WHERE (identityId = @identityId) AND identity > @identity ORDER BY identity ASC LIMIT $_count;";
                var _getPaging2Param1 = _getPaging2Command.CreateParameter();
                _getPaging2Param1.ParameterName = "@identity";
                _getPaging2Command.Parameters.Add(_getPaging2Param1);
                var _getPaging2Param2 = _getPaging2Command.CreateParameter();
                _getPaging2Param2.ParameterName = "$_count";
                _getPaging2Command.Parameters.Add(_getPaging2Param2);
                var _getPaging2Param3 = _getPaging2Command.CreateParameter();
                _getPaging2Param3.ParameterName = "@identityId";
                _getPaging2Command.Parameters.Add(_getPaging2Param3);

                _getPaging2Param1.Value = inCursor;
                _getPaging2Param2.Value = count+1;
                _getPaging2Param3.Value = identityId.ToByteArray();

                lock (conn._lock)
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_getPaging2Command, System.Data.CommandBehavior.Default))
                    {
                        var result = new List<ConnectionsRecord>();
                        int n = 0;
                        while ((n < count) && rdr.Read())
                        {
                            n++;
                            result.Add(ReadRecordFromReaderAll(rdr));
                        } // while
                        if ((n > 0) && rdr.Read())
                        {
                                nextCursor = result[n - 1].identity;
                        }
                        else
                        {
                            nextCursor = null;
                        }
                        return result;
                    } // using
                } // Lock
            } // using 
        } // PagingGet

        protected List<ConnectionsRecord> PagingByIdentity(DatabaseConnection conn, int count, Guid identityId,Int32 status, string inCursor, out string nextCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");
            if (inCursor == null)
                inCursor = "";

            using (var _getPaging2Command = _database.CreateCommand())
            {
                _getPaging2Command.CommandText = "SELECT identityId,identity,displayName,status,accessIsRevoked,data,created,modified FROM connections " +
                                            "WHERE (identityId = @identityId AND status = @status) AND identity > @identity ORDER BY identity ASC LIMIT $_count;";
                var _getPaging2Param1 = _getPaging2Command.CreateParameter();
                _getPaging2Param1.ParameterName = "@identity";
                _getPaging2Command.Parameters.Add(_getPaging2Param1);
                var _getPaging2Param2 = _getPaging2Command.CreateParameter();
                _getPaging2Param2.ParameterName = "$_count";
                _getPaging2Command.Parameters.Add(_getPaging2Param2);
                var _getPaging2Param3 = _getPaging2Command.CreateParameter();
                _getPaging2Param3.ParameterName = "@identityId";
                _getPaging2Command.Parameters.Add(_getPaging2Param3);
                var _getPaging2Param4 = _getPaging2Command.CreateParameter();
                _getPaging2Param4.ParameterName = "@status";
                _getPaging2Command.Parameters.Add(_getPaging2Param4);

                _getPaging2Param1.Value = inCursor;
                _getPaging2Param2.Value = count+1;
                _getPaging2Param3.Value = identityId.ToByteArray();
                _getPaging2Param4.Value = status;

                lock (conn._lock)
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_getPaging2Command, System.Data.CommandBehavior.Default))
                    {
                        var result = new List<ConnectionsRecord>();
                        int n = 0;
                        while ((n < count) && rdr.Read())
                        {
                            n++;
                            result.Add(ReadRecordFromReaderAll(rdr));
                        } // while
                        if ((n > 0) && rdr.Read())
                        {
                                nextCursor = result[n - 1].identity;
                        }
                        else
                        {
                            nextCursor = null;
                        }
                        return result;
                    } // using
                } // Lock
            } // using 
        } // PagingGet

        protected List<ConnectionsRecord> PagingByCreated(DatabaseConnection conn, int count, Guid identityId,Int32 status, UnixTimeUtcUnique? inCursor, out UnixTimeUtcUnique? nextCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");
            if (inCursor == null)
                inCursor = new UnixTimeUtcUnique(long.MaxValue);

            using (var _getPaging7Command = _database.CreateCommand())
            {
                _getPaging7Command.CommandText = "SELECT identityId,identity,displayName,status,accessIsRevoked,data,created,modified FROM connections " +
                                            "WHERE (identityId = @identityId AND status = @status) AND created < @created ORDER BY created DESC LIMIT $_count;";
                var _getPaging7Param1 = _getPaging7Command.CreateParameter();
                _getPaging7Param1.ParameterName = "@created";
                _getPaging7Command.Parameters.Add(_getPaging7Param1);
                var _getPaging7Param2 = _getPaging7Command.CreateParameter();
                _getPaging7Param2.ParameterName = "$_count";
                _getPaging7Command.Parameters.Add(_getPaging7Param2);
                var _getPaging7Param3 = _getPaging7Command.CreateParameter();
                _getPaging7Param3.ParameterName = "@identityId";
                _getPaging7Command.Parameters.Add(_getPaging7Param3);
                var _getPaging7Param4 = _getPaging7Command.CreateParameter();
                _getPaging7Param4.ParameterName = "@status";
                _getPaging7Command.Parameters.Add(_getPaging7Param4);

                _getPaging7Param1.Value = inCursor?.uniqueTime;
                _getPaging7Param2.Value = count+1;
                _getPaging7Param3.Value = identityId.ToByteArray();
                _getPaging7Param4.Value = status;

                lock (conn._lock)
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_getPaging7Command, System.Data.CommandBehavior.Default))
                    {
                        var result = new List<ConnectionsRecord>();
                        int n = 0;
                        while ((n < count) && rdr.Read())
                        {
                            n++;
                            result.Add(ReadRecordFromReaderAll(rdr));
                        } // while
                        if ((n > 0) && rdr.Read())
                        {
                                nextCursor = result[n - 1].created;
                        }
                        else
                        {
                            nextCursor = null;
                        }
                        return result;
                    } // using
                } // Lock
            } // using 
        } // PagingGet

        protected List<ConnectionsRecord> PagingByCreated(DatabaseConnection conn, int count, Guid identityId, UnixTimeUtcUnique? inCursor, out UnixTimeUtcUnique? nextCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");
            if (inCursor == null)
                inCursor = new UnixTimeUtcUnique(long.MaxValue);

            using (var _getPaging7Command = _database.CreateCommand())
            {
                _getPaging7Command.CommandText = "SELECT identityId,identity,displayName,status,accessIsRevoked,data,created,modified FROM connections " +
                                            "WHERE (identityId = @identityId) AND created < @created ORDER BY created DESC LIMIT $_count;";
                var _getPaging7Param1 = _getPaging7Command.CreateParameter();
                _getPaging7Param1.ParameterName = "@created";
                _getPaging7Command.Parameters.Add(_getPaging7Param1);
                var _getPaging7Param2 = _getPaging7Command.CreateParameter();
                _getPaging7Param2.ParameterName = "$_count";
                _getPaging7Command.Parameters.Add(_getPaging7Param2);
                var _getPaging7Param3 = _getPaging7Command.CreateParameter();
                _getPaging7Param3.ParameterName = "@identityId";
                _getPaging7Command.Parameters.Add(_getPaging7Param3);

                _getPaging7Param1.Value = inCursor?.uniqueTime;
                _getPaging7Param2.Value = count+1;
                _getPaging7Param3.Value = identityId.ToByteArray();

                lock (conn._lock)
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_getPaging7Command, System.Data.CommandBehavior.Default))
                    {
                        var result = new List<ConnectionsRecord>();
                        int n = 0;
                        while ((n < count) && rdr.Read())
                        {
                            n++;
                            result.Add(ReadRecordFromReaderAll(rdr));
                        } // while
                        if ((n > 0) && rdr.Read())
                        {
                                nextCursor = result[n - 1].created;
                        }
                        else
                        {
                            nextCursor = null;
                        }
                        return result;
                    } // using
                } // Lock
            } // using 
        } // PagingGet

    }
}
