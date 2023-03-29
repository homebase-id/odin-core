using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Youverse.Core.Identity;

namespace Youverse.Core.Storage.Sqlite.IdentityDatabase
{
    public class KeyTwoValueRecord
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
                    if (value?.Length < 0) throw new Exception("Too short");
                    if (value?.Length > 128) throw new Exception("Too long");
                  _key2 = value;
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
    } // End of class KeyTwoValueRecord

    public class TableKeyTwoValueCRUD : TableBase
    {
        private bool _disposed = false;
        private SqliteCommand _insertCommand = null;
        private static Object _insertLock = new Object();
        private SqliteParameter _insertParam1 = null;
        private SqliteParameter _insertParam2 = null;
        private SqliteParameter _insertParam3 = null;
        private SqliteCommand _updateCommand = null;
        private static Object _updateLock = new Object();
        private SqliteParameter _updateParam1 = null;
        private SqliteParameter _updateParam2 = null;
        private SqliteParameter _updateParam3 = null;
        private SqliteCommand _upsertCommand = null;
        private static Object _upsertLock = new Object();
        private SqliteParameter _upsertParam1 = null;
        private SqliteParameter _upsertParam2 = null;
        private SqliteParameter _upsertParam3 = null;
        private SqliteCommand _delete0Command = null;
        private static Object _delete0Lock = new Object();
        private SqliteParameter _delete0Param1 = null;
        private SqliteCommand _get0Command = null;
        private static Object _get0Lock = new Object();
        private SqliteParameter _get0Param1 = null;
        private SqliteCommand _get1Command = null;
        private static Object _get1Lock = new Object();
        private SqliteParameter _get1Param1 = null;

        public TableKeyTwoValueCRUD(IdentityDatabase db) : base(db)
        {
        }

        ~TableKeyTwoValueCRUD()
        {
            if (_disposed == false) throw new Exception("TableKeyTwoValueCRUD Not disposed properly");
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
            _disposed = true;
        }

        public sealed override void EnsureTableExists(bool dropExisting = false)
        {
            using (var cmd = _database.CreateCommand())
            {
                if (dropExisting)
                {
                    cmd.CommandText = "DROP TABLE IF EXISTS keyTwoValue;";
                    _database.ExecuteNonQuery(cmd);
                }
                cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS keyTwoValue("
                     +"key1 BLOB NOT NULL UNIQUE, "
                     +"key2 BLOB , "
                     +"data BLOB  "
                     +", PRIMARY KEY (key1)"
                     +");"
                     +"CREATE INDEX IF NOT EXISTS Idx0TableKeyTwoValueCRUD ON keyTwoValue(key2);"
                     ;
                _database.ExecuteNonQuery(cmd);
                _database.Commit();
            }
        }

        public virtual int Insert(KeyTwoValueRecord item)
        {
            lock (_insertLock)
            {
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand();
                    _insertCommand.CommandText = "INSERT INTO keyTwoValue (key1,key2,data) " +
                                                 "VALUES ($key1,$key2,$data)";
                    _insertParam1 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam1);
                    _insertParam1.ParameterName = "$key1";
                    _insertParam2 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam2);
                    _insertParam2.ParameterName = "$key2";
                    _insertParam3 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam3);
                    _insertParam3.ParameterName = "$data";
                    _insertCommand.Prepare();
                }
                _insertParam1.Value = item.key1.ToByteArray();
                _insertParam2.Value = item.key2 ?? (object)DBNull.Value;
                _insertParam3.Value = item.data ?? (object)DBNull.Value;
                return _database.ExecuteNonQuery(_insertCommand);
            } // Lock
        }

        public virtual int Upsert(KeyTwoValueRecord item)
        {
            lock (_upsertLock)
            {
                if (_upsertCommand == null)
                {
                    _upsertCommand = _database.CreateCommand();
                    _upsertCommand.CommandText = "INSERT INTO keyTwoValue (key1,key2,data) " +
                                                 "VALUES ($key1,$key2,$data)"+
                                                 "ON CONFLICT (key1) DO UPDATE "+
                                                 "SET key2 = $key2,data = $data;";
                    _upsertParam1 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam1);
                    _upsertParam1.ParameterName = "$key1";
                    _upsertParam2 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam2);
                    _upsertParam2.ParameterName = "$key2";
                    _upsertParam3 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam3);
                    _upsertParam3.ParameterName = "$data";
                    _upsertCommand.Prepare();
                }
                _upsertParam1.Value = item.key1.ToByteArray();
                _upsertParam2.Value = item.key2 ?? (object)DBNull.Value;
                _upsertParam3.Value = item.data ?? (object)DBNull.Value;
                return _database.ExecuteNonQuery(_upsertCommand);
            } // Lock
        }

        public virtual int Update(KeyTwoValueRecord item)
        {
            lock (_updateLock)
            {
                if (_updateCommand == null)
                {
                    _updateCommand = _database.CreateCommand();
                    _updateCommand.CommandText = "UPDATE keyTwoValue " +
                                                 "SET key2 = $key2,data = $data "+
                                                 "WHERE (key1 = $key1)";
                    _updateParam1 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam1);
                    _updateParam1.ParameterName = "$key1";
                    _updateParam2 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam2);
                    _updateParam2.ParameterName = "$key2";
                    _updateParam3 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam3);
                    _updateParam3.ParameterName = "$data";
                    _updateCommand.Prepare();
                }
                _updateParam1.Value = item.key1.ToByteArray();
                _updateParam2.Value = item.key2 ?? (object)DBNull.Value;
                _updateParam3.Value = item.data ?? (object)DBNull.Value;
                return _database.ExecuteNonQuery(_updateCommand);
            } // Lock
        }

        // SELECT key1,key2,data
        public KeyTwoValueRecord ReadRecordFromReaderAll(SqliteDataReader rdr)
        {
            var result = new List<KeyTwoValueRecord>();
            byte[] _tmpbuf = new byte[1048576+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new KeyTwoValueRecord();

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
                item.key2 = null;
            else
            {
                bytesRead = rdr.GetBytes(1, 0, _tmpbuf, 0, 128+1);
                if (bytesRead > 128)
                    throw new Exception("Too much data in key2...");
                if (bytesRead < 0)
                    throw new Exception("Too little data in key2...");
                item.key2 = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.key2, 0, (int) bytesRead);
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

        public int Delete(Guid key1)
        {
            lock (_delete0Lock)
            {
                if (_delete0Command == null)
                {
                    _delete0Command = _database.CreateCommand();
                    _delete0Command.CommandText = "DELETE FROM keyTwoValue " +
                                                 "WHERE key1 = $key1";
                    _delete0Param1 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param1);
                    _delete0Param1.ParameterName = "$key1";
                    _delete0Command.Prepare();
                }
                _delete0Param1.Value = key1.ToByteArray();
                return _database.ExecuteNonQuery(_delete0Command);
            } // Lock
        }

        public KeyTwoValueRecord ReadRecordFromReader0(SqliteDataReader rdr, byte[] key2)
        {
            if (key2?.Length < 0) throw new Exception("Too short");
            if (key2?.Length > 128) throw new Exception("Too long");
            var result = new List<KeyTwoValueRecord>();
            byte[] _tmpbuf = new byte[1048576+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new KeyTwoValueRecord();
            item.key2 = key2;

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
            return item;
       }

        public List<KeyTwoValueRecord> GetByKeyTwo(byte[] key2)
        {
            if (key2?.Length < 0) throw new Exception("Too short");
            if (key2?.Length > 128) throw new Exception("Too long");
            lock (_get0Lock)
            {
                if (_get0Command == null)
                {
                    _get0Command = _database.CreateCommand();
                    _get0Command.CommandText = "SELECT key1,data FROM keyTwoValue " +
                                                 "WHERE key2 = $key2;";
                    _get0Param1 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param1);
                    _get0Param1.ParameterName = "$key2";
                    _get0Command.Prepare();
                }
                _get0Param1.Value = key2 ?? (object)DBNull.Value;
                using (SqliteDataReader rdr = _database.ExecuteReader(_get0Command, System.Data.CommandBehavior.Default))
                {
                    if (!rdr.Read())
                        return null;
                    var result = new List<KeyTwoValueRecord>();
                    while (true)
                    {
                        result.Add(ReadRecordFromReader0(rdr, key2));
                        if (!rdr.Read())
                            break;
                    }
                    return result;
                } // using
            } // lock
        }

        public KeyTwoValueRecord ReadRecordFromReader1(SqliteDataReader rdr, Guid key1)
        {
            var result = new List<KeyTwoValueRecord>();
            byte[] _tmpbuf = new byte[1048576+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new KeyTwoValueRecord();
            item.key1 = key1;

            if (rdr.IsDBNull(0))
                item.key2 = null;
            else
            {
                bytesRead = rdr.GetBytes(0, 0, _tmpbuf, 0, 128+1);
                if (bytesRead > 128)
                    throw new Exception("Too much data in key2...");
                if (bytesRead < 0)
                    throw new Exception("Too little data in key2...");
                item.key2 = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.key2, 0, (int) bytesRead);
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

        public KeyTwoValueRecord Get(Guid key1)
        {
            lock (_get1Lock)
            {
                if (_get1Command == null)
                {
                    _get1Command = _database.CreateCommand();
                    _get1Command.CommandText = "SELECT key2,data FROM keyTwoValue " +
                                                 "WHERE key1 = $key1 LIMIT 1;";
                    _get1Param1 = _get1Command.CreateParameter();
                    _get1Command.Parameters.Add(_get1Param1);
                    _get1Param1.ParameterName = "$key1";
                    _get1Command.Prepare();
                }
                _get1Param1.Value = key1.ToByteArray();
                using (SqliteDataReader rdr = _database.ExecuteReader(_get1Command, System.Data.CommandBehavior.SingleRow))
                {
                    if (!rdr.Read())
                        return null;
                    return ReadRecordFromReader1(rdr, key1);
                } // using
            } // lock
        }

    }
}
