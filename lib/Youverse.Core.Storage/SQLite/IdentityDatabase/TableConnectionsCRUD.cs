using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Youverse.Core.Identity;

namespace Youverse.Core.Storage.Sqlite.IdentityDatabase
{
    public class ConnectionsRecord
    {
        private OdinId _identity;
        public OdinId identity
        {
           get {
                   return _identity;
               }
           set {
                  _identity = value;
               }
        }
        private string _displayName;
        public string displayName
        {
           get {
                   return _displayName;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 0) throw new Exception("Too short");
                    if (value?.Length > 80) throw new Exception("Too long");
                  _displayName = value;
               }
        }
        private Int32 _status;
        public Int32 status
        {
           get {
                   return _status;
               }
           set {
                  _status = value;
               }
        }
        private Int32 _accessIsRevoked;
        public Int32 accessIsRevoked
        {
           get {
                   return _accessIsRevoked;
               }
           set {
                  _accessIsRevoked = value;
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
                    if (value?.Length > 65535) throw new Exception("Too long");
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
        private UnixTimeUtcUnique? _modified;
        public UnixTimeUtcUnique? modified
        {
           get {
                   return _modified;
               }
           set {
                  _modified = value;
               }
        }
    } // End of class ConnectionsRecord

    public class TableConnectionsCRUD : TableBase
    {
        private bool _disposed = false;
        private SqliteCommand _insertCommand = null;
        private static Object _insertLock = new Object();
        private SqliteParameter _insertParam1 = null;
        private SqliteParameter _insertParam2 = null;
        private SqliteParameter _insertParam3 = null;
        private SqliteParameter _insertParam4 = null;
        private SqliteParameter _insertParam5 = null;
        private SqliteParameter _insertParam6 = null;
        private SqliteParameter _insertParam7 = null;
        private SqliteCommand _updateCommand = null;
        private static Object _updateLock = new Object();
        private SqliteParameter _updateParam1 = null;
        private SqliteParameter _updateParam2 = null;
        private SqliteParameter _updateParam3 = null;
        private SqliteParameter _updateParam4 = null;
        private SqliteParameter _updateParam5 = null;
        private SqliteParameter _updateParam6 = null;
        private SqliteParameter _updateParam7 = null;
        private SqliteCommand _upsertCommand = null;
        private static Object _upsertLock = new Object();
        private SqliteParameter _upsertParam1 = null;
        private SqliteParameter _upsertParam2 = null;
        private SqliteParameter _upsertParam3 = null;
        private SqliteParameter _upsertParam4 = null;
        private SqliteParameter _upsertParam5 = null;
        private SqliteParameter _upsertParam6 = null;
        private SqliteParameter _upsertParam7 = null;
        private SqliteCommand _delete0Command = null;
        private static Object _delete0Lock = new Object();
        private SqliteParameter _delete0Param1 = null;
        private SqliteCommand _get0Command = null;
        private static Object _get0Lock = new Object();
        private SqliteParameter _get0Param1 = null;
        private SqliteCommand _getPaging1Command = null;
        private static Object _getPaging1Lock = new Object();
        private SqliteParameter _getPaging1Param1 = null;
        private SqliteParameter _getPaging1Param2 = null;
        private SqliteCommand _getPaging6Command = null;
        private static Object _getPaging6Lock = new Object();
        private SqliteParameter _getPaging6Param1 = null;
        private SqliteParameter _getPaging6Param2 = null;

        public TableConnectionsCRUD(IdentityDatabase db) : base(db)
        {
        }

        ~TableConnectionsCRUD()
        {
            if (_disposed == false) throw new Exception("TableConnectionsCRUD Not disposed properly");
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
            _getPaging1Command?.Dispose();
            _getPaging1Command = null;
            _getPaging6Command?.Dispose();
            _getPaging6Command = null;
            _disposed = true;
        }

        public sealed override void EnsureTableExists(bool dropExisting = false)
        {
            using (var cmd = _database.CreateCommand())
            {
                if (dropExisting)
                {
                    cmd.CommandText = "DROP TABLE IF EXISTS connections;";
                    cmd.ExecuteNonQuery(_database);
                }
                cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS connections("
                     +"identity BLOB NOT NULL UNIQUE, "
                     +"displayName STRING NOT NULL, "
                     +"status INT NOT NULL, "
                     +"accessIsRevoked INT NOT NULL, "
                     +"data BLOB , "
                     +"created INT NOT NULL, "
                     +"modified INT  "
                     +", PRIMARY KEY (identity)"
                     +");"
                     +"CREATE INDEX IF NOT EXISTS Idx0TableConnectionsCRUD ON connections(identity);"
                     +"CREATE INDEX IF NOT EXISTS Idx1TableConnectionsCRUD ON connections(created);"
                     ;
                cmd.ExecuteNonQuery(_database);
            }
        }

        public virtual int Insert(ConnectionsRecord item)
        {
            lock (_insertLock)
            {
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand();
                    _insertCommand.CommandText = "INSERT INTO connections (identity,displayName,status,accessIsRevoked,data,created,modified) " +
                                                 "VALUES ($identity,$displayName,$status,$accessIsRevoked,$data,$created,$modified)";
                    _insertParam1 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam1);
                    _insertParam1.ParameterName = "$identity";
                    _insertParam2 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam2);
                    _insertParam2.ParameterName = "$displayName";
                    _insertParam3 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam3);
                    _insertParam3.ParameterName = "$status";
                    _insertParam4 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam4);
                    _insertParam4.ParameterName = "$accessIsRevoked";
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
                _insertParam1.Value = item.identity.DomainName;
                _insertParam2.Value = item.displayName;
                _insertParam3.Value = item.status;
                _insertParam4.Value = item.accessIsRevoked;
                _insertParam5.Value = item.data ?? (object)DBNull.Value;
                _insertParam6.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _insertParam7.Value = DBNull.Value;
                _database.BeginTransaction();
                return _insertCommand.ExecuteNonQuery(_database);
            } // Lock
        }

        public virtual int Upsert(ConnectionsRecord item)
        {
            lock (_upsertLock)
            {
                if (_upsertCommand == null)
                {
                    _upsertCommand = _database.CreateCommand();
                    _upsertCommand.CommandText = "INSERT INTO connections (identity,displayName,status,accessIsRevoked,data,created,modified) " +
                                                 "VALUES ($identity,$displayName,$status,$accessIsRevoked,$data,$created,$modified)"+
                                                 "ON CONFLICT (identity) DO UPDATE "+
                                                 "SET displayName = $displayName,status = $status,accessIsRevoked = $accessIsRevoked,data = $data,modified = $modified;";
                    _upsertParam1 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam1);
                    _upsertParam1.ParameterName = "$identity";
                    _upsertParam2 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam2);
                    _upsertParam2.ParameterName = "$displayName";
                    _upsertParam3 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam3);
                    _upsertParam3.ParameterName = "$status";
                    _upsertParam4 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam4);
                    _upsertParam4.ParameterName = "$accessIsRevoked";
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
                _upsertParam1.Value = item.identity.DomainName;
                _upsertParam2.Value = item.displayName;
                _upsertParam3.Value = item.status;
                _upsertParam4.Value = item.accessIsRevoked;
                _upsertParam5.Value = item.data ?? (object)DBNull.Value;
                _upsertParam6.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _upsertParam7.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _database.BeginTransaction();
                return _upsertCommand.ExecuteNonQuery(_database);
            } // Lock
        }

        public virtual int Update(ConnectionsRecord item)
        {
            lock (_updateLock)
            {
                if (_updateCommand == null)
                {
                    _updateCommand = _database.CreateCommand();
                    _updateCommand.CommandText = "UPDATE connections " +
                                                 "SET displayName = $displayName,status = $status,accessIsRevoked = $accessIsRevoked,data = $data,modified = $modified "+
                                                 "WHERE (identity = $identity)";
                    _updateParam1 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam1);
                    _updateParam1.ParameterName = "$identity";
                    _updateParam2 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam2);
                    _updateParam2.ParameterName = "$displayName";
                    _updateParam3 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam3);
                    _updateParam3.ParameterName = "$status";
                    _updateParam4 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam4);
                    _updateParam4.ParameterName = "$accessIsRevoked";
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
                _updateParam1.Value = item.identity.DomainName;
                _updateParam2.Value = item.displayName;
                _updateParam3.Value = item.status;
                _updateParam4.Value = item.accessIsRevoked;
                _updateParam5.Value = item.data ?? (object)DBNull.Value;
                _updateParam6.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _updateParam7.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _database.BeginTransaction();
                return _updateCommand.ExecuteNonQuery(_database);
            } // Lock
        }

        public int Delete(OdinId identity)
        {
            lock (_delete0Lock)
            {
                if (_delete0Command == null)
                {
                    _delete0Command = _database.CreateCommand();
                    _delete0Command.CommandText = "DELETE FROM connections " +
                                                 "WHERE identity = $identity";
                    _delete0Param1 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param1);
                    _delete0Param1.ParameterName = "$identity";
                    _delete0Command.Prepare();
                }
                _delete0Param1.Value = identity.DomainName;
                _database.BeginTransaction();
                return _delete0Command.ExecuteNonQuery(_database);
            } // Lock
        }

        public ConnectionsRecord Get(OdinId identity)
        {
            lock (_get0Lock)
            {
                if (_get0Command == null)
                {
                    _get0Command = _database.CreateCommand();
                    _get0Command.CommandText = "SELECT displayName,status,accessIsRevoked,data,created,modified FROM connections " +
                                                 "WHERE identity = $identity LIMIT 1;";
                    _get0Param1 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param1);
                    _get0Param1.ParameterName = "$identity";
                    _get0Command.Prepare();
                }
                _get0Param1.Value = identity.DomainName;
                using (SqliteDataReader rdr = _get0Command.ExecuteReader(System.Data.CommandBehavior.SingleRow, _database))
                {
                    var result = new ConnectionsRecord();
                    if (!rdr.Read())
                        return null;
                    byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
                    long bytesRead;
#pragma warning restore CS0168
                    var _guid = new byte[16];
                        var item = new ConnectionsRecord();
                        item.identity = identity;

                        if (rdr.IsDBNull(0))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            item.displayName = rdr.GetString(0);
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
                            item.accessIsRevoked = rdr.GetInt32(2);
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
                            item.data = new byte[bytesRead];
                            Buffer.BlockCopy(_tmpbuf, 0, item.data, 0, (int) bytesRead);
                        }

                        if (rdr.IsDBNull(4))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            item.created = new UnixTimeUtcUnique(rdr.GetInt64(4));
                        }

                        if (rdr.IsDBNull(5))
                            item.modified = null;
                        else
                        {
                            item.modified = new UnixTimeUtcUnique(rdr.GetInt64(5));
                        }
                    return item;
                } // using
            } // lock
        }

        public List<ConnectionsRecord> PagingByIdentity(int count, string inCursor, out string nextCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");
            if (inCursor == null)
                inCursor = "";

            lock (_getPaging1Lock)
            {
                if (_getPaging1Command == null)
                {
                    _getPaging1Command = _database.CreateCommand();
                    _getPaging1Command.CommandText = "SELECT rowid,identity,displayName,status,accessIsRevoked,data,created,modified FROM connections " +
                                                 "WHERE identity > $identity ORDER BY identity ASC LIMIT $_count;";
                    _getPaging1Param1 = _getPaging1Command.CreateParameter();
                    _getPaging1Command.Parameters.Add(_getPaging1Param1);
                    _getPaging1Param1.ParameterName = "$identity";
                    _getPaging1Param2 = _getPaging1Command.CreateParameter();
                    _getPaging1Command.Parameters.Add(_getPaging1Param2);
                    _getPaging1Param2.ParameterName = "$_count";
                    _getPaging1Command.Prepare();
                }
                _getPaging1Param1.Value = inCursor;
                _getPaging1Param2.Value = count+1;
                _getPaging1Command.Transaction = _database.Transaction;

                using (SqliteDataReader rdr = _getPaging1Command.ExecuteReader(System.Data.CommandBehavior.Default, _database))
                {
                    var result = new List<ConnectionsRecord>();
                    int n = 0;
                    int rowid = 0;
                    while ((n < count) && rdr.Read())
                    {
                        n++;
                        var item = new ConnectionsRecord();
                        byte[] _tmpbuf = new byte[65535+1];
                        long bytesRead;
                        var _guid = new byte[16];

                        rowid = rdr.GetInt32(0);

                        if (rdr.IsDBNull(1))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            item.identity = new OdinId(rdr.GetString(1));
                        }

                        if (rdr.IsDBNull(2))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            item.displayName = rdr.GetString(2);
                        }

                        if (rdr.IsDBNull(3))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            item.status = rdr.GetInt32(3);
                        }

                        if (rdr.IsDBNull(4))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            item.accessIsRevoked = rdr.GetInt32(4);
                        }

                        if (rdr.IsDBNull(5))
                            item.data = null;
                        else
                        {
                            bytesRead = rdr.GetBytes(5, 0, _tmpbuf, 0, 65535+1);
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

                        if (rdr.IsDBNull(6))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            item.created = new UnixTimeUtcUnique(rdr.GetInt64(6));
                        }

                        if (rdr.IsDBNull(7))
                            item.modified = null;
                        else
                        {
                            item.modified = new UnixTimeUtcUnique(rdr.GetInt64(7));
                        }
                        result.Add(item);
                    } // while
                    if ((n > 0) && rdr.Read())
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

        public List<ConnectionsRecord> PagingByCreated(int count, UnixTimeUtcUnique? inCursor, out UnixTimeUtcUnique? nextCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");
            if (inCursor == null)
                inCursor = new UnixTimeUtcUnique(0);

            lock (_getPaging6Lock)
            {
                if (_getPaging6Command == null)
                {
                    _getPaging6Command = _database.CreateCommand();
                    _getPaging6Command.CommandText = "SELECT rowid,identity,displayName,status,accessIsRevoked,data,created,modified FROM connections " +
                                                 "WHERE created > $created ORDER BY created DESC LIMIT $_count;";
                    _getPaging6Param1 = _getPaging6Command.CreateParameter();
                    _getPaging6Command.Parameters.Add(_getPaging6Param1);
                    _getPaging6Param1.ParameterName = "$created";
                    _getPaging6Param2 = _getPaging6Command.CreateParameter();
                    _getPaging6Command.Parameters.Add(_getPaging6Param2);
                    _getPaging6Param2.ParameterName = "$_count";
                    _getPaging6Command.Prepare();
                }
                _getPaging6Param1.Value = inCursor?.uniqueTime;
                _getPaging6Param2.Value = count+1;
                _getPaging6Command.Transaction = _database.Transaction;

                using (SqliteDataReader rdr = _getPaging6Command.ExecuteReader(System.Data.CommandBehavior.Default, _database))
                {
                    var result = new List<ConnectionsRecord>();
                    int n = 0;
                    int rowid = 0;
                    while ((n < count) && rdr.Read())
                    {
                        n++;
                        var item = new ConnectionsRecord();
                        byte[] _tmpbuf = new byte[65535+1];
                        long bytesRead;
                        var _guid = new byte[16];

                        rowid = rdr.GetInt32(0);

                        if (rdr.IsDBNull(1))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            item.identity = new OdinId(rdr.GetString(1));
                        }

                        if (rdr.IsDBNull(2))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            item.displayName = rdr.GetString(2);
                        }

                        if (rdr.IsDBNull(3))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            item.status = rdr.GetInt32(3);
                        }

                        if (rdr.IsDBNull(4))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            item.accessIsRevoked = rdr.GetInt32(4);
                        }

                        if (rdr.IsDBNull(5))
                            item.data = null;
                        else
                        {
                            bytesRead = rdr.GetBytes(5, 0, _tmpbuf, 0, 65535+1);
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

                        if (rdr.IsDBNull(6))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            item.created = new UnixTimeUtcUnique(rdr.GetInt64(6));
                        }

                        if (rdr.IsDBNull(7))
                            item.modified = null;
                        else
                        {
                            item.modified = new UnixTimeUtcUnique(rdr.GetInt64(7));
                        }
                        result.Add(item);
                    } // while
                    if ((n > 0) && rdr.Read())
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
