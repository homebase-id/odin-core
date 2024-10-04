using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;
using Odin.Core.Identity;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("DatabaseCommitTest")]
[assembly: InternalsVisibleTo("DatabaseConnectionTests")]

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class KeyValueRecord
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
        private byte[] _key;
        public byte[] key
        {
           get {
                   return _key;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 16) throw new Exception("Too short");
                    if (value?.Length > 48) throw new Exception("Too long");
                  _key = value;
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
                    if (value?.Length > 1048576) throw new Exception("Too long");
                  _data = value;
               }
        }
    } // End of class KeyValueRecord

    public class TableKeyValueCRUD : TableBase
    {
        private bool _disposed = false;
        private readonly CacheHelper _cache;

        public TableKeyValueCRUD(IdentityDatabase db, CacheHelper cache) : base(db, "keyValue")
        {
            _cache = cache;
        }

        ~TableKeyValueCRUD()
        {
            if (_disposed == false) throw new Exception("TableKeyValueCRUD Not disposed properly");
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
                       cmd.CommandText = "DROP TABLE IF EXISTS keyValue;";
                       conn.ExecuteNonQuery(cmd);
                    }
                    cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS keyValue("
                     +"identityId BLOB NOT NULL, "
                     +"key BLOB NOT NULL UNIQUE, "
                     +"data BLOB  "
                     +", PRIMARY KEY (identityId,key)"
                     +");"
                     ;
                    conn.ExecuteNonQuery(cmd);
            }
        }

        internal virtual int Insert(DatabaseConnection conn, KeyValueRecord item)
        {
            DatabaseBase.AssertGuidNotEmpty(item.identityId, "Guid parameter identityId cannot be set to Empty GUID.");
            using (var _insertCommand = _database.CreateCommand())
            {
                _insertCommand.CommandText = "INSERT INTO keyValue (identityId,key,data) " +
                                             "VALUES (@identityId,@key,@data)";
                var _insertParam1 = _insertCommand.CreateParameter();
                _insertParam1.ParameterName = "@identityId";
                _insertCommand.Parameters.Add(_insertParam1);
                var _insertParam2 = _insertCommand.CreateParameter();
                _insertParam2.ParameterName = "@key";
                _insertCommand.Parameters.Add(_insertParam2);
                var _insertParam3 = _insertCommand.CreateParameter();
                _insertParam3.ParameterName = "@data";
                _insertCommand.Parameters.Add(_insertParam3);
                _insertParam1.Value = item.identityId.ToByteArray();
                _insertParam2.Value = item.key;
                _insertParam3.Value = item.data ?? (object)DBNull.Value;
                var count = conn.ExecuteNonQuery(_insertCommand);
                if (count > 0)
                {
                    _cache.AddOrUpdate("TableKeyValueCRUD", item.identityId.ToString()+item.key.ToBase64(), item);
                }
                return count;
            } // Using
        }

        internal virtual int TryInsert(DatabaseConnection conn, KeyValueRecord item)
        {
            DatabaseBase.AssertGuidNotEmpty(item.identityId, "Guid parameter identityId cannot be set to Empty GUID.");
            using (var _insertCommand = _database.CreateCommand())
            {
                _insertCommand.CommandText = "INSERT OR IGNORE INTO keyValue (identityId,key,data) " +
                                             "VALUES (@identityId,@key,@data)";
                var _insertParam1 = _insertCommand.CreateParameter();
                _insertParam1.ParameterName = "@identityId";
                _insertCommand.Parameters.Add(_insertParam1);
                var _insertParam2 = _insertCommand.CreateParameter();
                _insertParam2.ParameterName = "@key";
                _insertCommand.Parameters.Add(_insertParam2);
                var _insertParam3 = _insertCommand.CreateParameter();
                _insertParam3.ParameterName = "@data";
                _insertCommand.Parameters.Add(_insertParam3);
                _insertParam1.Value = item.identityId.ToByteArray();
                _insertParam2.Value = item.key;
                _insertParam3.Value = item.data ?? (object)DBNull.Value;
                var count = conn.ExecuteNonQuery(_insertCommand);
                if (count > 0)
                {
                   _cache.AddOrUpdate("TableKeyValueCRUD", item.identityId.ToString()+item.key.ToBase64(), item);
                }
                return count;
            } // Using
        }

        internal virtual int Upsert(DatabaseConnection conn, KeyValueRecord item)
        {
            DatabaseBase.AssertGuidNotEmpty(item.identityId, "Guid parameter identityId cannot be set to Empty GUID.");
            using (var _upsertCommand = _database.CreateCommand())
            {
                _upsertCommand.CommandText = "INSERT INTO keyValue (identityId,key,data) " +
                                             "VALUES (@identityId,@key,@data)"+
                                             "ON CONFLICT (identityId,key) DO UPDATE "+
                                             "SET data = @data "+
                                             ";";
                var _upsertParam1 = _upsertCommand.CreateParameter();
                _upsertParam1.ParameterName = "@identityId";
                _upsertCommand.Parameters.Add(_upsertParam1);
                var _upsertParam2 = _upsertCommand.CreateParameter();
                _upsertParam2.ParameterName = "@key";
                _upsertCommand.Parameters.Add(_upsertParam2);
                var _upsertParam3 = _upsertCommand.CreateParameter();
                _upsertParam3.ParameterName = "@data";
                _upsertCommand.Parameters.Add(_upsertParam3);
                _upsertParam1.Value = item.identityId.ToByteArray();
                _upsertParam2.Value = item.key;
                _upsertParam3.Value = item.data ?? (object)DBNull.Value;
                var count = conn.ExecuteNonQuery(_upsertCommand);
                if (count > 0)
                    _cache.AddOrUpdate("TableKeyValueCRUD", item.identityId.ToString()+item.key.ToBase64(), item);
                return count;
            } // Using
        }
        internal virtual int Update(DatabaseConnection conn, KeyValueRecord item)
        {
            DatabaseBase.AssertGuidNotEmpty(item.identityId, "Guid parameter identityId cannot be set to Empty GUID.");
            using (var _updateCommand = _database.CreateCommand())
            {
                _updateCommand.CommandText = "UPDATE keyValue " +
                                             "SET data = @data "+
                                             "WHERE (identityId = @identityId AND key = @key)";
                var _updateParam1 = _updateCommand.CreateParameter();
                _updateParam1.ParameterName = "@identityId";
                _updateCommand.Parameters.Add(_updateParam1);
                var _updateParam2 = _updateCommand.CreateParameter();
                _updateParam2.ParameterName = "@key";
                _updateCommand.Parameters.Add(_updateParam2);
                var _updateParam3 = _updateCommand.CreateParameter();
                _updateParam3.ParameterName = "@data";
                _updateCommand.Parameters.Add(_updateParam3);
                _updateParam1.Value = item.identityId.ToByteArray();
                _updateParam2.Value = item.key;
                _updateParam3.Value = item.data ?? (object)DBNull.Value;
                var count = conn.ExecuteNonQuery(_updateCommand);
                if (count > 0)
                {
                    _cache.AddOrUpdate("TableKeyValueCRUD", item.identityId.ToString()+item.key.ToBase64(), item);
                }
                return count;
            } // Using
        }

        internal virtual int GetCountDirty(DatabaseConnection conn)
        {
            using (var _getCountCommand = _database.CreateCommand())
            {
                _getCountCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM keyValue; PRAGMA read_uncommitted = 0;";
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
            sl.Add("key");
            sl.Add("data");
            return sl;
        }

        // SELECT identityId,key,data
        internal KeyValueRecord ReadRecordFromReaderAll(SqliteDataReader rdr)
        {
            var result = new List<KeyValueRecord>();
            byte[] _tmpbuf = new byte[1048576+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new KeyValueRecord();

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
                bytesRead = rdr.GetBytes(1, 0, _tmpbuf, 0, 48+1);
                if (bytesRead > 48)
                    throw new Exception("Too much data in key...");
                if (bytesRead < 16)
                    throw new Exception("Too little data in key...");
                item.key = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.key, 0, (int) bytesRead);
            }

            if (rdr.IsDBNull(2))
                item.data = null;
            else
            {
                bytesRead = rdr.GetBytes(2, 0, _tmpbuf, 0, 1048576+1);
                if (bytesRead > 1048576)
                    throw new Exception("Too much data in data...");
                if (bytesRead < 0)
                    throw new Exception("Too little data in data...");
                item.data = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.data, 0, (int) bytesRead);
            }
            return item;
       }

        internal int Delete(DatabaseConnection conn, Guid identityId,byte[] key)
        {
            if (key == null) throw new Exception("Cannot be null");
            if (key?.Length < 16) throw new Exception("Too short");
            if (key?.Length > 48) throw new Exception("Too long");
            using (var _delete0Command = _database.CreateCommand())
            {
                _delete0Command.CommandText = "DELETE FROM keyValue " +
                                             "WHERE identityId = @identityId AND key = @key";
                var _delete0Param1 = _delete0Command.CreateParameter();
                _delete0Param1.ParameterName = "@identityId";
                _delete0Command.Parameters.Add(_delete0Param1);
                var _delete0Param2 = _delete0Command.CreateParameter();
                _delete0Param2.ParameterName = "@key";
                _delete0Command.Parameters.Add(_delete0Param2);

                _delete0Param1.Value = identityId.ToByteArray();
                _delete0Param2.Value = key;
                var count = conn.ExecuteNonQuery(_delete0Command);
                if (count > 0)
                    _cache.Remove("TableKeyValueCRUD", identityId.ToString()+key.ToBase64());
                return count;
            } // Using
        }

        internal KeyValueRecord ReadRecordFromReader0(SqliteDataReader rdr, Guid identityId,byte[] key)
        {
            if (key == null) throw new Exception("Cannot be null");
            if (key?.Length < 16) throw new Exception("Too short");
            if (key?.Length > 48) throw new Exception("Too long");
            var result = new List<KeyValueRecord>();
            byte[] _tmpbuf = new byte[1048576+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new KeyValueRecord();
            item.identityId = identityId;
            item.key = key;

            if (rdr.IsDBNull(0))
                item.data = null;
            else
            {
                bytesRead = rdr.GetBytes(0, 0, _tmpbuf, 0, 1048576+1);
                if (bytesRead > 1048576)
                    throw new Exception("Too much data in data...");
                if (bytesRead < 0)
                    throw new Exception("Too little data in data...");
                item.data = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.data, 0, (int) bytesRead);
            }
            return item;
       }

        internal KeyValueRecord Get(DatabaseConnection conn, Guid identityId,byte[] key)
        {
            if (key == null) throw new Exception("Cannot be null");
            if (key?.Length < 16) throw new Exception("Too short");
            if (key?.Length > 48) throw new Exception("Too long");
            var (hit, cacheObject) = _cache.Get("TableKeyValueCRUD", identityId.ToString()+key.ToBase64());
            if (hit)
                return (KeyValueRecord)cacheObject;
            using (var _get0Command = _database.CreateCommand())
            {
                _get0Command.CommandText = "SELECT data FROM keyValue " +
                                             "WHERE identityId = @identityId AND key = @key LIMIT 1;";
                var _get0Param1 = _get0Command.CreateParameter();
                _get0Param1.ParameterName = "@identityId";
                _get0Command.Parameters.Add(_get0Param1);
                var _get0Param2 = _get0Command.CreateParameter();
                _get0Param2.ParameterName = "@key";
                _get0Command.Parameters.Add(_get0Param2);

                _get0Param1.Value = identityId.ToByteArray();
                _get0Param2.Value = key;
                lock (conn._lock)
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_get0Command, System.Data.CommandBehavior.SingleRow))
                    {
                        if (!rdr.Read())
                        {
                            _cache.AddOrUpdate("TableKeyValueCRUD", identityId.ToString()+key.ToBase64(), null);
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr, identityId,key);
                        _cache.AddOrUpdate("TableKeyValueCRUD", identityId.ToString()+key.ToBase64(), r);
                        return r;
                    } // using
                } // lock
            } // using
        }

    }
}
