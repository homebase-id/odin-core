using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;
using Odin.Core.Identity;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class KeyValueRecord
    {
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

        public TableKeyValueCRUD(IdentityDatabase db, CacheHelper cache) : base(db)
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

        public sealed override void EnsureTableExists(DatabaseBase.DatabaseConnection conn, bool dropExisting = false)
        {
                using (var cmd = _database.CreateCommand())
                {
                    if (dropExisting)
                    {
                       cmd.CommandText = "DROP TABLE IF EXISTS keyValue;";
                        _database.ExecuteNonQuery(conn, cmd);
                    }
                    cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS keyValue("
                     +"key BLOB NOT NULL UNIQUE, "
                     +"data BLOB  "
                     +", PRIMARY KEY (key)"
                     +");"
                     ;
                    _database.ExecuteNonQuery(conn, cmd);
            }
        }

        public virtual int Insert(DatabaseBase.DatabaseConnection conn, KeyValueRecord item)
        {
                using (var _insertCommand = _database.CreateCommand())
                {
                    _insertCommand.CommandText = "INSERT INTO keyValue (key,data) " +
                                                 "VALUES ($key,$data)";
                    var _insertParam1 = _insertCommand.CreateParameter();
                    _insertParam1.ParameterName = "$key";
                    _insertCommand.Parameters.Add(_insertParam1);
                    var _insertParam2 = _insertCommand.CreateParameter();
                    _insertParam2.ParameterName = "$data";
                    _insertCommand.Parameters.Add(_insertParam2);
                _insertParam1.Value = item.key;
                _insertParam2.Value = item.data ?? (object)DBNull.Value;
                var count = _database.ExecuteNonQuery(conn, _insertCommand);
                if (count > 0)
                 {
                    _cache.AddOrUpdate("TableKeyValueCRUD", item.key.ToBase64(), item);
                 }
                return count;
                } // Using
        }

        public virtual int Upsert(DatabaseBase.DatabaseConnection conn, KeyValueRecord item)
        {
                using (var _upsertCommand = _database.CreateCommand())
                {
                    _upsertCommand.CommandText = "INSERT INTO keyValue (key,data) " +
                                                 "VALUES ($key,$data)"+
                                                 "ON CONFLICT (key) DO UPDATE "+
                                                 "SET data = $data "+
                                                 ";";
                    var _upsertParam1 = _upsertCommand.CreateParameter();
                    _upsertParam1.ParameterName = "$key";
                    _upsertCommand.Parameters.Add(_upsertParam1);
                    var _upsertParam2 = _upsertCommand.CreateParameter();
                    _upsertParam2.ParameterName = "$data";
                    _upsertCommand.Parameters.Add(_upsertParam2);
                _upsertParam1.Value = item.key;
                _upsertParam2.Value = item.data ?? (object)DBNull.Value;
                var count = _database.ExecuteNonQuery(conn, _upsertCommand);
                if (count > 0)
                    _cache.AddOrUpdate("TableKeyValueCRUD", item.key.ToBase64(), item);
                return count;
                } // Using
        }
        public virtual int Update(DatabaseBase.DatabaseConnection conn, KeyValueRecord item)
        {
                using (var _updateCommand = _database.CreateCommand())
                {
                    _updateCommand.CommandText = "UPDATE keyValue " +
                                                 "SET data = $data "+
                                                 "WHERE (key = $key)";
                    var _updateParam1 = _updateCommand.CreateParameter();
                    _updateParam1.ParameterName = "$key";
                    _updateCommand.Parameters.Add(_updateParam1);
                    var _updateParam2 = _updateCommand.CreateParameter();
                    _updateParam2.ParameterName = "$data";
                    _updateCommand.Parameters.Add(_updateParam2);
                _updateParam1.Value = item.key;
                _updateParam2.Value = item.data ?? (object)DBNull.Value;
                var count = _database.ExecuteNonQuery(conn, _updateCommand);
                if (count > 0)
                {
                    _cache.AddOrUpdate("TableKeyValueCRUD", item.key.ToBase64(), item);
                }
                return count;
                } // Using
        }

        public virtual int GetCount(DatabaseBase.DatabaseConnection conn)
        {
                using (var _getCountCommand = _database.CreateCommand())
                {
                    _getCountCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM keyValue; PRAGMA read_uncommitted = 0;";
                    var count = _database.ExecuteNonQuery(conn, _getCountCommand);
                    return count;
                }
        }

        // SELECT key,data
        public KeyValueRecord ReadRecordFromReaderAll(SqliteDataReader rdr)
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
                bytesRead = rdr.GetBytes(0, 0, _tmpbuf, 0, 48+1);
                if (bytesRead > 48)
                    throw new Exception("Too much data in key...");
                if (bytesRead < 16)
                    throw new Exception("Too little data in key...");
                item.key = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.key, 0, (int) bytesRead);
            }

            if (rdr.IsDBNull(1))
                item.data = null;
            else
            {
                bytesRead = rdr.GetBytes(1, 0, _tmpbuf, 0, 1048576+1);
                if (bytesRead > 1048576)
                    throw new Exception("Too much data in data...");
                if (bytesRead < 0)
                    throw new Exception("Too little data in data...");
                item.data = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.data, 0, (int) bytesRead);
            }
            return item;
       }

        public int Delete(DatabaseBase.DatabaseConnection conn, byte[] key)
        {
            if (key == null) throw new Exception("Cannot be null");
            if (key?.Length < 16) throw new Exception("Too short");
            if (key?.Length > 48) throw new Exception("Too long");
                using (var _delete0Command = _database.CreateCommand())
                {
                    _delete0Command.CommandText = "DELETE FROM keyValue " +
                                                 "WHERE key = $key";
                    var _delete0Param1 = _delete0Command.CreateParameter();
                    _delete0Param1.ParameterName = "$key";
                    _delete0Command.Parameters.Add(_delete0Param1);

                _delete0Param1.Value = key;
                var count = _database.ExecuteNonQuery(conn, _delete0Command);
                if (count > 0)
                    _cache.Remove("TableKeyValueCRUD", key.ToBase64());
                return count;
                } // Using
        }

        public KeyValueRecord ReadRecordFromReader0(SqliteDataReader rdr, byte[] key)
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

        public KeyValueRecord Get(DatabaseBase.DatabaseConnection conn, byte[] key)
        {
            if (key == null) throw new Exception("Cannot be null");
            if (key?.Length < 16) throw new Exception("Too short");
            if (key?.Length > 48) throw new Exception("Too long");
            var (hit, cacheObject) = _cache.Get("TableKeyValueCRUD", key.ToBase64());
            if (hit)
                return (KeyValueRecord)cacheObject;
                using (var _get0Command = _database.CreateCommand())
                {
                    _get0Command.CommandText = "SELECT data FROM keyValue " +
                                                 "WHERE key = $key LIMIT 1;";
                    var _get0Param1 = _get0Command.CreateParameter();
                    _get0Param1.ParameterName = "$key";
                    _get0Command.Parameters.Add(_get0Param1);

                _get0Param1.Value = key;
                    lock (conn._lock)
                    {
                using (SqliteDataReader rdr = _database.ExecuteReader(conn, _get0Command, System.Data.CommandBehavior.SingleRow))
                {
                    if (!rdr.Read())
                    {
                        _cache.AddOrUpdate("TableKeyValueCRUD", key.ToBase64(), null);
                        return null;
                    }
                    var r = ReadRecordFromReader0(rdr, key);
                    _cache.AddOrUpdate("TableKeyValueCRUD", key.ToBase64(), r);
                    return r;
                } // using
            } // lock
            } // using
        }

    }
}
