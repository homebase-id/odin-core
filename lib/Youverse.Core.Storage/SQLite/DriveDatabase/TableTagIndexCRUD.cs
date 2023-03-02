using System;
using System.Collections.Generic;
using System.Data.SQLite;
using Youverse.Core.Identity;

namespace Youverse.Core.Storage.SQLite.DriveDatabase
{
    public class TagIndexItem
    {
        private Guid _fileId;
        public Guid fileId
        {
           get {
                   return _fileId;
               }
           set {
                  _fileId = value;
               }
        }
        private Guid _tagId;
        public Guid tagId
        {
           get {
                   return _tagId;
               }
           set {
                  _tagId = value;
               }
        }
    } // End of class TagIndexItem

    public class TableTagIndexCRUD : TableBase
    {
        private bool _disposed = false;
        private SQLiteCommand _insertCommand = null;
        private static Object _insertLock = new Object();
        private SQLiteParameter _insertParam1 = null;
        private SQLiteParameter _insertParam2 = null;
        private SQLiteCommand _updateCommand = null;
        private static Object _updateLock = new Object();
        private SQLiteParameter _updateParam1 = null;
        private SQLiteParameter _updateParam2 = null;
        private SQLiteCommand _upsertCommand = null;
        private static Object _upsertLock = new Object();
        private SQLiteParameter _upsertParam1 = null;
        private SQLiteParameter _upsertParam2 = null;
        private SQLiteCommand _deleteCommand = null;
        private static Object _deleteLock = new Object();
        private SQLiteParameter _deleteParam1 = null;
        private SQLiteParameter _deleteParam2 = null;
        private SQLiteCommand _get0Command = null;
        private static Object _get0Lock = new Object();
        private SQLiteParameter _get0Param1 = null;
        private SQLiteParameter _get0Param2 = null;

        public TableTagIndexCRUD(DriveDatabase db) : base(db)
        {
        }

        ~TableTagIndexCRUD()
        {
            if (_disposed == false) throw new Exception("TableTagIndexCRUD Not disposed properly");
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
            _disposed = true;
        }

        public sealed override void EnsureTableExists(bool dropExisting = false)
        {
            using (var cmd = _database.CreateCommand())
            {
                if (dropExisting)
                {
                    cmd.CommandText = "DROP TABLE IF EXISTS tagIndex;";
                    cmd.ExecuteNonQuery();
                }
                cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS tagIndex("
                     +"fileId BLOB NOT NULL, "
                     +"tagId BLOB NOT NULL "
                     +", PRIMARY KEY (fileId,tagId)"
                     +");"
                     +"CREATE INDEX IF NOT EXISTS Idx0TableTagIndexCRUD ON tagIndex(fileId);"
                     ;
                cmd.ExecuteNonQuery();
            }
        }

        public virtual int Insert(TagIndexItem item)
        {
            lock (_insertLock)
            {
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand();
                    _insertCommand.CommandText = "INSERT INTO tagIndex (fileId,tagId) " +
                                                 "VALUES ($fileId,$tagId)";
                    _insertParam1 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam1);
                    _insertParam1.ParameterName = "$fileId";
                    _insertParam2 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam2);
                    _insertParam2.ParameterName = "$tagId";
                    _insertCommand.Prepare();
                }
                _insertParam1.Value = item.fileId;
                _insertParam2.Value = item.tagId;
                _database.BeginTransaction();
                return _insertCommand.ExecuteNonQuery();
            } // Lock
        }

        public virtual int Upsert(TagIndexItem item)
        {
            lock (_upsertLock)
            {
                if (_upsertCommand == null)
                {
                    _upsertCommand = _database.CreateCommand();
                    _upsertCommand.CommandText = "INSERT INTO tagIndex (fileId,tagId) " +
                                                 "VALUES ($fileId,$tagId)"+
                                                 "ON CONFLICT (fileId,tagId) DO UPDATE "+
                                                 "SET ;";
                    _upsertParam1 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam1);
                    _upsertParam1.ParameterName = "$fileId";
                    _upsertParam2 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam2);
                    _upsertParam2.ParameterName = "$tagId";
                    _upsertCommand.Prepare();
                }
                _upsertParam1.Value = item.fileId;
                _upsertParam2.Value = item.tagId;
                _database.BeginTransaction();
                return _upsertCommand.ExecuteNonQuery();
            } // Lock
        }

        public virtual int Update(TagIndexItem item)
        {
            lock (_updateLock)
            {
                if (_updateCommand == null)
                {
                    _updateCommand = _database.CreateCommand();
                    _updateCommand.CommandText = "UPDATE tagIndex " +
                                                 "SET  "+
                                                 "WHERE (fileId = $fileId,tagId = $tagId)";
                    _updateParam1 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam1);
                    _updateParam1.ParameterName = "$fileId";
                    _updateParam2 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam2);
                    _updateParam2.ParameterName = "$tagId";
                    _updateCommand.Prepare();
                }
                _updateParam1.Value = item.fileId;
                _updateParam2.Value = item.tagId;
                _database.BeginTransaction();
                return _updateCommand.ExecuteNonQuery();
            } // Lock
        }

        public int Delete(Guid fileId,Guid tagId)
        {
            lock (_deleteLock)
            {
                if (_deleteCommand == null)
                {
                    _deleteCommand = _database.CreateCommand();
                    _deleteCommand.CommandText = "DELETE FROM tagIndex " +
                                                 "WHERE fileId = $fileId AND tagId = $tagId";
                    _deleteParam1 = _deleteCommand.CreateParameter();
                    _deleteCommand.Parameters.Add(_deleteParam1);
                    _deleteParam1.ParameterName = "$fileId";
                    _deleteParam2 = _deleteCommand.CreateParameter();
                    _deleteCommand.Parameters.Add(_deleteParam2);
                    _deleteParam2.ParameterName = "$tagId";
                    _deleteCommand.Prepare();
                }
                _deleteParam1.Value = fileId;
                _deleteParam2.Value = tagId;
                _database.BeginTransaction();
                return _deleteCommand.ExecuteNonQuery();
            } // Lock
        }

        public TagIndexItem Get(Guid fileId,Guid tagId)
        {
            lock (_get0Lock)
            {
                if (_get0Command == null)
                {
                    _get0Command = _database.CreateCommand();
                    _get0Command.CommandText = "SELECT  FROM tagIndex " +
                                                 "WHERE fileId = $fileId AND tagId = $tagId LIMIT 1;";
                    _get0Param1 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param1);
                    _get0Param1.ParameterName = "$fileId";
                    _get0Param2 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param2);
                    _get0Param2.ParameterName = "$tagId";
                    _get0Command.Prepare();
                }
                _get0Param1.Value = fileId;
                _get0Param2.Value = tagId;
                using (SQLiteDataReader rdr = _get0Command.ExecuteReader(System.Data.CommandBehavior.SingleRow))
                {
                    var result = new TagIndexItem();
                    if (!rdr.Read())
                        return null;
                    byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
                    long bytesRead;
#pragma warning restore CS0168
                    var _guid = new byte[16];
                        var item = new TagIndexItem();
                        item.fileId = fileId;
                        item.tagId = tagId;
                    return item;
                } // using
            } // lock
        }

    }
}
