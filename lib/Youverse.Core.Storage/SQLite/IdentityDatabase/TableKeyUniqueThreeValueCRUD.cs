using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Youverse.Core.Identity;

namespace Youverse.Core.Storage.Sqlite.IdentityDatabase
{
    public class KeyUniqueThreeValueRecord
    {
        private Guid _key1;
        public Guid key1
        {
           get {
                   return _key1;
               }
           set {
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

        public TableKeyUniqueThreeValueCRUD(IdentityDatabase db) : base(db)
        {
        }

        ~TableKeyUniqueThreeValueCRUD()
        {
            if (_disposed == false) throw new Exception("TableKeyUniqueThreeValueCRUD Not disposed properly");
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
        }

        public sealed override void EnsureTableExists(bool dropExisting = false)
        {
            using (var cmd = _database.CreateCommand())
            {
                if (dropExisting)
                {
                    cmd.CommandText = "DROP TABLE IF EXISTS keyUniqueThreeValue;";
                    cmd.ExecuteNonQuery();
                }
                cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS keyUniqueThreeValue("
                     +"key1 BLOB NOT NULL UNIQUE, "
                     +"key2 BLOB NOT NULL, "
                     +"key3 BLOB NOT NULL, "
                     +"data BLOB  "
                     +", PRIMARY KEY (key1)"
                     +", UNIQUE(key2,key3)"
                     +");"
                     +"CREATE INDEX IF NOT EXISTS Idx0TableKeyUniqueThreeValueCRUD ON keyUniqueThreeValue(key2);"
                     +"CREATE INDEX IF NOT EXISTS Idx1TableKeyUniqueThreeValueCRUD ON keyUniqueThreeValue(key3);"
                     ;
                cmd.ExecuteNonQuery();
            }
        }

        public virtual int Insert(KeyUniqueThreeValueRecord item)
        {
            lock (_insertLock)
            {
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand();
                    _insertCommand.CommandText = "INSERT INTO keyUniqueThreeValue (key1,key2,key3,data) " +
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
                _insertParam1.Value = item.key1.ToByteArray();
                _insertParam2.Value = item.key2;
                _insertParam3.Value = item.key3;
                _insertParam4.Value = item.data ?? (object)DBNull.Value;
                _database.BeginTransaction();
                return _database.ExecuteNonQuery(_insertCommand);
            } // Lock
        }

        public virtual int Upsert(KeyUniqueThreeValueRecord item)
        {
            lock (_upsertLock)
            {
                if (_upsertCommand == null)
                {
                    _upsertCommand = _database.CreateCommand();
                    _upsertCommand.CommandText = "INSERT INTO keyUniqueThreeValue (key1,key2,key3,data) " +
                                                 "VALUES ($key1,$key2,$key3,$data)"+
                                                 "ON CONFLICT (key1) DO UPDATE "+
                                                 "SET key2 = $key2,key3 = $key3,data = $data;";
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
                _upsertParam1.Value = item.key1.ToByteArray();
                _upsertParam2.Value = item.key2;
                _upsertParam3.Value = item.key3;
                _upsertParam4.Value = item.data ?? (object)DBNull.Value;
                _database.BeginTransaction();
                return _database.ExecuteNonQuery(_upsertCommand);
            } // Lock
        }

        public virtual int Update(KeyUniqueThreeValueRecord item)
        {
            lock (_updateLock)
            {
                if (_updateCommand == null)
                {
                    _updateCommand = _database.CreateCommand();
                    _updateCommand.CommandText = "UPDATE keyUniqueThreeValue " +
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
                _updateParam1.Value = item.key1.ToByteArray();
                _updateParam2.Value = item.key2;
                _updateParam3.Value = item.key3;
                _updateParam4.Value = item.data ?? (object)DBNull.Value;
                _database.BeginTransaction();
                return _database.ExecuteNonQuery(_updateCommand);
            } // Lock
        }

        public int Delete(Guid key1)
        {
            lock (_delete0Lock)
            {
                if (_delete0Command == null)
                {
                    _delete0Command = _database.CreateCommand();
                    _delete0Command.CommandText = "DELETE FROM keyUniqueThreeValue " +
                                                 "WHERE key1 = $key1";
                    _delete0Param1 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param1);
                    _delete0Param1.ParameterName = "$key1";
                    _delete0Command.Prepare();
                }
                _delete0Param1.Value = key1.ToByteArray();
                _database.BeginTransaction();
                return _database.ExecuteNonQuery(_delete0Command);
            } // Lock
        }

        public List<byte[]> GetByKeyTwo(byte[] key2)
        {
            if (key2 == null) throw new Exception("Cannot be null");
            if (key2?.Length < 0) throw new Exception("Too short");
            if (key2?.Length > 256) throw new Exception("Too long");
            lock (_get0Lock)
            {
                if (_get0Command == null)
                {
                    _get0Command = _database.CreateCommand();
                    _get0Command.CommandText = "SELECT data FROM keyUniqueThreeValue " +
                                                 "WHERE key2 = $key2;";
                    _get0Param1 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param1);
                    _get0Param1.ParameterName = "$key2";
                    _get0Command.Prepare();
                }
                _get0Param1.Value = key2;
                using (SqliteDataReader rdr = _database.ExecuteReader(_get0Command, System.Data.CommandBehavior.Default))
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

        public List<byte[]> GetByKeyThree(byte[] key3)
        {
            if (key3 == null) throw new Exception("Cannot be null");
            if (key3?.Length < 0) throw new Exception("Too short");
            if (key3?.Length > 256) throw new Exception("Too long");
            lock (_get1Lock)
            {
                if (_get1Command == null)
                {
                    _get1Command = _database.CreateCommand();
                    _get1Command.CommandText = "SELECT data FROM keyUniqueThreeValue " +
                                                 "WHERE key3 = $key3;";
                    _get1Param1 = _get1Command.CreateParameter();
                    _get1Command.Parameters.Add(_get1Param1);
                    _get1Param1.ParameterName = "$key3";
                    _get1Command.Prepare();
                }
                _get1Param1.Value = key3;
                using (SqliteDataReader rdr = _database.ExecuteReader(_get1Command, System.Data.CommandBehavior.Default))
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

        public List<KeyUniqueThreeValueRecord> GetByKeyTwoThree(byte[] key2,byte[] key3)
        {
            if (key2 == null) throw new Exception("Cannot be null");
            if (key2?.Length < 0) throw new Exception("Too short");
            if (key2?.Length > 256) throw new Exception("Too long");
            if (key3 == null) throw new Exception("Cannot be null");
            if (key3?.Length < 0) throw new Exception("Too short");
            if (key3?.Length > 256) throw new Exception("Too long");
            lock (_get2Lock)
            {
                if (_get2Command == null)
                {
                    _get2Command = _database.CreateCommand();
                    _get2Command.CommandText = "SELECT key1,data FROM keyUniqueThreeValue " +
                                                 "WHERE key2 = $key2 AND key3 = $key3;";
                    _get2Param1 = _get2Command.CreateParameter();
                    _get2Command.Parameters.Add(_get2Param1);
                    _get2Param1.ParameterName = "$key2";
                    _get2Param2 = _get2Command.CreateParameter();
                    _get2Command.Parameters.Add(_get2Param2);
                    _get2Param2.ParameterName = "$key3";
                    _get2Command.Prepare();
                }
                _get2Param1.Value = key2;
                _get2Param2.Value = key3;
                using (SqliteDataReader rdr = _database.ExecuteReader(_get2Command, System.Data.CommandBehavior.Default))
                {
                    var result = new List<KeyUniqueThreeValueRecord>();
                    if (!rdr.Read())
                        return null;
                    byte[] _tmpbuf = new byte[1048576+1];
#pragma warning disable CS0168
                    long bytesRead;
#pragma warning restore CS0168
                    var _guid = new byte[16];
                    while (true)
                    {
                        var item = new KeyUniqueThreeValueRecord();
                        item.key2 = key2;
                        item.key3 = key3;

                        if (rdr.IsDBNull(0))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            bytesRead = rdr.GetBytes(0, 0, _guid, 0, 16);
                            if (bytesRead != 16)
                                throw new Exception("Not a GUID in key1...");
                            item.key1 = new Guid(_guid);
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
                        result.Add(item);
                        if (!rdr.Read())
                           break;
                    } // while
                    return result;
                } // using
            } // lock
        }

        public KeyUniqueThreeValueRecord Get(Guid key1)
        {
            lock (_get3Lock)
            {
                if (_get3Command == null)
                {
                    _get3Command = _database.CreateCommand();
                    _get3Command.CommandText = "SELECT key2,key3,data FROM keyUniqueThreeValue " +
                                                 "WHERE key1 = $key1 LIMIT 1;";
                    _get3Param1 = _get3Command.CreateParameter();
                    _get3Command.Parameters.Add(_get3Param1);
                    _get3Param1.ParameterName = "$key1";
                    _get3Command.Prepare();
                }
                _get3Param1.Value = key1.ToByteArray();
                using (SqliteDataReader rdr = _database.ExecuteReader(_get3Command, System.Data.CommandBehavior.SingleRow))
                {
                    var result = new KeyUniqueThreeValueRecord();
                    if (!rdr.Read())
                        return null;
                    byte[] _tmpbuf = new byte[1048576+1];
#pragma warning disable CS0168
                    long bytesRead;
#pragma warning restore CS0168
                    var _guid = new byte[16];
                        var item = new KeyUniqueThreeValueRecord();
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
                } // using
            } // lock
        }

    }
}
