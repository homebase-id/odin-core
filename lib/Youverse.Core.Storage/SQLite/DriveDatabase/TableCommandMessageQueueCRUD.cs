using System;
using System.Collections.Generic;
using System.Data.SQLite;
using Youverse.Core.Identity;

namespace Youverse.Core.Storage.SQLite.DriveDatabase
{
    public class CommandMessageQueueItem
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
        private UnixTimeUtc _timeStamp;
        public UnixTimeUtc timeStamp
        {
           get {
                   return _timeStamp;
               }
           set {
                  _timeStamp = value;
               }
        }
    } // End of class CommandMessageQueueItem

    public class TableCommandMessageQueueCRUD : TableBase
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
        private SQLiteCommand _get0Command = null;
        private static Object _get0Lock = new Object();
        private SQLiteParameter _get0Param1 = null;

        public TableCommandMessageQueueCRUD(DriveDatabase db) : base(db)
        {
        }

        ~TableCommandMessageQueueCRUD()
        {
            if (_disposed == false) throw new Exception("TableCommandMessageQueueCRUD Not disposed properly");
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
                    cmd.CommandText = "DROP TABLE IF EXISTS commandMessageQueue;";
                    cmd.ExecuteNonQuery();
                }
                cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS commandMessageQueue("
                     +"fileId BLOB NOT NULL UNIQUE, "
                     +"timeStamp INT NOT NULL "
                     +", PRIMARY KEY (fileId)"
                     +");"
                     ;
                cmd.ExecuteNonQuery();
            }
        }

        public virtual int Insert(CommandMessageQueueItem item)
        {
            lock (_insertLock)
            {
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand();
                    _insertCommand.CommandText = "INSERT INTO commandMessageQueue (fileId,timeStamp) " +
                                                 "VALUES ($fileId,$timeStamp)";
                    _insertParam1 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam1);
                    _insertParam1.ParameterName = "$fileId";
                    _insertParam2 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam2);
                    _insertParam2.ParameterName = "$timeStamp";
                    _insertCommand.Prepare();
                }
                _insertParam1.Value = item.fileId;
                _insertParam2.Value = item.timeStamp.milliseconds;
                _database.BeginTransaction();
                return _insertCommand.ExecuteNonQuery();
            } // Lock
        }

        public virtual int Upsert(CommandMessageQueueItem item)
        {
            lock (_upsertLock)
            {
                if (_upsertCommand == null)
                {
                    _upsertCommand = _database.CreateCommand();
                    _upsertCommand.CommandText = "INSERT INTO commandMessageQueue (fileId,timeStamp) " +
                                                 "VALUES ($fileId,$timeStamp)"+
                                                 "ON CONFLICT (fileId) DO UPDATE "+
                                                 "SET timeStamp = $timeStamp;";
                    _upsertParam1 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam1);
                    _upsertParam1.ParameterName = "$fileId";
                    _upsertParam2 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam2);
                    _upsertParam2.ParameterName = "$timeStamp";
                    _upsertCommand.Prepare();
                }
                _upsertParam1.Value = item.fileId;
                _upsertParam2.Value = item.timeStamp.milliseconds;
                _database.BeginTransaction();
                return _upsertCommand.ExecuteNonQuery();
            } // Lock
        }

        public virtual int Update(CommandMessageQueueItem item)
        {
            lock (_updateLock)
            {
                if (_updateCommand == null)
                {
                    _updateCommand = _database.CreateCommand();
                    _updateCommand.CommandText = "UPDATE commandMessageQueue " +
                                                 "SET timeStamp = $timeStamp "+
                                                 "WHERE (fileId = $fileId)";
                    _updateParam1 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam1);
                    _updateParam1.ParameterName = "$fileId";
                    _updateParam2 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam2);
                    _updateParam2.ParameterName = "$timeStamp";
                    _updateCommand.Prepare();
                }
                _updateParam1.Value = item.fileId;
                _updateParam2.Value = item.timeStamp.milliseconds;
                _database.BeginTransaction();
                return _updateCommand.ExecuteNonQuery();
            } // Lock
        }

        public int Delete(Guid fileId)
        {
            lock (_deleteLock)
            {
                if (_deleteCommand == null)
                {
                    _deleteCommand = _database.CreateCommand();
                    _deleteCommand.CommandText = "DELETE FROM commandMessageQueue " +
                                                 "WHERE fileId = $fileId";
                    _deleteParam1 = _deleteCommand.CreateParameter();
                    _deleteCommand.Parameters.Add(_deleteParam1);
                    _deleteParam1.ParameterName = "$fileId";
                    _deleteCommand.Prepare();
                }
                _deleteParam1.Value = fileId;
                _database.BeginTransaction();
                return _deleteCommand.ExecuteNonQuery();
            } // Lock
        }

        public CommandMessageQueueItem Get(Guid fileId)
        {
            lock (_get0Lock)
            {
                if (_get0Command == null)
                {
                    _get0Command = _database.CreateCommand();
                    _get0Command.CommandText = "SELECT timeStamp FROM commandMessageQueue " +
                                                 "WHERE fileId = $fileId LIMIT 1;";
                    _get0Param1 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param1);
                    _get0Param1.ParameterName = "$fileId";
                    _get0Command.Prepare();
                }
                _get0Param1.Value = fileId;
                using (SQLiteDataReader rdr = _get0Command.ExecuteReader(System.Data.CommandBehavior.SingleRow))
                {
                    var result = new CommandMessageQueueItem();
                    if (!rdr.Read())
                        return null;
                    byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
                    long bytesRead;
#pragma warning restore CS0168
                    var _guid = new byte[16];
                        var item = new CommandMessageQueueItem();
                        item.fileId = fileId;

                        if (rdr.IsDBNull(0))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            item.timeStamp = new UnixTimeUtc((UInt64) rdr.GetInt64(0));
                        }
                    return item;
                } // using
            } // lock
        }

    }
}
