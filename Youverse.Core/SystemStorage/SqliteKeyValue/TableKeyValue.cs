using System;
using System.Data.SQLite;

namespace Youverse.Core.SystemStorage.SqliteKeyValue
{
    public class TableKeyValue : TableKeyValueBase // Make it IDisposable??
    {
        const int MAX_VALUE_LENGTH = 65535; // Stored value cannot be longer than this

        private SQLiteCommand _insertCommand = null;
        private SQLiteParameter _iparam1 = null;
        private SQLiteParameter _iparam2 = null;
        private Object _insertLock = new Object();

        private SQLiteCommand _updateCommand = null;
        private SQLiteParameter _uparam1 = null;
        private SQLiteParameter _uparam2 = null;
        private Object _updateLock = new Object();

        private SQLiteCommand _upsertCommand = null;
        private SQLiteParameter _zparam1 = null;
        private SQLiteParameter _zparam2 = null;
        private Object _upsertLock = new Object();

        private SQLiteCommand _selectCommand = null;
        private SQLiteParameter _sparam1 = null;
        private Object _selectLock = new Object();

        private SQLiteCommand _deleteCommand = null;
        private SQLiteParameter _dparam1 = null;
        private Object _deleteLock = new Object();


        public TableKeyValue(KeyValueDatabase db) : base(db)
        {
        }

        ~TableKeyValue()
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
        }


        public override void EnsureTableExists(bool dropExisting = false)
        {
            using (var cmd = _keyValueDatabase.CreateCommand())
            {
                if(dropExisting)
                {
                    cmd.CommandText = "DROP TABLE IF EXISTS keyvalue;";
                    cmd.ExecuteNonQuery();
                }

                cmd.CommandText =
                    @"CREATE TABLE if not exists keyvalue(
                     key BLOB UNIQUE NOT NULL, value BLOB); "
                    + "CREATE INDEX if not exists idxkey ON keyvalue(key);";

                cmd.ExecuteNonQuery();
            }
        }


        public byte[] Get(byte[] key)
        {
            lock (_selectLock)
            {
                // Make sure we only prep once 
                if (_selectCommand == null)
                {
                    _selectCommand = _keyValueDatabase.CreateCommand();
                    _selectCommand.CommandText =
                        $"SELECT value FROM keyvalue WHERE key=$key";
                    _sparam1 = _selectCommand.CreateParameter();
                    _sparam1.ParameterName = "$key";
                    _selectCommand.Parameters.Add(_sparam1);
                    _selectCommand.Prepare();
                }

                _sparam1.Value = key;
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


        public void InsertRow(byte[] key, byte[] value)
        {
            if ((value != null) && (value.Length >= MAX_VALUE_LENGTH))
                throw new Exception("value too large");

            lock (_insertLock)
            {
                // Make sure we only prep once 
                if (_insertCommand == null)
                {
                    _insertCommand = _keyValueDatabase.CreateCommand();
                    _insertCommand.CommandText = @"INSERT INTO keyvalue(key, value) VALUES ($key, $value)";

                    _iparam1 = _insertCommand.CreateParameter();
                    _iparam1.ParameterName = "$key";
                    _iparam2 = _insertCommand.CreateParameter();
                    _iparam2.ParameterName = "$value";

                    _insertCommand.Parameters.Add(_iparam1);
                    _insertCommand.Parameters.Add(_iparam2);
                    _insertCommand.Prepare();
                }

                _iparam1.Value = key;
                _iparam2.Value = value;

                _insertCommand.ExecuteNonQuery();
            }
        }


        public void UpdateRow(byte[] key, byte[] value)
        {
            if ((value != null) && (value.Length >= MAX_VALUE_LENGTH))
                throw new Exception("value too large");

            lock (_updateLock)
            {
                // Make sure we only prep once 
                if (_updateCommand == null)
                {
                    _updateCommand = _keyValueDatabase.CreateCommand();
                    _updateCommand.CommandText = @"UPDATE keyvalue SET value=$value WHERE key=$key";

                    _uparam1 = _updateCommand.CreateParameter();
                    _uparam2 = _updateCommand.CreateParameter();

                    _uparam1.ParameterName = "$key";
                    _updateCommand.Parameters.Add(_uparam1);

                    _uparam2.ParameterName = "$value";
                    _updateCommand.Parameters.Add(_uparam2);
                    _updateCommand.Prepare();
                }

                _uparam1.Value = key;
                _uparam2.Value = value;

                _updateCommand.ExecuteNonQuery();
            }
        }


        public void UpsertRow(byte[] key, byte[] value)
        {
            if ((value != null) && (value.Length >= MAX_VALUE_LENGTH))
                throw new Exception("value too large");

            lock (_upsertLock)
            {
                // Make sure we only prep once 
                if (_upsertCommand == null)
                {
                    _upsertCommand = _keyValueDatabase.CreateCommand();
                    _upsertCommand.CommandText = @"INSERT INTO keyvalue(key, value) VALUES ($key, $value) ON CONFLICT (key) DO UPDATE SET value=$value";

                    _zparam1 = _upsertCommand.CreateParameter();
                    _zparam1.ParameterName = "$key";
                    _zparam2 = _upsertCommand.CreateParameter();
                    _zparam2.ParameterName = "$value";

                    _upsertCommand.Parameters.Add(_zparam1);
                    _upsertCommand.Parameters.Add(_zparam2);
                    _upsertCommand.Prepare();
                }

                _zparam1.Value = key;
                _zparam2.Value = value;

                _upsertCommand.ExecuteNonQuery();
            }
        }


        public void DeleteRow(byte[] key)
        {
            lock (_deleteLock)
            {
                // Make sure we only prep once 
                if (_deleteCommand == null)
                {
                    _deleteCommand = _keyValueDatabase.CreateCommand();
                    _deleteCommand.CommandText = @"DELETE FROM keyvalue WHERE key=$key";

                    _dparam1 = _deleteCommand.CreateParameter();
                    _dparam1.ParameterName = "$key";
                    _deleteCommand.Parameters.Add(_dparam1);
                    _deleteCommand.Prepare();
                }

                _dparam1.Value = key;

                _deleteCommand.ExecuteNonQuery();
            }
        }
    }
}