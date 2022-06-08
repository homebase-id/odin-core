using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace Youverse.Core.SystemStorage.KeyValue
{
    public class TableKeyTwoValue : TableKeyValueBase  // Make it IDisposable??
    {
        const int MAX_VALUE_LENGTH = 65535;  // Stored value cannot be longer than this

        private SQLiteCommand _insertCommand = null;
        private SQLiteParameter _iparam1 = null;
        private SQLiteParameter _iparam2 = null;
        private SQLiteParameter _iparam3 = null;
        private  Object _insertLock = new Object();

        private SQLiteCommand _upsertCommand = null;
        private SQLiteParameter _zparam1 = null;
        private SQLiteParameter _zparam2 = null;
        private SQLiteParameter _zparam3 = null;
        private  Object _upsertLock = new Object();

        private SQLiteCommand _updateCommand = null;
        private SQLiteParameter _uparam1 = null;
        private SQLiteParameter _uparam2 = null;
        private  Object _updateLock = new Object();

        private SQLiteCommand _deleteCommand = null;
        private SQLiteParameter _dparam1 = null;
        private  Object _deleteLock = new Object();

        private SQLiteCommand _selectCommand = null;
        private SQLiteParameter _sparam1 = null;
        private  Object _selectLock = new Object();

        private SQLiteCommand _selectTwoCommand = null;
        private SQLiteParameter _sparamTwo1 = null;
        private  Object _selectTwoLock = new Object();

        public TableKeyTwoValue(KeyValueDatabase db) : base(db)
        {
        }

        ~TableKeyTwoValue()
        {
            if (_insertCommand != null)
            {
                _insertCommand.Dispose();
                _insertCommand = null;
            }

            if (_updateCommand != null)
            {
                _updateCommand.Dispose();
                _updateCommand = null;
            }

            if (_upsertCommand != null)
            {
                _upsertCommand.Dispose();
                _upsertCommand = null;
            }

            if (_deleteCommand != null)
            {
                _deleteCommand.Dispose();
                _deleteCommand = null;
            }

            if (_selectCommand != null)
            {
                _selectCommand.Dispose();
                _selectCommand = null;
            }

            if (_selectTwoCommand != null)
            {
                _selectTwoCommand.Dispose();
                _selectTwoCommand = null;
            }
        }


        public override void EnsureTableExists(bool dropExisting = false)
        {
            using (var cmd = _keyValueDatabase.CreateCommand())
            {
                if(dropExisting)
                {
                    cmd.CommandText = "DROP TABLE IF EXISTS keytwovalue;";
                    cmd.ExecuteNonQuery();
                }
                
                cmd.CommandText =
                    @"CREATE TABLE if not exists keytwovalue(
                     key1   BLOB UNIQUE NOT NULL,
                     key2   BLOB NOT NULL,
                     value BLOB); "
                    + "CREATE INDEX idxkey1 ON keytwovalue(key1);"+
                      "CREATE INDEX idxkey2 ON keytwovalue(key2); ";

                cmd.ExecuteNonQuery();
            }
        }


        public byte[] Get(byte[] key1)
        {
            lock (_selectLock)
            {
                // Make sure we only prep once 
                if (_selectCommand == null)
                {
                    _selectCommand = _keyValueDatabase.CreateCommand();
                    _selectCommand.CommandText =
                        $"SELECT value FROM keytwovalue WHERE key1=$key1";
                    _sparam1 = _selectCommand.CreateParameter();
                    _sparam1.ParameterName = "$key1";
                    _selectCommand.Parameters.Add(_sparam1);
                    _selectCommand.Prepare();
                }

                _sparam1.Value = key1;
                byte[] _tmpbuf = new byte[MAX_VALUE_LENGTH];

                using (SQLiteDataReader rdr = _selectCommand.ExecuteReader(System.Data.CommandBehavior.SingleRow))
                {
                    if (!rdr.Read())
                        return null;

                    if (rdr.IsDBNull(0))
                        return null;

                    long n = rdr.GetBytes(0, 0, _tmpbuf, 0, MAX_VALUE_LENGTH);
                    if (n < 1)
                        throw new Exception("I suppose 0 might be OK, but negative not. No description in docs if it can be zero or negative");

                    if (n >= MAX_VALUE_LENGTH)
                        throw new Exception("Too much data...");

                    byte[] value = new byte[n];
                    Buffer.BlockCopy(_tmpbuf, 0, value, 0, (int)n);

                    return value;
                }
            }
        }


        public List<byte[]> GetByKeyTwo(byte[] key2)
        {
            lock (_selectTwoLock)
            {
                // Make sure we only prep once 
                if (_selectTwoCommand == null)
                {
                    _selectTwoCommand = _keyValueDatabase.CreateCommand();
                    _selectTwoCommand.CommandText =
                        $"SELECT value FROM keytwovalue WHERE key2=$key2";

                    _sparamTwo1 = _selectTwoCommand.CreateParameter();

                    _sparamTwo1.ParameterName = "$key2";

                    _selectTwoCommand.Parameters.Add(_sparamTwo1);
                    _selectTwoCommand.Prepare();
                }

                _sparamTwo1.Value = key2;
                byte[] _tmpbuf = new byte[MAX_VALUE_LENGTH];

                using (SQLiteDataReader rdr = _selectTwoCommand.ExecuteReader())
                {
                    List<byte[]> values = new List<byte[]>();
                    byte[] value = null;

                    while (rdr.Read())
                    {
                        long n = rdr.GetBytes(0, 0, _tmpbuf, 0, MAX_VALUE_LENGTH);
                        if (n < 1)
                            throw new Exception("I suppose 0 might be OK, but negative not. No description in docs if it can be zero or negative");

                        if (n >= MAX_VALUE_LENGTH)
                            throw new Exception("Too much data...");

                        value = new byte[n];
                        Buffer.BlockCopy(_tmpbuf, 0, value, 0, (int)n);
                        values.Add(value);
                    }

                    return values;
                }
            }
        }


        public void InsertRow(byte[] key1, byte[] key2, byte[] value)
        {
            if ((value != null) && (value.Length >= MAX_VALUE_LENGTH))
                throw new Exception("value too large");

            lock (_insertLock)
            {
                // Make sure we only prep once 
                if (_insertCommand == null)
                {
                    _insertCommand = _keyValueDatabase.CreateCommand();
                    _insertCommand.CommandText = @"INSERT INTO keytwovalue(key1, key2, value) VALUES ($key1, $key2, $value)";

                    _iparam1 = _insertCommand.CreateParameter();
                    _iparam1.ParameterName = "$key1";
                    _insertCommand.Parameters.Add(_iparam1);

                    _iparam2 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_iparam2);
                    _iparam2.ParameterName = "$key2";

                    _iparam3 = _insertCommand.CreateParameter();
                    _iparam3.ParameterName = "$value";
                    _insertCommand.Parameters.Add(_iparam3);

                    _insertCommand.Prepare();
                }

                _iparam1.Value = key1;
                _iparam2.Value = key2;
                _iparam3.Value = value;

                _insertCommand.ExecuteNonQuery();
            }
        }


        public void UpdateRow(byte[] key1, byte[] value)
        {
            if ((value != null) && (value.Length >= MAX_VALUE_LENGTH))
                throw new Exception("value too large");

            lock (_updateLock)
            {
                // Make sure we only prep once 
                if (_updateCommand == null)
                {
                    _updateCommand = _keyValueDatabase.CreateCommand();
                    _updateCommand.CommandText = @"UPDATE keytwovalue SET value=$value WHERE key1=$key1";

                    _uparam1 = _updateCommand.CreateParameter();
                    _uparam1.ParameterName = "$key1";
                    _updateCommand.Parameters.Add(_uparam1);

                    _uparam2 = _updateCommand.CreateParameter();
                    _uparam2.ParameterName = "$value";
                    _updateCommand.Parameters.Add(_uparam2);

                    _updateCommand.Prepare();
                }

                _uparam1.Value = key1;
                _uparam2.Value = value;

                _updateCommand.ExecuteNonQuery();
            }
        }

        public void UpsertRow(byte[] key1, byte[] key2, byte[] value)
        {
            if ((value != null) && (value.Length >= MAX_VALUE_LENGTH))
                throw new Exception("value too large");

            lock (_upsertLock)
            {
                // Make sure we only prep once 
                if (_upsertCommand == null)
                {
                    _upsertCommand = _keyValueDatabase.CreateCommand();
                    _upsertCommand.CommandText = @"INSERT INTO keytwovalue(key1, key2, value) VALUES ($key1, $key2, $value) ON CONFLICT (key1) DO UPDATE SET key2=$key2, value=$value";

                    _zparam1 = _upsertCommand.CreateParameter();
                    _zparam1.ParameterName = "$key1";
                    _upsertCommand.Parameters.Add(_zparam1);

                    _zparam2 = _upsertCommand.CreateParameter();
                    _zparam2.ParameterName = "$key2";
                    _upsertCommand.Parameters.Add(_zparam2);

                    _zparam3 = _upsertCommand.CreateParameter();
                    _zparam3.ParameterName = "$value";
                    _upsertCommand.Parameters.Add(_zparam3);

                    _upsertCommand.Prepare();
                }

                _zparam1.Value = key1;
                _zparam2.Value = key2;
                _zparam3.Value = value;

                _upsertCommand.ExecuteNonQuery();
            }
        }


        public void DeleteRow(byte[] key1)
        {
            lock (_deleteLock)
            {
                // Make sure we only prep once 
                if (_deleteCommand == null)
                {
                    _deleteCommand = _keyValueDatabase.CreateCommand();
                    _deleteCommand.CommandText = @"DELETE FROM keytwovalue WHERE key1=$key1";

                    _dparam1 = _deleteCommand.CreateParameter();
                    _dparam1.ParameterName = "$key1";
                    _deleteCommand.Parameters.Add(_dparam1);
                    _deleteCommand.Prepare();
                }

                _dparam1.Value = key1;

                int n = _deleteCommand.ExecuteNonQuery();
            }
        }
    }
}
