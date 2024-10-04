using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;
using Odin.Core.Identity;
using Odin.Core.Exceptions;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class KeyUniqueThreeValueRecord
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
        private byte[] _key1;
        public byte[] key1
        {
           get {
                   return _key1;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 16) throw new Exception("Too short");
                    if (value?.Length > 48) throw new Exception("Too long");
                  _key1 = value;
               }
        }
        private byte[] _key2;
        public byte[] key2
        {
           get {
                   return _key2;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 0) throw new Exception("Too short");
                    if (value?.Length > 256) throw new Exception("Too long");
                  _key2 = value;
               }
        }
        private byte[] _key3;
        public byte[] key3
        {
           get {
                   return _key3;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 0) throw new Exception("Too short");
                    if (value?.Length > 256) throw new Exception("Too long");
                  _key3 = value;
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
    } // End of class KeyUniqueThreeValueRecord

    public class TableKeyUniqueThreeValueCRUD : TableBase
    {
        private bool _disposed = false;
        private readonly CacheHelper _cache;

        public TableKeyUniqueThreeValueCRUD(IdentityDatabase db, CacheHelper cache) : base(db, "keyUniqueThreeValue")
        {
            _cache = cache;
        }

        ~TableKeyUniqueThreeValueCRUD()
        {
            if (_disposed == false) throw new Exception("TableKeyUniqueThreeValueCRUD Not disposed properly");
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
                       cmd.CommandText = "DROP TABLE IF EXISTS keyUniqueThreeValue;";
                       conn.ExecuteNonQuery(cmd);
                    }
                    cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS keyUniqueThreeValue("
                     +"identityId BLOB NOT NULL, "
                     +"key1 BLOB NOT NULL UNIQUE, "
                     +"key2 BLOB NOT NULL, "
                     +"key3 BLOB NOT NULL, "
                     +"data BLOB  "
                     +", PRIMARY KEY (identityId,key1)"
                     +", UNIQUE(identityId,key2,key3)"
                     +");"
                     +"CREATE INDEX IF NOT EXISTS Idx0TableKeyUniqueThreeValueCRUD ON keyUniqueThreeValue(identityId,key2);"
                     +"CREATE INDEX IF NOT EXISTS Idx1TableKeyUniqueThreeValueCRUD ON keyUniqueThreeValue(key3);"
                     ;
                    conn.ExecuteNonQuery(cmd);
            }
        }

        protected virtual int Insert(DatabaseConnection conn, KeyUniqueThreeValueRecord item)
        {
           if (item.identityId == Guid.Empty)
              throw new OdinSystemException("Guid parameter identityId cannot be set to Empty GUID.");
            using (var _insertCommand = _database.CreateCommand())
            {
                _insertCommand.CommandText = "INSERT INTO keyUniqueThreeValue (identityId,key1,key2,key3,data) " +
                                             "VALUES (@identityId,@key1,@key2,@key3,@data)";
                var _insertParam1 = _insertCommand.CreateParameter();
                _insertParam1.ParameterName = "@identityId";
                _insertCommand.Parameters.Add(_insertParam1);
                var _insertParam2 = _insertCommand.CreateParameter();
                _insertParam2.ParameterName = "@key1";
                _insertCommand.Parameters.Add(_insertParam2);
                var _insertParam3 = _insertCommand.CreateParameter();
                _insertParam3.ParameterName = "@key2";
                _insertCommand.Parameters.Add(_insertParam3);
                var _insertParam4 = _insertCommand.CreateParameter();
                _insertParam4.ParameterName = "@key3";
                _insertCommand.Parameters.Add(_insertParam4);
                var _insertParam5 = _insertCommand.CreateParameter();
                _insertParam5.ParameterName = "@data";
                _insertCommand.Parameters.Add(_insertParam5);
                _insertParam1.Value = item.identityId.ToByteArray();
                _insertParam2.Value = item.key1;
                _insertParam3.Value = item.key2;
                _insertParam4.Value = item.key3;
                _insertParam5.Value = item.data ?? (object)DBNull.Value;
                var count = conn.ExecuteNonQuery(_insertCommand);
                if (count > 0)
                {
                    _cache.AddOrUpdate("TableKeyUniqueThreeValueCRUD", item.identityId.ToString()+item.key1.ToBase64(), item);
                }
                return count;
            } // Using
        }

        public virtual int TryInsert(DatabaseConnection conn, KeyUniqueThreeValueRecord item)
        {
           if (item.identityId == Guid.Empty)
              throw new OdinSystemException("Guid parameter identityId cannot be set to Empty GUID.");
            using (var _insertCommand = _database.CreateCommand())
            {
                _insertCommand.CommandText = "INSERT OR IGNORE INTO keyUniqueThreeValue (identityId,key1,key2,key3,data) " +
                                             "VALUES (@identityId,@key1,@key2,@key3,@data)";
                var _insertParam1 = _insertCommand.CreateParameter();
                _insertParam1.ParameterName = "@identityId";
                _insertCommand.Parameters.Add(_insertParam1);
                var _insertParam2 = _insertCommand.CreateParameter();
                _insertParam2.ParameterName = "@key1";
                _insertCommand.Parameters.Add(_insertParam2);
                var _insertParam3 = _insertCommand.CreateParameter();
                _insertParam3.ParameterName = "@key2";
                _insertCommand.Parameters.Add(_insertParam3);
                var _insertParam4 = _insertCommand.CreateParameter();
                _insertParam4.ParameterName = "@key3";
                _insertCommand.Parameters.Add(_insertParam4);
                var _insertParam5 = _insertCommand.CreateParameter();
                _insertParam5.ParameterName = "@data";
                _insertCommand.Parameters.Add(_insertParam5);
                _insertParam1.Value = item.identityId.ToByteArray();
                _insertParam2.Value = item.key1;
                _insertParam3.Value = item.key2;
                _insertParam4.Value = item.key3;
                _insertParam5.Value = item.data ?? (object)DBNull.Value;
                var count = conn.ExecuteNonQuery(_insertCommand);
                if (count > 0)
                {
                   _cache.AddOrUpdate("TableKeyUniqueThreeValueCRUD", item.identityId.ToString()+item.key1.ToBase64(), item);
                }
                return count;
            } // Using
        }

        protected virtual int Upsert(DatabaseConnection conn, KeyUniqueThreeValueRecord item)
        {
           if (item.identityId == Guid.Empty)
              throw new OdinSystemException("Guid parameter identityId cannot be set to Empty GUID.");
            using (var _upsertCommand = _database.CreateCommand())
            {
                _upsertCommand.CommandText = "INSERT INTO keyUniqueThreeValue (identityId,key1,key2,key3,data) " +
                                             "VALUES (@identityId,@key1,@key2,@key3,@data)"+
                                             "ON CONFLICT (identityId,key1) DO UPDATE "+
                                             "SET key2 = @key2,key3 = @key3,data = @data "+
                                             ";";
                var _upsertParam1 = _upsertCommand.CreateParameter();
                _upsertParam1.ParameterName = "@identityId";
                _upsertCommand.Parameters.Add(_upsertParam1);
                var _upsertParam2 = _upsertCommand.CreateParameter();
                _upsertParam2.ParameterName = "@key1";
                _upsertCommand.Parameters.Add(_upsertParam2);
                var _upsertParam3 = _upsertCommand.CreateParameter();
                _upsertParam3.ParameterName = "@key2";
                _upsertCommand.Parameters.Add(_upsertParam3);
                var _upsertParam4 = _upsertCommand.CreateParameter();
                _upsertParam4.ParameterName = "@key3";
                _upsertCommand.Parameters.Add(_upsertParam4);
                var _upsertParam5 = _upsertCommand.CreateParameter();
                _upsertParam5.ParameterName = "@data";
                _upsertCommand.Parameters.Add(_upsertParam5);
                _upsertParam1.Value = item.identityId.ToByteArray();
                _upsertParam2.Value = item.key1;
                _upsertParam3.Value = item.key2;
                _upsertParam4.Value = item.key3;
                _upsertParam5.Value = item.data ?? (object)DBNull.Value;
                var count = conn.ExecuteNonQuery(_upsertCommand);
                if (count > 0)
                    _cache.AddOrUpdate("TableKeyUniqueThreeValueCRUD", item.identityId.ToString()+item.key1.ToBase64(), item);
                return count;
            } // Using
        }
        protected virtual int Update(DatabaseConnection conn, KeyUniqueThreeValueRecord item)
        {
           if (item.identityId == Guid.Empty)
              throw new OdinSystemException("Guid parameter identityId cannot be set to Empty GUID.");
            using (var _updateCommand = _database.CreateCommand())
            {
                _updateCommand.CommandText = "UPDATE keyUniqueThreeValue " +
                                             "SET key2 = @key2,key3 = @key3,data = @data "+
                                             "WHERE (identityId = @identityId AND key1 = @key1)";
                var _updateParam1 = _updateCommand.CreateParameter();
                _updateParam1.ParameterName = "@identityId";
                _updateCommand.Parameters.Add(_updateParam1);
                var _updateParam2 = _updateCommand.CreateParameter();
                _updateParam2.ParameterName = "@key1";
                _updateCommand.Parameters.Add(_updateParam2);
                var _updateParam3 = _updateCommand.CreateParameter();
                _updateParam3.ParameterName = "@key2";
                _updateCommand.Parameters.Add(_updateParam3);
                var _updateParam4 = _updateCommand.CreateParameter();
                _updateParam4.ParameterName = "@key3";
                _updateCommand.Parameters.Add(_updateParam4);
                var _updateParam5 = _updateCommand.CreateParameter();
                _updateParam5.ParameterName = "@data";
                _updateCommand.Parameters.Add(_updateParam5);
                _updateParam1.Value = item.identityId.ToByteArray();
                _updateParam2.Value = item.key1;
                _updateParam3.Value = item.key2;
                _updateParam4.Value = item.key3;
                _updateParam5.Value = item.data ?? (object)DBNull.Value;
                var count = conn.ExecuteNonQuery(_updateCommand);
                if (count > 0)
                {
                    _cache.AddOrUpdate("TableKeyUniqueThreeValueCRUD", item.identityId.ToString()+item.key1.ToBase64(), item);
                }
                return count;
            } // Using
        }

        protected virtual int GetCountDirty(DatabaseConnection conn)
        {
            using (var _getCountCommand = _database.CreateCommand())
            {
                _getCountCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM keyUniqueThreeValue; PRAGMA read_uncommitted = 0;";
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
            sl.Add("key1");
            sl.Add("key2");
            sl.Add("key3");
            sl.Add("data");
            return sl;
        }

        // SELECT identityId,key1,key2,key3,data
        protected KeyUniqueThreeValueRecord ReadRecordFromReaderAll(SqliteDataReader rdr)
        {
            var result = new List<KeyUniqueThreeValueRecord>();
            byte[] _tmpbuf = new byte[1048576+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new KeyUniqueThreeValueRecord();

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
                    throw new Exception("Too much data in key1...");
                if (bytesRead < 16)
                    throw new Exception("Too little data in key1...");
                item.key1 = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.key1, 0, (int) bytesRead);
            }

            if (rdr.IsDBNull(2))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(2, 0, _tmpbuf, 0, 256+1);
                if (bytesRead > 256)
                    throw new Exception("Too much data in key2...");
                if (bytesRead < 0)
                    throw new Exception("Too little data in key2...");
                item.key2 = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.key2, 0, (int) bytesRead);
            }

            if (rdr.IsDBNull(3))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(3, 0, _tmpbuf, 0, 256+1);
                if (bytesRead > 256)
                    throw new Exception("Too much data in key3...");
                if (bytesRead < 0)
                    throw new Exception("Too little data in key3...");
                item.key3 = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.key3, 0, (int) bytesRead);
            }

            if (rdr.IsDBNull(4))
                item.data = null;
            else
            {
                bytesRead = rdr.GetBytes(4, 0, _tmpbuf, 0, 1048576+1);
                if (bytesRead > 1048576)
                    throw new Exception("Too much data in data...");
                if (bytesRead < 0)
                    throw new Exception("Too little data in data...");
                item.data = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.data, 0, (int) bytesRead);
            }
            return item;
       }

        protected int Delete(DatabaseConnection conn, Guid identityId,byte[] key1)
        {
            if (key1 == null) throw new Exception("Cannot be null");
            if (key1?.Length < 16) throw new Exception("Too short");
            if (key1?.Length > 48) throw new Exception("Too long");
            using (var _delete0Command = _database.CreateCommand())
            {
                _delete0Command.CommandText = "DELETE FROM keyUniqueThreeValue " +
                                             "WHERE identityId = @identityId AND key1 = @key1";
                var _delete0Param1 = _delete0Command.CreateParameter();
                _delete0Param1.ParameterName = "@identityId";
                _delete0Command.Parameters.Add(_delete0Param1);
                var _delete0Param2 = _delete0Command.CreateParameter();
                _delete0Param2.ParameterName = "@key1";
                _delete0Command.Parameters.Add(_delete0Param2);

                _delete0Param1.Value = identityId.ToByteArray();
                _delete0Param2.Value = key1;
                var count = conn.ExecuteNonQuery(_delete0Command);
                if (count > 0)
                    _cache.Remove("TableKeyUniqueThreeValueCRUD", identityId.ToString()+key1.ToBase64());
                return count;
            } // Using
        }

        protected List<byte[]> GetByKeyTwo(DatabaseConnection conn, Guid identityId,byte[] key2)
        {
            if (key2 == null) throw new Exception("Cannot be null");
            if (key2?.Length < 0) throw new Exception("Too short");
            if (key2?.Length > 256) throw new Exception("Too long");
            using (var _get0Command = _database.CreateCommand())
            {
                _get0Command.CommandText = "SELECT data FROM keyUniqueThreeValue " +
                                             "WHERE identityId = @identityId AND key2 = @key2;";
                var _get0Param1 = _get0Command.CreateParameter();
                _get0Param1.ParameterName = "@identityId";
                _get0Command.Parameters.Add(_get0Param1);
                var _get0Param2 = _get0Command.CreateParameter();
                _get0Param2.ParameterName = "@key2";
                _get0Command.Parameters.Add(_get0Param2);

                _get0Param1.Value = identityId.ToByteArray();
                _get0Param2.Value = key2;
                lock (conn._lock)
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_get0Command, System.Data.CommandBehavior.Default))
                    {
                        byte[] result0tmp;
                        var thelistresult = new List<byte[]>();
                        if (!rdr.Read()) {
                            return null;
                        }
                    byte[] _tmpbuf = new byte[1048576+1];
#pragma warning disable CS0168
                    long bytesRead;
#pragma warning restore CS0168
                    var _guid = new byte[16];
                    while (true)
                    {

                        if (rdr.IsDBNull(0))
                            result0tmp = null;
                        else
                        {
                            bytesRead = rdr.GetBytes(0, 0, _tmpbuf, 0, 1048576+1);
                            if (bytesRead > 1048576)
                                throw new Exception("Too much data in data...");
                            if (bytesRead < 0)
                                throw new Exception("Too little data in data...");
                            result0tmp = new byte[bytesRead];
                            Buffer.BlockCopy(_tmpbuf, 0, result0tmp, 0, (int) bytesRead);
                        }
                        thelistresult.Add(result0tmp);
                        if (!rdr.Read())
                           break;
                    } // while
                    return thelistresult;
                    } // using
                } // lock
            } // using
        }

        protected List<byte[]> GetByKeyThree(DatabaseConnection conn, Guid identityId,byte[] key3)
        {
            if (key3 == null) throw new Exception("Cannot be null");
            if (key3?.Length < 0) throw new Exception("Too short");
            if (key3?.Length > 256) throw new Exception("Too long");
            using (var _get1Command = _database.CreateCommand())
            {
                _get1Command.CommandText = "SELECT data FROM keyUniqueThreeValue " +
                                             "WHERE identityId = @identityId AND key3 = @key3;";
                var _get1Param1 = _get1Command.CreateParameter();
                _get1Param1.ParameterName = "@identityId";
                _get1Command.Parameters.Add(_get1Param1);
                var _get1Param2 = _get1Command.CreateParameter();
                _get1Param2.ParameterName = "@key3";
                _get1Command.Parameters.Add(_get1Param2);

                _get1Param1.Value = identityId.ToByteArray();
                _get1Param2.Value = key3;
                lock (conn._lock)
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_get1Command, System.Data.CommandBehavior.Default))
                    {
                        byte[] result0tmp;
                        var thelistresult = new List<byte[]>();
                        if (!rdr.Read()) {
                            return null;
                        }
                    byte[] _tmpbuf = new byte[1048576+1];
#pragma warning disable CS0168
                    long bytesRead;
#pragma warning restore CS0168
                    var _guid = new byte[16];
                    while (true)
                    {

                        if (rdr.IsDBNull(0))
                            result0tmp = null;
                        else
                        {
                            bytesRead = rdr.GetBytes(0, 0, _tmpbuf, 0, 1048576+1);
                            if (bytesRead > 1048576)
                                throw new Exception("Too much data in data...");
                            if (bytesRead < 0)
                                throw new Exception("Too little data in data...");
                            result0tmp = new byte[bytesRead];
                            Buffer.BlockCopy(_tmpbuf, 0, result0tmp, 0, (int) bytesRead);
                        }
                        thelistresult.Add(result0tmp);
                        if (!rdr.Read())
                           break;
                    } // while
                    return thelistresult;
                    } // using
                } // lock
            } // using
        }

        protected KeyUniqueThreeValueRecord ReadRecordFromReader2(SqliteDataReader rdr, Guid identityId,byte[] key2,byte[] key3)
        {
            if (key2 == null) throw new Exception("Cannot be null");
            if (key2?.Length < 0) throw new Exception("Too short");
            if (key2?.Length > 256) throw new Exception("Too long");
            if (key3 == null) throw new Exception("Cannot be null");
            if (key3?.Length < 0) throw new Exception("Too short");
            if (key3?.Length > 256) throw new Exception("Too long");
            var result = new List<KeyUniqueThreeValueRecord>();
            byte[] _tmpbuf = new byte[1048576+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new KeyUniqueThreeValueRecord();
            item.identityId = identityId;
            item.key2 = key2;
            item.key3 = key3;

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(0, 0, _tmpbuf, 0, 48+1);
                if (bytesRead > 48)
                    throw new Exception("Too much data in key1...");
                if (bytesRead < 16)
                    throw new Exception("Too little data in key1...");
                item.key1 = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.key1, 0, (int) bytesRead);
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

        protected List<KeyUniqueThreeValueRecord> GetByKeyTwoThree(DatabaseConnection conn, Guid identityId,byte[] key2,byte[] key3)
        {
            if (key2 == null) throw new Exception("Cannot be null");
            if (key2?.Length < 0) throw new Exception("Too short");
            if (key2?.Length > 256) throw new Exception("Too long");
            if (key3 == null) throw new Exception("Cannot be null");
            if (key3?.Length < 0) throw new Exception("Too short");
            if (key3?.Length > 256) throw new Exception("Too long");
            using (var _get2Command = _database.CreateCommand())
            {
                _get2Command.CommandText = "SELECT key1,data FROM keyUniqueThreeValue " +
                                             "WHERE identityId = @identityId AND key2 = @key2 AND key3 = @key3;";
                var _get2Param1 = _get2Command.CreateParameter();
                _get2Param1.ParameterName = "@identityId";
                _get2Command.Parameters.Add(_get2Param1);
                var _get2Param2 = _get2Command.CreateParameter();
                _get2Param2.ParameterName = "@key2";
                _get2Command.Parameters.Add(_get2Param2);
                var _get2Param3 = _get2Command.CreateParameter();
                _get2Param3.ParameterName = "@key3";
                _get2Command.Parameters.Add(_get2Param3);

                _get2Param1.Value = identityId.ToByteArray();
                _get2Param2.Value = key2;
                _get2Param3.Value = key3;
                lock (conn._lock)
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_get2Command, System.Data.CommandBehavior.Default))
                    {
                        if (!rdr.Read())
                        {
                            _cache.AddOrUpdate("TableKeyUniqueThreeValueCRUD", identityId.ToString()+key2.ToBase64()+key3.ToBase64(), null);
                            return null;
                        }
                        var result = new List<KeyUniqueThreeValueRecord>();
                        while (true)
                        {
                            result.Add(ReadRecordFromReader2(rdr, identityId,key2,key3));
                            if (!rdr.Read())
                                break;
                        }
                        return result;
                    } // using
                } // lock
            } // using
        }

        protected KeyUniqueThreeValueRecord ReadRecordFromReader3(SqliteDataReader rdr, Guid identityId,byte[] key1)
        {
            if (key1 == null) throw new Exception("Cannot be null");
            if (key1?.Length < 16) throw new Exception("Too short");
            if (key1?.Length > 48) throw new Exception("Too long");
            var result = new List<KeyUniqueThreeValueRecord>();
            byte[] _tmpbuf = new byte[1048576+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new KeyUniqueThreeValueRecord();
            item.identityId = identityId;
            item.key1 = key1;

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(0, 0, _tmpbuf, 0, 256+1);
                if (bytesRead > 256)
                    throw new Exception("Too much data in key2...");
                if (bytesRead < 0)
                    throw new Exception("Too little data in key2...");
                item.key2 = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.key2, 0, (int) bytesRead);
            }

            if (rdr.IsDBNull(1))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(1, 0, _tmpbuf, 0, 256+1);
                if (bytesRead > 256)
                    throw new Exception("Too much data in key3...");
                if (bytesRead < 0)
                    throw new Exception("Too little data in key3...");
                item.key3 = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.key3, 0, (int) bytesRead);
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

        protected KeyUniqueThreeValueRecord Get(DatabaseConnection conn, Guid identityId,byte[] key1)
        {
            if (key1 == null) throw new Exception("Cannot be null");
            if (key1?.Length < 16) throw new Exception("Too short");
            if (key1?.Length > 48) throw new Exception("Too long");
            var (hit, cacheObject) = _cache.Get("TableKeyUniqueThreeValueCRUD", identityId.ToString()+key1.ToBase64());
            if (hit)
                return (KeyUniqueThreeValueRecord)cacheObject;
            using (var _get3Command = _database.CreateCommand())
            {
                _get3Command.CommandText = "SELECT key2,key3,data FROM keyUniqueThreeValue " +
                                             "WHERE identityId = @identityId AND key1 = @key1 LIMIT 1;";
                var _get3Param1 = _get3Command.CreateParameter();
                _get3Param1.ParameterName = "@identityId";
                _get3Command.Parameters.Add(_get3Param1);
                var _get3Param2 = _get3Command.CreateParameter();
                _get3Param2.ParameterName = "@key1";
                _get3Command.Parameters.Add(_get3Param2);

                _get3Param1.Value = identityId.ToByteArray();
                _get3Param2.Value = key1;
                lock (conn._lock)
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_get3Command, System.Data.CommandBehavior.SingleRow))
                    {
                        if (!rdr.Read())
                        {
                            _cache.AddOrUpdate("TableKeyUniqueThreeValueCRUD", identityId.ToString()+key1.ToBase64(), null);
                            return null;
                        }
                        var r = ReadRecordFromReader3(rdr, identityId,key1);
                        _cache.AddOrUpdate("TableKeyUniqueThreeValueCRUD", identityId.ToString()+key1.ToBase64(), r);
                        return r;
                    } // using
                } // lock
            } // using
        }

    }
}
