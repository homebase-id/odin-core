using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;
using Odin.Core.Identity;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class KeyThreeValueRecord
    {
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
    } // End of class KeyThreeValueRecord

    public class TableKeyThreeValueCRUD : TableBase
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
        private SqliteCommand _get0Command = null;
        private static Object _get0Lock = new Object();
        private SqliteParameter _get0Param1 = null;
        private SqliteCommand _get1Command = null;
        private static Object _get1Lock = new Object();
        private SqliteParameter _get1Param1 = null;
        private SqliteCommand _get2Command = null;
        private static Object _get2Lock = new Object();
        private SqliteParameter _get2Param1 = null;
        private SqliteParameter _get2Param2 = null;
        private SqliteCommand _get3Command = null;
        private static Object _get3Lock = new Object();
        private SqliteParameter _get3Param1 = null;
        private readonly CacheHelper _cache;

        public TableKeyThreeValueCRUD(IdentityDatabase db, CacheHelper cache) : base(db)
        {
            _cache = cache;
        }

        ~TableKeyThreeValueCRUD()
        {
            if (_disposed == false) throw new Exception("TableKeyThreeValueCRUD Not disposed properly");
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
            _get2Command?.Dispose();
            _get2Command = null;
            _get3Command?.Dispose();
            _get3Command = null;
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        public sealed override void EnsureTableExists(DatabaseBase.DatabaseConnection conn, bool dropExisting = false)
        {
                using (var cmd = _database.CreateCommand(conn))
                {
                    if (dropExisting)
                    {
                       cmd.CommandText = "DROP TABLE IF EXISTS keyThreeValue;";
                        _database.ExecuteNonQuery(conn, cmd);
                    }
                    cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS keyThreeValue("
                     +"key1 BLOB NOT NULL UNIQUE, "
                     +"key2 BLOB , "
                     +"key3 BLOB , "
                     +"data BLOB  "
                     +", PRIMARY KEY (key1)"
                     +");"
                     +"CREATE INDEX IF NOT EXISTS Idx0TableKeyThreeValueCRUD ON keyThreeValue(key2);"
                     +"CREATE INDEX IF NOT EXISTS Idx1TableKeyThreeValueCRUD ON keyThreeValue(key3);"
                     ;
                    _database.ExecuteNonQuery(conn, cmd);
            }
        }

        public virtual int Insert(DatabaseBase.DatabaseConnection conn, KeyThreeValueRecord item)
        {
            lock (_insertLock)
            {
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand(conn);
                    _insertCommand.CommandText = "INSERT INTO keyThreeValue (key1,key2,key3,data) " +
                                                 "VALUES ($key1,$key2,$key3,$data)";
                    _insertParam1 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam1);
                    _insertParam1.ParameterName = "$key1";
                    _insertParam2 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam2);
                    _insertParam2.ParameterName = "$key2";
                    _insertParam3 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam3);
                    _insertParam3.ParameterName = "$key3";
                    _insertParam4 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam4);
                    _insertParam4.ParameterName = "$data";
                    _insertCommand.Prepare();
                }
                _insertParam1.Value = item.key1;
                _insertParam2.Value = item.key2 ?? (object)DBNull.Value;
                _insertParam3.Value = item.key3 ?? (object)DBNull.Value;
                _insertParam4.Value = item.data ?? (object)DBNull.Value;
                var count = _database.ExecuteNonQuery(conn, _insertCommand);
                if (count > 0)
                 {
                    _cache.AddOrUpdate("TableKeyThreeValueCRUD", item.key1.ToBase64(), item);
                 }
                return count;
            } // Lock
        }

        public virtual int Upsert(DatabaseBase.DatabaseConnection conn, KeyThreeValueRecord item)
        {
            lock (_upsertLock)
            {
                if (_upsertCommand == null)
                {
                    _upsertCommand = _database.CreateCommand(conn);
                    _upsertCommand.CommandText = "INSERT INTO keyThreeValue (key1,key2,key3,data) " +
                                                 "VALUES ($key1,$key2,$key3,$data)"+
                                                 "ON CONFLICT (key1) DO UPDATE "+
                                                 "SET key2 = $key2,key3 = $key3,data = $data "+
                                                 ";";
                    _upsertParam1 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam1);
                    _upsertParam1.ParameterName = "$key1";
                    _upsertParam2 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam2);
                    _upsertParam2.ParameterName = "$key2";
                    _upsertParam3 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam3);
                    _upsertParam3.ParameterName = "$key3";
                    _upsertParam4 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam4);
                    _upsertParam4.ParameterName = "$data";
                    _upsertCommand.Prepare();
                }
                _upsertParam1.Value = item.key1;
                _upsertParam2.Value = item.key2 ?? (object)DBNull.Value;
                _upsertParam3.Value = item.key3 ?? (object)DBNull.Value;
                _upsertParam4.Value = item.data ?? (object)DBNull.Value;
                var count = _database.ExecuteNonQuery(conn, _upsertCommand);
                if (count > 0)
                    _cache.AddOrUpdate("TableKeyThreeValueCRUD", item.key1.ToBase64(), item);
                return count;
            } // Lock
        }
        public virtual int Update(DatabaseBase.DatabaseConnection conn, KeyThreeValueRecord item)
        {
            lock (_updateLock)
            {
                if (_updateCommand == null)
                {
                    _updateCommand = _database.CreateCommand(conn);
                    _updateCommand.CommandText = "UPDATE keyThreeValue " +
                                                 "SET key2 = $key2,key3 = $key3,data = $data "+
                                                 "WHERE (key1 = $key1)";
                    _updateParam1 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam1);
                    _updateParam1.ParameterName = "$key1";
                    _updateParam2 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam2);
                    _updateParam2.ParameterName = "$key2";
                    _updateParam3 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam3);
                    _updateParam3.ParameterName = "$key3";
                    _updateParam4 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam4);
                    _updateParam4.ParameterName = "$data";
                    _updateCommand.Prepare();
                }
                _updateParam1.Value = item.key1;
                _updateParam2.Value = item.key2 ?? (object)DBNull.Value;
                _updateParam3.Value = item.key3 ?? (object)DBNull.Value;
                _updateParam4.Value = item.data ?? (object)DBNull.Value;
                var count = _database.ExecuteNonQuery(conn, _updateCommand);
                if (count > 0)
                {
                    _cache.AddOrUpdate("TableKeyThreeValueCRUD", item.key1.ToBase64(), item);
                }
                return count;
            } // Lock
        }

        // SELECT key1,key2,key3,data
        public KeyThreeValueRecord ReadRecordFromReaderAll(SqliteDataReader rdr)
        {
            var result = new List<KeyThreeValueRecord>();
            byte[] _tmpbuf = new byte[1048576+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new KeyThreeValueRecord();

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
                item.key2 = null;
            else
            {
                bytesRead = rdr.GetBytes(1, 0, _tmpbuf, 0, 256+1);
                if (bytesRead > 256)
                    throw new Exception("Too much data in key2...");
                if (bytesRead < 0)
                    throw new Exception("Too little data in key2...");
                item.key2 = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.key2, 0, (int) bytesRead);
            }

            if (rdr.IsDBNull(2))
                item.key3 = null;
            else
            {
                bytesRead = rdr.GetBytes(2, 0, _tmpbuf, 0, 256+1);
                if (bytesRead > 256)
                    throw new Exception("Too much data in key3...");
                if (bytesRead < 0)
                    throw new Exception("Too little data in key3...");
                item.key3 = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.key3, 0, (int) bytesRead);
            }

            if (rdr.IsDBNull(3))
                item.data = null;
            else
            {
                bytesRead = rdr.GetBytes(3, 0, _tmpbuf, 0, 1048576+1);
                if (bytesRead > 1048576)
                    throw new Exception("Too much data in data...");
                if (bytesRead < 0)
                    throw new Exception("Too little data in data...");
                item.data = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.data, 0, (int) bytesRead);
            }
            return item;
       }

        public int Delete(DatabaseBase.DatabaseConnection conn, byte[] key1)
        {
            if (key1 == null) throw new Exception("Cannot be null");
            if (key1?.Length < 16) throw new Exception("Too short");
            if (key1?.Length > 48) throw new Exception("Too long");
            lock (_delete0Lock)
            {
                if (_delete0Command == null)
                {
                    _delete0Command = _database.CreateCommand(conn);
                    _delete0Command.CommandText = "DELETE FROM keyThreeValue " +
                                                 "WHERE key1 = $key1";
                    _delete0Param1 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param1);
                    _delete0Param1.ParameterName = "$key1";
                    _delete0Command.Prepare();
                }
                _delete0Param1.Value = key1;
                var count = _database.ExecuteNonQuery(conn, _delete0Command);
                if (count > 0)
                    _cache.Remove("TableKeyThreeValueCRUD", key1.ToBase64());
                return count;
            } // Lock
        }

        public List<byte[]> GetByKeyTwo(DatabaseBase.DatabaseConnection conn, byte[] key2)
        {
            if (key2?.Length < 0) throw new Exception("Too short");
            if (key2?.Length > 256) throw new Exception("Too long");
            lock (_get0Lock)
            {
                if (_get0Command == null)
                {
                    _get0Command = _database.CreateCommand(conn);
                    _get0Command.CommandText = "SELECT data FROM keyThreeValue " +
                                                 "WHERE key2 = $key2;";
                    _get0Param1 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param1);
                    _get0Param1.ParameterName = "$key2";
                    _get0Command.Prepare();
                }
                _get0Param1.Value = key2 ?? (object)DBNull.Value;
                using (SqliteDataReader rdr = _database.ExecuteReader(conn, _get0Command, System.Data.CommandBehavior.Default))
                {
                    byte[] result0tmp;
                    var thelistresult = new List<byte[]>();
                    if (!rdr.Read())
                        return null;
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
        }

        public List<byte[]> GetByKeyThree(DatabaseBase.DatabaseConnection conn, byte[] key3)
        {
            if (key3?.Length < 0) throw new Exception("Too short");
            if (key3?.Length > 256) throw new Exception("Too long");
            lock (_get1Lock)
            {
                if (_get1Command == null)
                {
                    _get1Command = _database.CreateCommand(conn);
                    _get1Command.CommandText = "SELECT data FROM keyThreeValue " +
                                                 "WHERE key3 = $key3;";
                    _get1Param1 = _get1Command.CreateParameter();
                    _get1Command.Parameters.Add(_get1Param1);
                    _get1Param1.ParameterName = "$key3";
                    _get1Command.Prepare();
                }
                _get1Param1.Value = key3 ?? (object)DBNull.Value;
                using (SqliteDataReader rdr = _database.ExecuteReader(conn, _get1Command, System.Data.CommandBehavior.Default))
                {
                    byte[] result0tmp;
                    var thelistresult = new List<byte[]>();
                    if (!rdr.Read())
                        return null;
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
        }

        public KeyThreeValueRecord ReadRecordFromReader2(SqliteDataReader rdr, byte[] key2,byte[] key3)
        {
            if (key2?.Length < 0) throw new Exception("Too short");
            if (key2?.Length > 256) throw new Exception("Too long");
            if (key3?.Length < 0) throw new Exception("Too short");
            if (key3?.Length > 256) throw new Exception("Too long");
            var result = new List<KeyThreeValueRecord>();
            byte[] _tmpbuf = new byte[1048576+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new KeyThreeValueRecord();
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

        public List<KeyThreeValueRecord> GetByKeyTwoThree(DatabaseBase.DatabaseConnection conn, byte[] key2,byte[] key3)
        {
            if (key2?.Length < 0) throw new Exception("Too short");
            if (key2?.Length > 256) throw new Exception("Too long");
            if (key3?.Length < 0) throw new Exception("Too short");
            if (key3?.Length > 256) throw new Exception("Too long");
            lock (_get2Lock)
            {
                if (_get2Command == null)
                {
                    _get2Command = _database.CreateCommand(conn);
                    _get2Command.CommandText = "SELECT key1,data FROM keyThreeValue " +
                                                 "WHERE key2 = $key2 AND key3 = $key3;";
                    _get2Param1 = _get2Command.CreateParameter();
                    _get2Command.Parameters.Add(_get2Param1);
                    _get2Param1.ParameterName = "$key2";
                    _get2Param2 = _get2Command.CreateParameter();
                    _get2Command.Parameters.Add(_get2Param2);
                    _get2Param2.ParameterName = "$key3";
                    _get2Command.Prepare();
                }
                _get2Param1.Value = key2 ?? (object)DBNull.Value;
                _get2Param2.Value = key3 ?? (object)DBNull.Value;
                using (SqliteDataReader rdr = _database.ExecuteReader(conn, _get2Command, System.Data.CommandBehavior.Default))
                {
                    if (!rdr.Read())
                    {
                        _cache.AddOrUpdate("TableKeyThreeValueCRUD", key2.ToBase64()+key3.ToBase64(), null);
                        return null;
                    }
                    var result = new List<KeyThreeValueRecord>();
                    while (true)
                    {
                        result.Add(ReadRecordFromReader2(rdr, key2,key3));
                        if (!rdr.Read())
                            break;
                    }
                    return result;
                } // using
            } // lock
        }

        public KeyThreeValueRecord ReadRecordFromReader3(SqliteDataReader rdr, byte[] key1)
        {
            if (key1 == null) throw new Exception("Cannot be null");
            if (key1?.Length < 16) throw new Exception("Too short");
            if (key1?.Length > 48) throw new Exception("Too long");
            var result = new List<KeyThreeValueRecord>();
            byte[] _tmpbuf = new byte[1048576+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new KeyThreeValueRecord();
            item.key1 = key1;

            if (rdr.IsDBNull(0))
                item.key2 = null;
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
                item.key3 = null;
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

        public KeyThreeValueRecord Get(DatabaseBase.DatabaseConnection conn, byte[] key1)
        {
            if (key1 == null) throw new Exception("Cannot be null");
            if (key1?.Length < 16) throw new Exception("Too short");
            if (key1?.Length > 48) throw new Exception("Too long");
            var (hit, cacheObject) = _cache.Get("TableKeyThreeValueCRUD", key1.ToBase64());
            if (hit)
                return (KeyThreeValueRecord)cacheObject;
            lock (_get3Lock)
            {
                if (_get3Command == null)
                {
                    _get3Command = _database.CreateCommand(conn);
                    _get3Command.CommandText = "SELECT key2,key3,data FROM keyThreeValue " +
                                                 "WHERE key1 = $key1 LIMIT 1;";
                    _get3Param1 = _get3Command.CreateParameter();
                    _get3Command.Parameters.Add(_get3Param1);
                    _get3Param1.ParameterName = "$key1";
                    _get3Command.Prepare();
                }
                _get3Param1.Value = key1;
                using (SqliteDataReader rdr = _database.ExecuteReader(conn, _get3Command, System.Data.CommandBehavior.SingleRow))
                {
                    if (!rdr.Read())
                    {
                        _cache.AddOrUpdate("TableKeyThreeValueCRUD", key1.ToBase64(), null);
                        return null;
                    }
                    var r = ReadRecordFromReader3(rdr, key1);
                    _cache.AddOrUpdate("TableKeyThreeValueCRUD", key1.ToBase64(), r);
                    return r;
                } // using
            } // lock
        }

    }
}
