using System;
using System.Collections.Generic;
using System.Data.SQLite;
using Youverse.Core.Identity;

namespace Youverse.Core.Storage.SQLite.DriveDatabase
{
    public class ReactionsItem
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
        private Guid _postId;
        public Guid postId
        {
           get {
                   return _postId;
               }
           set {
                  _postId = value;
               }
        }
        private string _singleReaction;
        public string singleReaction
        {
           get {
                   return _singleReaction;
               }
           set {
                  if (value == null) throw new Exception("Cannot be null");
                  if (value?.Length < 3) throw new Exception("Too short");
                  if (value?.Length > 80) throw new Exception("Too long");
                  _singleReaction = value;
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
        private SQLiteCommand _updateCommand = null;
        private static Object _updateLock = new Object();
        private SQLiteParameter _updateParam1 = null;
        private SQLiteParameter _updateParam2 = null;
        private SQLiteParameter _updateParam3 = null;
        private SQLiteCommand _upsertCommand = null;
        private static Object _upsertLock = new Object();
        private SQLiteParameter _upsertParam1 = null;
        private SQLiteParameter _upsertParam2 = null;
        private SQLiteParameter _upsertParam3 = null;
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
                     +"identity BLOB NOT NULL, "
                     +"postId BLOB NOT NULL, "
                     +"singleReaction STRING NOT NULL "
                     +", PRIMARY KEY (identity,postId,singleReaction)"
                     +");"
                     ;
                cmd.ExecuteNonQuery();
            }
        }

        public virtual int Insert(ReactionsItem item)
        {
            lock (_insertLock)
            {
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand();
                    _insertCommand.CommandText = "INSERT INTO reactions (identity,postId,singleReaction) " +
                                                 "VALUES ($identity,$postId,$singleReaction)";
                    _insertParam1 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam1);
                    _insertParam1.ParameterName = "$identity";
                    _insertParam2 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam2);
                    _insertParam2.ParameterName = "$postId";
                    _insertParam3 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam3);
                    _insertParam3.ParameterName = "$singleReaction";
                    _insertCommand.Prepare();
                }
                _insertParam1.Value = item.identity;
                _insertParam2.Value = item.postId;
                _insertParam3.Value = item.singleReaction;
                _database.BeginTransaction();
                return _insertCommand.ExecuteNonQuery();
            } // Lock
        }

        public virtual int Upsert(ReactionsItem item)
        {
            lock (_upsertLock)
            {
                if (_upsertCommand == null)
                {
                    _upsertCommand = _database.CreateCommand();
                    _upsertCommand.CommandText = "INSERT INTO reactions (identity,postId,singleReaction) " +
                                                 "VALUES ($identity,$postId,$singleReaction)"+
                                                 "ON CONFLICT (identity,postId,singleReaction) DO UPDATE "+
                                                 "SET ;";
                    _upsertParam1 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam1);
                    _upsertParam1.ParameterName = "$identity";
                    _upsertParam2 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam2);
                    _upsertParam2.ParameterName = "$postId";
                    _upsertParam3 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam3);
                    _upsertParam3.ParameterName = "$singleReaction";
                    _upsertCommand.Prepare();
                }
                _upsertParam1.Value = item.identity;
                _upsertParam2.Value = item.postId;
                _upsertParam3.Value = item.singleReaction;
                _database.BeginTransaction();
                return _upsertCommand.ExecuteNonQuery();
            } // Lock
        }

        public virtual int Update(ReactionsItem item)
        {
            lock (_updateLock)
            {
                if (_updateCommand == null)
                {
                    _updateCommand = _database.CreateCommand();
                    _updateCommand.CommandText = "UPDATE reactions " +
                                                 "SET  "+
                                                 "WHERE (identity = $identity,postId = $postId,singleReaction = $singleReaction)";
                    _updateParam1 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam1);
                    _updateParam1.ParameterName = "$identity";
                    _updateParam2 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam2);
                    _updateParam2.ParameterName = "$postId";
                    _updateParam3 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam3);
                    _updateParam3.ParameterName = "$singleReaction";
                    _updateCommand.Prepare();
                }
                _updateParam1.Value = item.identity;
                _updateParam2.Value = item.postId;
                _updateParam3.Value = item.singleReaction;
                _database.BeginTransaction();
                return _updateCommand.ExecuteNonQuery();
            } // Lock
        }

        public int Delete(OdinId identity,Guid postId,string singleReaction)
        {
            lock (_deleteLock)
            {
                if (_deleteCommand == null)
                {
                    _deleteCommand = _database.CreateCommand();
                    _deleteCommand.CommandText = "DELETE FROM reactions " +
                                                 "WHERE identity = $identity AND postId = $postId AND singleReaction = $singleReaction";
                    _deleteParam1 = _deleteCommand.CreateParameter();
                    _deleteCommand.Parameters.Add(_deleteParam1);
                    _deleteParam1.ParameterName = "$identity";
                    _deleteParam2 = _deleteCommand.CreateParameter();
                    _deleteCommand.Parameters.Add(_deleteParam2);
                    _deleteParam2.ParameterName = "$postId";
                    _deleteParam3 = _deleteCommand.CreateParameter();
                    _deleteCommand.Parameters.Add(_deleteParam3);
                    _deleteParam3.ParameterName = "$singleReaction";
                    _deleteCommand.Prepare();
                }
                _deleteParam1.Value = identity;
                _deleteParam2.Value = postId;
                _deleteParam3.Value = singleReaction;
                _database.BeginTransaction();
                return _deleteCommand.ExecuteNonQuery();
            } // Lock
        }

        public ReactionsItem Get(OdinId identity,Guid postId,string singleReaction)
        {
            lock (_getLock)
            {
                if (_getCommand == null)
                {
                    _getCommand = _database.CreateCommand();
                    _getCommand.CommandText = "SELECT  FROM reactions " +
                                                 "WHERE identity = $identity AND postId = $postId AND singleReaction = $singleReaction;";
                    _getParam1 = _getCommand.CreateParameter();
                    _getCommand.Parameters.Add(_getParam1);
                    _getParam1.ParameterName = "$identity";
                    _getParam2 = _getCommand.CreateParameter();
                    _getCommand.Parameters.Add(_getParam2);
                    _getParam2.ParameterName = "$postId";
                    _getParam3 = _getCommand.CreateParameter();
                    _getCommand.Parameters.Add(_getParam3);
                    _getParam3.ParameterName = "$singleReaction";
                    _getCommand.Prepare();
                }
                _getParam1.Value = identity;
                _getParam2.Value = postId;
                _getParam3.Value = singleReaction;
                using (SQLiteDataReader rdr = _getCommand.ExecuteReader(System.Data.CommandBehavior.SingleRow))
                {
                    if (!rdr.Read())
                        return null;
                    var item = new ReactionsItem();
                    item.identity = identity;
                    item.postId = postId;
                    item.singleReaction = singleReaction;
                    byte[] _tmpbuf = new byte[65535+1];
                    long bytesRead;
                    var _guid = new byte[16];

                    return item;
                } // using
            } // lock
        }

    }
}
