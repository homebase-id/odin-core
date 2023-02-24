using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace Youverse.Core.Storage.SQLite.DriveDatabase
{
    public class ReactionsItem
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
                  if (value != null) if (value.Length > 256) throw new Exception("Too long");
                  _identity = value;
               }
        }
        private Guid _postid;
        public Guid postid
        {
           get {
                   return _postid;
               }
           set {
                  if (value == null) throw new Exception("Cannot be null");
                  _postid = value;
               }
        }
        private string _singlereaction;
        public string singlereaction
        {
           get {
                   return _singlereaction;
               }
           set {
                  if (value == null) throw new Exception("Cannot be null");
                  if (value != null) if (value.Length < 3) throw new Exception("Too short");
                  if (value != null) if (value.Length > 80) throw new Exception("Too long");
                  _singlereaction = value;
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
    } // End of class ReactionsItem

    public class TableReactionsCRUD : TableBase
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
        private SQLiteCommand _updateCommand = null;
        private static Object _updateLock = new Object();
        private SQLiteParameter _updateParam1 = null;
        private SQLiteParameter _updateParam2 = null;
        private SQLiteParameter _updateParam3 = null;
        private SQLiteParameter _updateParam4 = null;
        private SQLiteParameter _updateParam5 = null;
        private SQLiteParameter _updateParam6 = null;
        private SQLiteCommand _upsertCommand = null;
        private static Object _upsertLock = new Object();
        private SQLiteParameter _upsertParam1 = null;
        private SQLiteParameter _upsertParam2 = null;
        private SQLiteParameter _upsertParam3 = null;
        private SQLiteParameter _upsertParam4 = null;
        private SQLiteParameter _upsertParam5 = null;
        private SQLiteParameter _upsertParam6 = null;
        private SQLiteCommand _deleteCommand = null;
        private static Object _deleteLock = new Object();
        private SQLiteParameter _deleteParam1 = null;
        private SQLiteParameter _deleteParam2 = null;
        private SQLiteParameter _deleteParam3 = null;
        private SQLiteCommand _getCommand = null;
        private static Object _getLock = new Object();
        private SQLiteParameter _getParam1 = null;
        private SQLiteParameter _getParam2 = null;
        private SQLiteParameter _getParam3 = null;

        public TableReactionsCRUD(DriveDatabase db) : base(db)
        {
        }

        ~TableReactionsCRUD()
        {
            if (_disposed == false) throw new Exception("TableReactionsCRUD Not disposed properly");
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
            _disposed = true;
        }

        public sealed override void EnsureTableExists(bool dropExisting = false)
        {
            using (var cmd = _database.CreateCommand())
            {
                if (dropExisting)
                {
                    cmd.CommandText = "DROP TABLE IF EXISTS reactions;";
                    cmd.ExecuteNonQuery();
                }
                cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS reactions("
                     +"identity STRING NOT NULL, "
                     +"postid BLOB NOT NULL, "
                     +"singlereaction STRING NOT NULL, "
                     +"created INT NOT NULL, "
                     +"modified INT NOT NULL "
                     +", PRIMARY KEY (identity,postid,singlereaction)"
                     +");"
                     ;
                cmd.ExecuteNonQuery();
            }
        }

        public int Insert(ReactionsItem item)
        {
            lock (_insertLock)
            {
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand();
                    _insertCommand.CommandText = "INSERT INTO reactions (identity,postid,singlereaction,created,modified) " +
                                                 "VALUES ($identity,$postid,$singlereaction,$created,$modified)";
                    _insertParam1 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam1);
                    _insertParam1.ParameterName = "$identity";
                    _insertParam2 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam2);
                    _insertParam2.ParameterName = "$postid";
                    _insertParam3 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam3);
                    _insertParam3.ParameterName = "$singlereaction";
                    _insertParam4 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam4);
                    _insertParam4.ParameterName = "$created";
                    _insertParam5 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam5);
                    _insertParam5.ParameterName = "$modified";
                    _insertCommand.Prepare();
                }
                _insertParam1.Value = item.identity;
                _insertParam2.Value = item.postid;
                _insertParam3.Value = item.singlereaction;
                _insertParam4.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _insertParam5.Value = UnixTimeUtc.Now().milliseconds;
                _database.BeginTransaction();
                return _insertCommand.ExecuteNonQuery();
            } // Lock
        }

        public int Upsert(ReactionsItem item)
        {
            lock (_upsertLock)
            {
                if (_upsertCommand == null)
                {
                    _upsertCommand = _database.CreateCommand();
                    _upsertCommand.CommandText = "INSERT INTO reactions (identity,postid,singlereaction,created,modified) " +
                                                 "VALUES ($identity,$postid,$singlereaction,$created,$modified)"+
                                                 "ON CONFLICT (identity,postid,singlereaction) DO UPDATE "+
                                                 "SET modified = $modified;";
                    _upsertParam1 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam1);
                    _upsertParam1.ParameterName = "$identity";
                    _upsertParam2 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam2);
                    _upsertParam2.ParameterName = "$postid";
                    _upsertParam3 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam3);
                    _upsertParam3.ParameterName = "$singlereaction";
                    _upsertParam4 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam4);
                    _upsertParam4.ParameterName = "$created";
                    _upsertParam5 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam5);
                    _upsertParam5.ParameterName = "$modified";
                    _upsertCommand.Prepare();
                }
                _upsertParam1.Value = item.identity;
                _upsertParam2.Value = item.postid;
                _upsertParam3.Value = item.singlereaction;
                _upsertParam4.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _upsertParam5.Value = UnixTimeUtc.Now().milliseconds;
                _database.BeginTransaction();
                return _upsertCommand.ExecuteNonQuery();
            } // Lock
        }

        public int Update(ReactionsItem item)
        {
            lock (_updateLock)
            {
                if (_updateCommand == null)
                {
                    _updateCommand = _database.CreateCommand();
                    _updateCommand.CommandText = "UPDATE reactions (identity,postid,singlereaction,created,modified) " +
                                                 "VALUES ($modified)"+
                                                 "WHERE (identity = $identity,postid = $postid,singlereaction = $singlereaction)";
                    _updateParam1 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam1);
                    _updateParam1.ParameterName = "$identity";
                    _updateParam2 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam2);
                    _updateParam2.ParameterName = "$postid";
                    _updateParam3 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam3);
                    _updateParam3.ParameterName = "$singlereaction";
                    _updateParam4 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam4);
                    _updateParam4.ParameterName = "$created";
                    _updateParam5 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam5);
                    _updateParam5.ParameterName = "$modified";
                    _updateCommand.Prepare();
                }
                _updateParam1.Value = item.identity;
                _updateParam2.Value = item.postid;
                _updateParam3.Value = item.singlereaction;
                _updateParam4.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _updateParam5.Value = UnixTimeUtc.Now().milliseconds;
                _database.BeginTransaction();
                return _updateCommand.ExecuteNonQuery();
            } // Lock
        }

        public int Delete(string identity,Guid postid,string singlereaction)
        {
            lock (_deleteLock)
            {
                if (_deleteCommand == null)
                {
                    _deleteCommand = _database.CreateCommand();
                    _deleteCommand.CommandText = "DELETE FROM reactions " +
                                                 "WHERE identity = $identity AND postid = $postid AND singlereaction = $singlereaction";
                    _deleteParam1 = _deleteCommand.CreateParameter();
                    _deleteCommand.Parameters.Add(_deleteParam1);
                    _deleteParam1.ParameterName = "$identity";
                    _deleteParam2 = _deleteCommand.CreateParameter();
                    _deleteCommand.Parameters.Add(_deleteParam2);
                    _deleteParam2.ParameterName = "$postid";
                    _deleteParam3 = _deleteCommand.CreateParameter();
                    _deleteCommand.Parameters.Add(_deleteParam3);
                    _deleteParam3.ParameterName = "$singlereaction";
                    _deleteCommand.Prepare();
                }
                _deleteParam1.Value = identity;
                _deleteParam2.Value = postid;
                _deleteParam3.Value = singlereaction;
                _database.BeginTransaction();
                return _deleteCommand.ExecuteNonQuery();
            } // Lock
        }

        public ReactionsItem Get(string identity,Guid postid,string singlereaction)
        {
            lock (_getLock)
            {
                if (_getCommand == null)
                {
                    _getCommand = _database.CreateCommand();
                    _getCommand.CommandText = "SELECT created,modified FROM reactions " +
                                                 "WHERE identity = $identity AND postid = $postid AND singlereaction = $singlereaction;";
                    _getParam1 = _getCommand.CreateParameter();
                    _getCommand.Parameters.Add(_getParam1);
                    _getParam1.ParameterName = "$identity";
                    _getParam2 = _getCommand.CreateParameter();
                    _getCommand.Parameters.Add(_getParam2);
                    _getParam2.ParameterName = "$postid";
                    _getParam3 = _getCommand.CreateParameter();
                    _getCommand.Parameters.Add(_getParam3);
                    _getParam3.ParameterName = "$singlereaction";
                    _getCommand.Prepare();
                }
                _getParam1.Value = identity;
                _getParam2.Value = postid;
                _getParam3.Value = singlereaction;
                using (SQLiteDataReader rdr = _getCommand.ExecuteReader(System.Data.CommandBehavior.SingleRow))
                {
                    if (!rdr.Read())
                        return null;
                    var item = new ReactionsItem();
                    item.identity = identity;
                    item.postid = postid;
                    item.singlereaction = singlereaction;
                    byte[] _tmpbuf = new byte[65535+1];
                    long bytesRead;
                    var _guid = new byte[16];

                    if (rdr.IsDBNull(0))
                        throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                    else
                    {
                        item.created = new UnixTimeUtcUnique((UInt64) rdr.GetInt64(0));
                    }

                    if (rdr.IsDBNull(1))
                        throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                    else
                    {
                        item.modified = new UnixTimeUtc((UInt64) rdr.GetInt64(1));
                    }

                    return item;
                } // using
            } // lock
        }

    }
}
