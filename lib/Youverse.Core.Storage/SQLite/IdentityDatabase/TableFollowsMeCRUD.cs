using System;
using System.Collections.Generic;
using System.Data.SQLite;
using Youverse.Core.Identity;

namespace Youverse.Core.Storage.SQLite.IdentityDatabase
{
    public class FollowsMeItem
    {
        private string _identity;
        public string identity
        {
           get {
                   return _identity;
               }
           set {
                  if (value == null) throw new Exception("Cannot be null");
                  if (value?.Length < 3) throw new Exception("Too short");
                  if (value?.Length > 255) throw new Exception("Too long");
                  _identity = value;
               }
        }
        private Guid _driveId;
        public Guid driveId
        {
           get {
                   return _driveId;
               }
           set {
                  _driveId = value;
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
    } // End of class FollowsMeItem

    public class TableFollowsMeCRUD : TableBase
    {
        private bool _disposed = false;
        private SQLiteCommand _insertCommand = null;
        private static Object _insertLock = new Object();
        private SQLiteParameter _insertParam1 = null;
        private SQLiteParameter _insertParam2 = null;
        private SQLiteParameter _insertParam3 = null;
        private SQLiteParameter _insertParam4 = null;
        private SQLiteCommand _updateCommand = null;
        private static Object _updateLock = new Object();
        private SQLiteParameter _updateParam1 = null;
        private SQLiteParameter _updateParam2 = null;
        private SQLiteParameter _updateParam3 = null;
        private SQLiteParameter _updateParam4 = null;
        private SQLiteCommand _upsertCommand = null;
        private static Object _upsertLock = new Object();
        private SQLiteParameter _upsertParam1 = null;
        private SQLiteParameter _upsertParam2 = null;
        private SQLiteParameter _upsertParam3 = null;
        private SQLiteParameter _upsertParam4 = null;
        private SQLiteCommand _deleteCommand = null;
        private static Object _deleteLock = new Object();
        private SQLiteParameter _deleteParam1 = null;
        private SQLiteParameter _deleteParam2 = null;
        private SQLiteCommand _get0Command = null;
        private static Object _get0Lock = new Object();
        private SQLiteParameter _get0Param1 = null;
        private SQLiteParameter _get0Param2 = null;
        private SQLiteCommand _get1Command = null;
        private static Object _get1Lock = new Object();
        private SQLiteParameter _get1Param1 = null;

        public TableFollowsMeCRUD(IdentityDatabase db) : base(db)
        {
        }

        ~TableFollowsMeCRUD()
        {
            if (_disposed == false) throw new Exception("TableFollowsMeCRUD Not disposed properly");
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
                    cmd.CommandText = "DROP TABLE IF EXISTS followsMe;";
                    cmd.ExecuteNonQuery();
                }
                cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS followsMe("
                     +"identity STRING NOT NULL, "
                     +"driveId BLOB NOT NULL, "
                     +"created INT NOT NULL, "
                     +"modified INT  "
                     +", PRIMARY KEY (identity,driveId)"
                     +");"
                     +"CREATE INDEX IF NOT EXISTS Idx0TableFollowsMeCRUD ON followsMe(identity);"
                     ;
                cmd.ExecuteNonQuery();
            }
        }

        public virtual int Insert(FollowsMeItem item)
        {
            lock (_insertLock)
            {
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand();
                    _insertCommand.CommandText = "INSERT INTO followsMe (identity,driveId,created,modified) " +
                                                 "VALUES ($identity,$driveId,$created,$modified)";
                    _insertParam1 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam1);
                    _insertParam1.ParameterName = "$identity";
                    _insertParam2 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam2);
                    _insertParam2.ParameterName = "$driveId";
                    _insertParam3 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam3);
                    _insertParam3.ParameterName = "$created";
                    _insertParam4 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam4);
                    _insertParam4.ParameterName = "$modified";
                    _insertCommand.Prepare();
                }
                _insertParam1.Value = item.identity;
                _insertParam2.Value = item.driveId;
                _insertParam3.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _insertParam4.Value = null;
                _database.BeginTransaction();
                return _insertCommand.ExecuteNonQuery();
            } // Lock
        }

        public virtual int Upsert(FollowsMeItem item)
        {
            lock (_upsertLock)
            {
                if (_upsertCommand == null)
                {
                    _upsertCommand = _database.CreateCommand();
                    _upsertCommand.CommandText = "INSERT INTO followsMe (identity,driveId,created,modified) " +
                                                 "VALUES ($identity,$driveId,$created,$modified)"+
                                                 "ON CONFLICT (identity,driveId) DO UPDATE "+
                                                 "SET modified = $modified;";
                    _upsertParam1 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam1);
                    _upsertParam1.ParameterName = "$identity";
                    _upsertParam2 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam2);
                    _upsertParam2.ParameterName = "$driveId";
                    _upsertParam3 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam3);
                    _upsertParam3.ParameterName = "$created";
                    _upsertParam4 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam4);
                    _upsertParam4.ParameterName = "$modified";
                    _upsertCommand.Prepare();
                }
                _upsertParam1.Value = item.identity;
                _upsertParam2.Value = item.driveId;
                _upsertParam3.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _upsertParam4.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _database.BeginTransaction();
                return _upsertCommand.ExecuteNonQuery();
            } // Lock
        }

        public virtual int Update(FollowsMeItem item)
        {
            lock (_updateLock)
            {
                if (_updateCommand == null)
                {
                    _updateCommand = _database.CreateCommand();
                    _updateCommand.CommandText = "UPDATE followsMe " +
                                                 "SET modified = $modified "+
                                                 "WHERE (identity = $identity,driveId = $driveId)";
                    _updateParam1 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam1);
                    _updateParam1.ParameterName = "$identity";
                    _updateParam2 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam2);
                    _updateParam2.ParameterName = "$driveId";
                    _updateParam3 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam3);
                    _updateParam3.ParameterName = "$created";
                    _updateParam4 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam4);
                    _updateParam4.ParameterName = "$modified";
                    _updateCommand.Prepare();
                }
                _updateParam1.Value = item.identity;
                _updateParam2.Value = item.driveId;
                _updateParam3.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _updateParam4.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _database.BeginTransaction();
                return _updateCommand.ExecuteNonQuery();
            } // Lock
        }

        public int Delete(string identity,Guid driveId)
        {
            lock (_deleteLock)
            {
                if (_deleteCommand == null)
                {
                    _deleteCommand = _database.CreateCommand();
                    _deleteCommand.CommandText = "DELETE FROM followsMe " +
                                                 "WHERE identity = $identity AND driveId = $driveId";
                    _deleteParam1 = _deleteCommand.CreateParameter();
                    _deleteCommand.Parameters.Add(_deleteParam1);
                    _deleteParam1.ParameterName = "$identity";
                    _deleteParam2 = _deleteCommand.CreateParameter();
                    _deleteCommand.Parameters.Add(_deleteParam2);
                    _deleteParam2.ParameterName = "$driveId";
                    _deleteCommand.Prepare();
                }
                _deleteParam1.Value = identity;
                _deleteParam2.Value = driveId;
                _database.BeginTransaction();
                return _deleteCommand.ExecuteNonQuery();
            } // Lock
        }

        public FollowsMeItem Get(string identity,Guid driveId)
        {
            lock (_get0Lock)
            {
                if (_get0Command == null)
                {
                    _get0Command = _database.CreateCommand();
                    _get0Command.CommandText = "SELECT created,modified FROM followsMe " +
                                                 "WHERE identity = $identity AND driveId = $driveId LIMIT 1;";
                    _get0Param1 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param1);
                    _get0Param1.ParameterName = "$identity";
                    _get0Param2 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param2);
                    _get0Param2.ParameterName = "$driveId";
                    _get0Command.Prepare();
                }
                _get0Param1.Value = identity;
                _get0Param2.Value = driveId;
                using (SQLiteDataReader rdr = _get0Command.ExecuteReader(System.Data.CommandBehavior.SingleRow))
                {
                    var result = new FollowsMeItem();
                    if (!rdr.Read())
                        return null;
                    byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
                    long bytesRead;
#pragma warning restore CS0168
                    var _guid = new byte[16];
                        var item = new FollowsMeItem();
                        item.identity = identity;
                        item.driveId = driveId;

                        if (rdr.IsDBNull(0))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            item.created = new UnixTimeUtcUnique((UInt64) rdr.GetInt64(0));
                        }

                        if (rdr.IsDBNull(1))
                            item.modified = null;
                        else
                        {
                            item.modified = new UnixTimeUtcUnique((UInt64) rdr.GetInt64(1));
                        }
                    return item;
                } // using
            } // lock
        }

        public List<FollowsMeItem> Get(string identity)
        {
            lock (_get1Lock)
            {
                if (_get1Command == null)
                {
                    _get1Command = _database.CreateCommand();
                    _get1Command.CommandText = "SELECT driveId,created,modified FROM followsMe " +
                                                 "WHERE identity = $identity;";
                    _get1Param1 = _get1Command.CreateParameter();
                    _get1Command.Parameters.Add(_get1Param1);
                    _get1Param1.ParameterName = "$identity";
                    _get1Command.Prepare();
                }
                _get1Param1.Value = identity;
                using (SQLiteDataReader rdr = _get1Command.ExecuteReader(System.Data.CommandBehavior.Default))
                {
                    var result = new List<FollowsMeItem>();
                    if (!rdr.Read())
                        return null;
                    byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
                    long bytesRead;
#pragma warning restore CS0168
                    var _guid = new byte[16];
                    while (true)
                    {
                        var item = new FollowsMeItem();
                        item.identity = identity;

                        if (rdr.IsDBNull(0))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            bytesRead = rdr.GetBytes(0, 0, _guid, 0, 16);
                            if (bytesRead != 16)
                                throw new Exception("Not a GUID in driveId...");
                            item.driveId = new Guid(_guid);
                        }

                        if (rdr.IsDBNull(1))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            item.created = new UnixTimeUtcUnique((UInt64) rdr.GetInt64(1));
                        }

                        if (rdr.IsDBNull(2))
                            item.modified = null;
                        else
                        {
                            item.modified = new UnixTimeUtcUnique((UInt64) rdr.GetInt64(2));
                        }
                        result.Add(item);
                        if (!rdr.Read())
                           break;
                    } // while
                    return result;
                } // using
            } // lock
        }

    }
}
