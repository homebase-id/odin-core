using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace Youverse.Core.Storage.SQLite
{
    public class CommandMessage
    {
        public Guid fileId;
        public UnixTimeUtc timestamp;
    }

    public class TableCommandMessageQueue : TableBase
    {
        private SQLiteCommand _insertCommand = null;
        private SQLiteParameter _iparam1 = null;
        private Object _insertLock = new Object();

        private SQLiteCommand _deleteCommand = null;
        private SQLiteParameter _dparam1 = null;
        private Object _deleteLock = new Object();

        private SQLiteCommand _selectCommand = null;
        private Object _selectLock = new Object();


        public TableCommandMessageQueue(DriveDatabase db) : base(db)
        {
        }

        ~TableCommandMessageQueue()
        {
        }

        public override void Dispose()
        {
            _insertCommand?.Dispose();
            _insertCommand = null;

            _deleteCommand?.Dispose();
            _deleteCommand = null;

            _selectCommand?.Dispose();
            _selectCommand = null;
        }

        public override void EnsureTableExists(bool dropExisting = false)
        {
            using (var cmd = _database.CreateCommand())
            {
                if (dropExisting)
                {
                    cmd.CommandText = "DROP TABLE IF EXISTS commandmessages;";
                    cmd.ExecuteNonQuery();
                }

                cmd.CommandText = @"CREATE TABLE if not exists commandmessages(fileid BLOB NOT NULL, timestamp INT NOT NULL, UNIQUE(fileid));"
                                  + "CREATE INDEX if not exists FileIdIdx ON commandmessages(fileid);";

                cmd.ExecuteNonQuery();
            }
        }


        // Returns up to count items
        public List<CommandMessage> Get(int count)
        {
            lock (_selectLock)
            {
                using (_selectCommand = _database.CreateCommand())
                {
                    _selectCommand.CommandText = $"SELECT fileid,timestamp FROM commandmessages ORDER BY fileid ASC LIMIT {count}";

                    using (SQLiteDataReader rdr = _selectCommand.ExecuteReader(System.Data.CommandBehavior.SingleResult))
                    {
                        int i = 0;
                        var queue = new List<CommandMessage>();
                        byte[] bytes = new byte[16];
                        Int64 ts;

                        while (rdr.Read())
                        {
                            rdr.GetBytes(0, 0, bytes, 0, 16);
                            ts = rdr.GetInt64(1);

                            queue.Add(new CommandMessage { fileId = new Guid(bytes), timestamp = new UnixTimeUtc((UInt64)ts) });
                            i++;
                        }

                        if (i < 1)
                            return null;
                        else
                            return queue;
                    }
                }
            }
        }

        public void InsertRows(List<Guid> fileId)
        {
            if (fileId == null)
                return;

            lock (_insertLock)
            {
                // Make sure we only prep once - I wish I had been able to use local static vars
                // rather then class members
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand();
                    _insertCommand.CommandText = @"INSERT INTO commandmessages(fileid, timestamp) VALUES($fileid, 0)";
                    _iparam1 = _insertCommand.CreateParameter();
                    _iparam1.ParameterName = "$fileid";
                    _insertCommand.Parameters.Add(_iparam1);
                }

                _database.BeginTransaction();

                // Since we are writing multiple rows we do a logic unit here
                using (_database.CreateCommitUnitOfWork())
                {
                    for (int i = 0; i < fileId.Count; i++)
                    {
                        _iparam1.Value = fileId[i];
                        _insertCommand.ExecuteNonQuery();
                    }
                }
            }
        }

        public void DeleteRow(List<Guid> fileId)
        {
            if (fileId == null)
                return;

            lock (_deleteLock)
            {
                // Make sure we only prep once - I wish I had been able to use local static vars
                // rather then class members
                if (_deleteCommand == null)
                {
                    _deleteCommand = _database.CreateCommand();
                    _deleteCommand.CommandText = @"DELETE FROM commandmessages WHERE fileid=$fileid";
                    _dparam1 = _deleteCommand.CreateParameter();
                    _dparam1.ParameterName = "$fileid";
                    _deleteCommand.Parameters.Add(_dparam1);
                }

                _database.BeginTransaction();

                // Since we are deletign multiple rows we do a logic unit here
                using (_database.CreateCommitUnitOfWork())
                {
                    for (int i = 0; i < fileId.Count; i++)
                    {
                        _dparam1.Value = fileId[i];
                        _deleteCommand.ExecuteNonQuery();
                    }
                }
            }
        }
    }
}