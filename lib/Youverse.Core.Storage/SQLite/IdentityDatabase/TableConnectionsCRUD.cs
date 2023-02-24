using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace Youverse.Core.Storage.SQLite.IdentityDatabase
{
    public class ConnectionsItem
    {
        private string _identity;
        public string identity
        {
           get {
                   return _identity;
               }
           set {
                  if (value == null) throw new Exception("Cannot be null");
                  if (value != null) if (value.Length < 3) throw new Exception("Too short");
                  if (value != null) if (value.Length > 255) throw new Exception("Too long");
                  _identity = value;
               }
        }
        private string _displayname;
        public string displayname
        {
           get {
                   return _displayname;
               }
           set {
                  if (value == null) throw new Exception("Cannot be null");
                  if (value != null) if (value.Length < 0) throw new Exception("Too short");
                  if (value != null) if (value.Length > 80) throw new Exception("Too long");
                  _displayname = value;
               }
        }
        private Int32 _status;
        public Int32 status
        {
           get {
                   return _status;
               }
           set {
                  if (value == null) throw new Exception("Cannot be null");
                  _status = value;
               }
        }
        private Int32 _accessisrevoked;
        public Int32 accessisrevoked
        {
           get {
                   return _accessisrevoked;
               }
           set {
                  if (value == null) throw new Exception("Cannot be null");
                  _accessisrevoked = value;
               }
        }
        private byte[]? _data;
        public byte[]? data
        {
           get {
                   return _data;
               }
           set {
                  if (value != null) if (value.Length < 0) throw new Exception("Too short");
                  if (value != null) if (value.Length > 65535) throw new Exception("Too long");
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
        private UnixTimeUtc _modified;
        public UnixTimeUtc modified
        {
           get {
                   return _modified;
               }
           set {
                  if (value == null) throw new Exception("Cannot be null");
                  _modified = value;
               }
        }
    } // End of class ConnectionsItem

    public class TableConnectionsCRUD : TableBase
    {
        private bool _disposed = false;
        private SQLiteCommand _insertCommand = null;
        private static Object _insertLock = new Object();
        private SQLiteParameter _insertParam1 = null;
        private SQLiteParameter _insertParam2 = null;
        private SQLiteParameter _insertParam3 = null;
        private SQLiteParameter _insertParam4 = null;
        private SQLiteParameter _insertParam5 = null;
        private SQLiteParameter _insertParam6 = null;
        private SQLiteParameter _insertParam7 = null;
        private SQLiteCommand _updateCommand = null;
        private static Object _updateLock = new Object();
        private SQLiteParameter _updateParam1 = null;
        private SQLiteParameter _updateParam2 = null;
        private SQLiteParameter _updateParam3 = null;
        private SQLiteParameter _updateParam4 = null;
        private SQLiteParameter _updateParam5 = null;
        private SQLiteParameter _updateParam6 = null;
        private SQLiteParameter _updateParam7 = null;
        private SQLiteCommand _upsertCommand = null;
        private static Object _upsertLock = new Object();
        private SQLiteParameter _upsertParam1 = null;
        private SQLiteParameter _upsertParam2 = null;
        private SQLiteParameter _upsertParam3 = null;
        private SQLiteParameter _upsertParam4 = null;
        private SQLiteParameter _upsertParam5 = null;
        private SQLiteParameter _upsertParam6 = null;
        private SQLiteParameter _upsertParam7 = null;
        private SQLiteCommand _deleteCommand = null;
        private static Object _deleteLock = new Object();
        private SQLiteParameter _deleteParam1 = null;
        private SQLiteCommand _getCommand = null;
        private static Object _getLock = new Object();
        private SQLiteParameter _getParam1 = null;
        private SQLiteCommand _getPaging0Command = null;
        private static Object _getPaging0Lock = new Object();
        private SQLiteParameter _getPaging0Param1 = null;
        private SQLiteParameter _getPaging0Param2 = null;
        private SQLiteCommand _getPaging5Command = null;
        private static Object _getPaging5Lock = new Object();
        private SQLiteParameter _getPaging5Param1 = null;
        private SQLiteParameter _getPaging5Param2 = null;

        public TableConnectionsCRUD(IdentityDatabase db) : base(db)
        {
        }

        ~TableConnectionsCRUD()
        {
            if (_disposed == false) throw new Exception("Not disposed properly");
        }

        public override void Dispose()
        {
            _insertCommand?.Dispose();
            _insertCommand = null;
            _updateCommand?.Dispose();
            _updateCommand = null;
            _upsertCommand?.Dispose();
            _upsertCommand = null;
            _deleteCommand?.Dispose();
            _deleteCommand = null;
            _getCommand?.Dispose();
            _getCommand = null;
            _getPaging0Command?.Dispose();
            _getPaging0Command = null;
            _getPaging5Command?.Dispose();
            _getPaging5Command = null;
            _disposed = true;
        }

        public sealed override void EnsureTableExists(bool dropExisting = false)
        {
            using (var cmd = _database.CreateCommand())
            {
                if (dropExisting)
                {
                    cmd.CommandText = "DROP TABLE IF EXISTS connections;";
                    cmd.ExecuteNonQuery();
                }
                cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS connections("
                     +"identity STRING NOT NULL UNIQUE, "
                     +"displayname STRING NOT NULL, "
                     +"status INT NOT NULL, "
                     +"accessisrevoked INT NOT NULL, "
                     +"data BLOB , "
                     +"created INT NOT NULL, "
                     +"modified INT NOT NULL "
                     +", PRIMARY KEY (identity)"
                     +");"
                     +"CREATE INDEX IF NOT EXISTS Idx0TableConnectionsCRUD ON connections(identity);"
                     +"CREATE INDEX IF NOT EXISTS Idx1TableConnectionsCRUD ON connections(created);"
                     ;
                cmd.ExecuteNonQuery();
            }
        }

        public int Insert(ConnectionsItem item)
        {
            lock (_insertLock)
            {
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand();
                    _insertCommand.CommandText = "INSERT INTO connections (identity,displayname,status,accessisrevoked,data,created,modified) " +
                                                 "VALUES ($identity,$displayname,$status,$accessisrevoked,$data,$created,$modified)";
                    _insertParam1 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam1);
                    _insertParam1.ParameterName = "$identity";
                    _insertParam2 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam2);
                    _insertParam2.ParameterName = "$displayname";
                    _insertParam3 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam3);
                    _insertParam3.ParameterName = "$status";
                    _insertParam4 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam4);
                    _insertParam4.ParameterName = "$accessisrevoked";
                    _insertParam5 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam5);
                    _insertParam5.ParameterName = "$data";
                    _insertParam6 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam6);
                    _insertParam6.ParameterName = "$created";
                    _insertParam7 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam7);
                    _insertParam7.ParameterName = "$modified";
                    _insertCommand.Prepare();
                }
                _insertParam1.Value = item.identity;
                _insertParam2.Value = item.displayname;
                _insertParam3.Value = item.status;
                _insertParam4.Value = item.accessisrevoked;
                _insertParam5.Value = item.data;
                _insertParam6.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _insertParam7.Value = UnixTimeUtc.Now().milliseconds;
                _database.BeginTransaction();
                return _insertCommand.ExecuteNonQuery();
            } // Lock
        }

        public int Upsert(ConnectionsItem item)
        {
            lock (_upsertLock)
            {
                if (_upsertCommand == null)
                {
                    _upsertCommand = _database.CreateCommand();
                    _upsertCommand.CommandText = "INSERT INTO connections (identity,displayname,status,accessisrevoked,data,created,modified) " +
                                                 "VALUES ($identity,$displayname,$status,$accessisrevoked,$data,$created,$modified)"+
                                                 "ON CONFLICT (identity) DO UPDATE "+
                                                 "SET displayname = $displayname,status = $status,accessisrevoked = $accessisrevoked,data = $data,modified = $modified;";
                    _upsertParam1 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam1);
                    _upsertParam1.ParameterName = "$identity";
                    _upsertParam2 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam2);
                    _upsertParam2.ParameterName = "$displayname";
                    _upsertParam3 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam3);
                    _upsertParam3.ParameterName = "$status";
                    _upsertParam4 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam4);
                    _upsertParam4.ParameterName = "$accessisrevoked";
                    _upsertParam5 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam5);
                    _upsertParam5.ParameterName = "$data";
                    _upsertParam6 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam6);
                    _upsertParam6.ParameterName = "$created";
                    _upsertParam7 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam7);
                    _upsertParam7.ParameterName = "$modified";
                    _upsertCommand.Prepare();
                }
                _upsertParam1.Value = item.identity;
                _upsertParam2.Value = item.displayname;
                _upsertParam3.Value = item.status;
                _upsertParam4.Value = item.accessisrevoked;
                _upsertParam5.Value = item.data;
                _upsertParam6.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _upsertParam7.Value = UnixTimeUtc.Now().milliseconds;
                _database.BeginTransaction();
                return _upsertCommand.ExecuteNonQuery();
            } // Lock
        }

        public int Update(ConnectionsItem item)
        {
            lock (_updateLock)
            {
                if (_updateCommand == null)
                {
                    _updateCommand = _database.CreateCommand();
                    _updateCommand.CommandText = "UPDATE connections (identity,displayname,status,accessisrevoked,data,created,modified) " +
                                                 "VALUES ($displayname,$status,$accessisrevoked,$data,$modified)"+
                                                 "WHERE (identity = $identity)";
                    _updateParam1 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam1);
                    _updateParam1.ParameterName = "$identity";
                    _updateParam2 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam2);
                    _updateParam2.ParameterName = "$displayname";
                    _updateParam3 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam3);
                    _updateParam3.ParameterName = "$status";
                    _updateParam4 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam4);
                    _updateParam4.ParameterName = "$accessisrevoked";
                    _updateParam5 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam5);
                    _updateParam5.ParameterName = "$data";
                    _updateParam6 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam6);
                    _updateParam6.ParameterName = "$created";
                    _updateParam7 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam7);
                    _updateParam7.ParameterName = "$modified";
                    _updateCommand.Prepare();
                }
                _updateParam1.Value = item.identity;
                _updateParam2.Value = item.displayname;
                _updateParam3.Value = item.status;
                _updateParam4.Value = item.accessisrevoked;
                _updateParam5.Value = item.data;
                _updateParam6.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _updateParam7.Value = UnixTimeUtc.Now().milliseconds;
                _database.BeginTransaction();
                return _updateCommand.ExecuteNonQuery();
            } // Lock
        }

        public int Delete(string identity)
        {
            lock (_deleteLock)
            {
                if (_deleteCommand == null)
                {
                    _deleteCommand = _database.CreateCommand();
                    _deleteCommand.CommandText = "DELETE FROM connections " +
                                                 "WHERE identity = $identity";
                    _deleteParam1 = _deleteCommand.CreateParameter();
                    _deleteCommand.Parameters.Add(_deleteParam1);
                    _deleteParam1.ParameterName = "$identity";
                    _deleteCommand.Prepare();
                }
                _deleteParam1.Value = identity;
                _database.BeginTransaction();
                return _deleteCommand.ExecuteNonQuery();
            } // Lock
        }

        public ConnectionsItem Get(string identity)
        {
            lock (_getLock)
            {
                if (_getCommand == null)
                {
                    _getCommand = _database.CreateCommand();
                    _getCommand.CommandText = "SELECT displayname,status,accessisrevoked,data,created,modified FROM connections " +
                                                 "WHERE identity = $identity;";
                    _getParam1 = _getCommand.CreateParameter();
                    _getCommand.Parameters.Add(_getParam1);
                    _getParam1.ParameterName = "$identity";
                    _getCommand.Prepare();
                }
                _getParam1.Value = identity;
                using (SQLiteDataReader rdr = _getCommand.ExecuteReader(System.Data.CommandBehavior.SingleRow))
                {
                    if (!rdr.Read())
                        return null;
                    var item = new ConnectionsItem();
                    item.identity = identity;
                    byte[] _tmpbuf = new byte[65535+1];
                    long bytesRead;
                    var _guid = new byte[16];

                    if (rdr.IsDBNull(0))
                        throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                    else
                    {
                        item.displayname = rdr.GetString(0);
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
                        item.accessisrevoked = rdr.GetInt32(2);
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
                        if (bytesRead > 0)
                        {
                            item.data = new byte[bytesRead];
                            Buffer.BlockCopy(_tmpbuf, 0, item.data, 0, (int) bytesRead);
                        }
                    }

                    if (rdr.IsDBNull(4))
                        throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                    else
                    {
                        item.created = new UnixTimeUtcUnique((UInt64) rdr.GetInt64(4));
                    }

                    if (rdr.IsDBNull(5))
                        throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                    else
                    {
                        item.modified = new UnixTimeUtc((UInt64) rdr.GetInt64(5));
                    }

                    return item;
                } // using
            } // lock
        }

        public List<ConnectionsItem> PagingByIdentity(int count, string? inCursor, out string? nextCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");
            if (inCursor == null)
                inCursor = "";

            lock (_getPaging0Lock)
            {
                if (_getPaging0Command == null)
                {
                    _getPaging0Command = _database.CreateCommand();
                    _getPaging0Command.CommandText = "SELECT identity,displayname,status,accessisrevoked,data,created,modified FROM connections " +
                                                 "WHERE identity > $identity ORDER BY identity ASC LIMIT $_count;";
                    _getPaging0Param1 = _getPaging0Command.CreateParameter();
                    _getPaging0Command.Parameters.Add(_getPaging0Param1);
                    _getPaging0Param1.ParameterName = "$identity";
                    _getPaging0Param2 = _getPaging0Command.CreateParameter();
                    _getPaging0Command.Parameters.Add(_getPaging0Param2);
                    _getPaging0Param2.ParameterName = "$_count";
                    _getPaging0Command.Prepare();
                }
                _getPaging0Param1.Value = inCursor;
                _getPaging0Param2.Value = count+1;

                using (SQLiteDataReader rdr = _getPaging0Command.ExecuteReader(System.Data.CommandBehavior.Default))
                {
                    var result = new List<ConnectionsItem>();
                    int n = 0;
                    while (rdr.Read() && (n < count))
                    {
                        n++;
                        var item = new ConnectionsItem();
                        byte[] _tmpbuf = new byte[65535+1];
                        long bytesRead;
                        var _guid = new byte[16];

                        if (rdr.IsDBNull(0))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            item.identity = rdr.GetString(0);
                        }

                        if (rdr.IsDBNull(1))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            item.displayname = rdr.GetString(1);
                        }

                        if (rdr.IsDBNull(2))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            item.status = rdr.GetInt32(2);
                        }

                        if (rdr.IsDBNull(3))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            item.accessisrevoked = rdr.GetInt32(3);
                        }

                        if (rdr.IsDBNull(4))
                            item.data = null;
                        else
                        {
                            bytesRead = rdr.GetBytes(4, 0, _tmpbuf, 0, 65535+1);
                            if (bytesRead > 65535)
                                throw new Exception("Too much data in data...");
                            if (bytesRead < 0)
                                throw new Exception("Too little data in data...");
                            if (bytesRead > 0)
                            {
                                item.data = new byte[bytesRead];
                                Buffer.BlockCopy(_tmpbuf, 0, item.data, 0, (int) bytesRead);
                            }
                        }

                        if (rdr.IsDBNull(5))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            item.created = new UnixTimeUtcUnique((UInt64) rdr.GetInt64(5));
                        }

                        if (rdr.IsDBNull(6))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            item.modified = new UnixTimeUtc((UInt64) rdr.GetInt64(6));
                        }
                        result.Add(item);
                    } // while
                    if ((n > 0) && rdr.HasRows)
                    {
                        nextCursor = result[n - 1].identity;
                    }
                    else
                    {
                        nextCursor = null;
                    }

                    return result;
                } // using
            } // lock
        } // PagingGet

        public List<ConnectionsItem> PagingByCreated(int count, UnixTimeUtcUnique? inCursor, out UnixTimeUtcUnique? nextCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");
            if (inCursor == null)
                inCursor = new UnixTimeUtcUnique(0);

            lock (_getPaging5Lock)
            {
                if (_getPaging5Command == null)
                {
                    _getPaging5Command = _database.CreateCommand();
                    _getPaging5Command.CommandText = "SELECT identity,displayname,status,accessisrevoked,data,created,modified FROM connections " +
                                                 "WHERE created > $created ORDER BY created DESC LIMIT $_count;";
                    _getPaging5Param1 = _getPaging5Command.CreateParameter();
                    _getPaging5Command.Parameters.Add(_getPaging5Param1);
                    _getPaging5Param1.ParameterName = "$created";
                    _getPaging5Param2 = _getPaging5Command.CreateParameter();
                    _getPaging5Command.Parameters.Add(_getPaging5Param2);
                    _getPaging5Param2.ParameterName = "$_count";
                    _getPaging5Command.Prepare();
                }
                _getPaging5Param1.Value = inCursor?.uniqueTime;
                _getPaging5Param2.Value = count+1;

                using (SQLiteDataReader rdr = _getPaging5Command.ExecuteReader(System.Data.CommandBehavior.Default))
                {
                    var result = new List<ConnectionsItem>();
                    int n = 0;
                    while (rdr.Read() && (n < count))
                    {
                        n++;
                        var item = new ConnectionsItem();
                        byte[] _tmpbuf = new byte[65535+1];
                        long bytesRead;
                        var _guid = new byte[16];

                        if (rdr.IsDBNull(0))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            item.identity = rdr.GetString(0);
                        }

                        if (rdr.IsDBNull(1))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            item.displayname = rdr.GetString(1);
                        }

                        if (rdr.IsDBNull(2))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            item.status = rdr.GetInt32(2);
                        }

                        if (rdr.IsDBNull(3))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            item.accessisrevoked = rdr.GetInt32(3);
                        }

                        if (rdr.IsDBNull(4))
                            item.data = null;
                        else
                        {
                            bytesRead = rdr.GetBytes(4, 0, _tmpbuf, 0, 65535+1);
                            if (bytesRead > 65535)
                                throw new Exception("Too much data in data...");
                            if (bytesRead < 0)
                                throw new Exception("Too little data in data...");
                            if (bytesRead > 0)
                            {
                                item.data = new byte[bytesRead];
                                Buffer.BlockCopy(_tmpbuf, 0, item.data, 0, (int) bytesRead);
                            }
                        }

                        if (rdr.IsDBNull(5))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            item.created = new UnixTimeUtcUnique((UInt64) rdr.GetInt64(5));
                        }

                        if (rdr.IsDBNull(6))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            item.modified = new UnixTimeUtc((UInt64) rdr.GetInt64(6));
                        }
                        result.Add(item);
                    } // while
                    if ((n > 0) && rdr.HasRows)
                    {
                        nextCursor = result[n - 1].created;
                    }
                    else
                    {
                        nextCursor = null;
                    }

                    return result;
                } // using
            } // lock
        } // PagingGet

    }
}
