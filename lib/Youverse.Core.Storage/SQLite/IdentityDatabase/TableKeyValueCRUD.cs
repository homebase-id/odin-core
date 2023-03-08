using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Youverse.Core.Identity;

namespace Youverse.Core.Storage.Sqlite.IdentityDatabase
{
    public class KeyValueRecord
    {
        private Guid _key;
        public Guid key
        {
           get {
                   return _key;
               }
           set {
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
        private SqliteCommand _insertCommand = null;
        private static Object _insertLock = new Object();
        private SqliteParameter _insertParam1 = null;
        private SqliteParameter _insertParam2 = null;
        private SqliteCommand _updateCommand = null;
        private static Object _updateLock = new Object();
        private SqliteParameter _updateParam1 = null;
        private SqliteParameter _updateParam2 = null;
        private SqliteCommand _upsertCommand = null;
        private static Object _upsertLock = new Object();
        private SqliteParameter _upsertParam1 = null;
        private SqliteParameter _upsertParam2 = null;
        private SqliteCommand _delete0Command = null;
        private static Object _delete0Lock = new Object();
        private SqliteParameter _delete0Param1 = null;
        private SqliteCommand _get0Command = null;
        private static Object _get0Lock = new Object();
        private SqliteParameter _get0Param1 = null;

        public TableKeyValueCRUD(IdentityDatabase db) : base(db)
        {
        }

        ~TableKeyValueCRUD()
        {
            if (_disposed == false) throw new Exception("TableKeyValueCRUD Not disposed properly");
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
            _disposed = true;
        }

        public sealed override void EnsureTableExists(bool dropExisting = false)
        {
            using (var cmd = _database.CreateCommand())
            {
                if (dropExisting)
                {
                    cmd.CommandText = "DROP TABLE IF EXISTS keyValue;";
                    cmd.ExecuteNonQuery(_database);
                }
                cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS keyValue("
                     +"key BLOB NOT NULL UNIQUE, "
                     +"data BLOB  "
                     +", PRIMARY KEY (key)"
                     +");"
                     ;
                cmd.ExecuteNonQuery(_database);
            }
        }

        public virtual int Insert(KeyValueRecord item)
        {
            lock (_insertLock)
            {
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand();
                    _insertCommand.CommandText = "INSERT INTO keyValue (key,data) " +
                                                 "VALUES ($key,$data)";
                    _insertParam1 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam1);
                    _insertParam1.ParameterName = "$key";
                    _insertParam2 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam2);
                    _insertParam2.ParameterName = "$data";
                    _insertCommand.Prepare();
                }
                _insertParam1.Value = item.key.ToByteArray();
                _insertParam2.Value = item.data ?? (object)DBNull.Value;
                _database.BeginTransaction();
                return _insertCommand.ExecuteNonQuery(_database);
            } // Lock
        }

        public virtual int Upsert(KeyValueRecord item)
        {
            lock (_upsertLock)
            {
                if (_upsertCommand == null)
                {
                    _upsertCommand = _database.CreateCommand();
                    _upsertCommand.CommandText = "INSERT INTO keyValue (key,data) " +
                                                 "VALUES ($key,$data)"+
                                                 "ON CONFLICT (key) DO UPDATE "+
                                                 "SET data = $data;";
                    _upsertParam1 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam1);
                    _upsertParam1.ParameterName = "$key";
                    _upsertParam2 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam2);
                    _upsertParam2.ParameterName = "$data";
                    _upsertCommand.Prepare();
                }
                _upsertParam1.Value = item.key.ToByteArray();
                _upsertParam2.Value = item.data ?? (object)DBNull.Value;
                _database.BeginTransaction();
                return _upsertCommand.ExecuteNonQuery(_database);
            } // Lock
        }

        public virtual int Update(KeyValueRecord item)
        {
            lock (_updateLock)
            {
                if (_updateCommand == null)
                {
                    _updateCommand = _database.CreateCommand();
                    _updateCommand.CommandText = "UPDATE keyValue " +
                                                 "SET data = $data "+
                                                 "WHERE (key = $key)";
                    _updateParam1 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam1);
                    _updateParam1.ParameterName = "$key";
                    _updateParam2 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam2);
                    _updateParam2.ParameterName = "$data";
                    _updateCommand.Prepare();
                }
                _updateParam1.Value = item.key.ToByteArray();
                _updateParam2.Value = item.data ?? (object)DBNull.Value;
                _database.BeginTransaction();
                return _updateCommand.ExecuteNonQuery(_database);
            } // Lock
        }

        public int Delete(Guid key)
        {
            lock (_delete0Lock)
            {
                if (_delete0Command == null)
                {
                    _delete0Command = _database.CreateCommand();
                    _delete0Command.CommandText = "DELETE FROM keyValue " +
                                                 "WHERE key = $key";
                    _delete0Param1 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param1);
                    _delete0Param1.ParameterName = "$key";
                    _delete0Command.Prepare();
                }
                _delete0Param1.Value = key.ToByteArray();
                _database.BeginTransaction();
                return _delete0Command.ExecuteNonQuery(_database);
            } // Lock
        }

        public KeyValueRecord Get(Guid key)
        {
            lock (_get0Lock)
            {
                if (_get0Command == null)
                {
                    _get0Command = _database.CreateCommand();
                    _get0Command.CommandText = "SELECT data FROM keyValue " +
                                                 "WHERE key = $key LIMIT 1;";
                    _get0Param1 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param1);
                    _get0Param1.ParameterName = "$key";
                    _get0Command.Prepare();
                }
                _get0Param1.Value = key.ToByteArray();
                using (SqliteDataReader rdr = _get0Command.ExecuteReader(System.Data.CommandBehavior.SingleRow, _database))
                {
                    var result = new KeyValueRecord();
                    if (!rdr.Read())
                        return null;
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
                } // using
            } // lock
        }

    }
}
