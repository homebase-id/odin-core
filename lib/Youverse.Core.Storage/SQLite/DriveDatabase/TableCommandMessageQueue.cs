﻿using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace Youverse.Core.Storage.Sqlite.DriveDatabase
{
    public class CommandMessage
    {
        public Guid fileId;
        public UnixTimeUtc timestamp;
    }

    public class TableCommandMessageQueue : TableCommandMessageQueueCRUD
    {
        private SqliteCommand _selectCommand = null;
        
        private Object _selectLock = new Object();


        public TableCommandMessageQueue(DriveDatabase db) : base(db)
        {
        }

        ~TableCommandMessageQueue()
        {
        }

        public override void Dispose()
        {
            _selectCommand?.Dispose();
            _selectCommand = null;

            base.Dispose();
        }


        // Returns up to count items
        public List<CommandMessage> Get(int count)
        {
            lock (_selectLock)
            {
                using (_selectCommand = _database.CreateCommand())
                {
                    _selectCommand.CommandText = $"SELECT fileid,timestamp FROM commandMessageQueue ORDER BY fileid ASC LIMIT {count}";

                    using (SqliteDataReader rdr = _selectCommand.ExecuteReader(System.Data.CommandBehavior.SingleResult, _database))
                    {
                        int i = 0;
                        long n;
                        var queue = new List<CommandMessage>();
                        byte[] _guid = new byte[16];
                        Int64 ts;

                        while (rdr.Read())
                        {
                            n = rdr.GetBytes(0, 0, _guid, 0, 16);
                            if (n != 16)
                                throw new Exception("Not a guid");
                            ts = rdr.GetInt64(1);

                            queue.Add(new CommandMessage { fileId = new Guid(_guid), timestamp = new UnixTimeUtc((UInt64)ts) });
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

            // Since we are writing multiple rows we do a logic unit here
            using (_database.CreateCommitUnitOfWork())
            {
                var item = new CommandMessageQueueRecord() { timeStamp = new UnixTimeUtc(0) };
                for (int i = 0; i < fileId.Count; i++)
                {
                    item.fileId = fileId[i];
                    Insert(item);
                }
            }
        }

        public void DeleteRow(List<Guid> fileId)
        {
            if (fileId == null)
                return;

            // Since we are deletign multiple rows we do a logic unit here
            using (_database.CreateCommitUnitOfWork())
            {
                for (int i = 0; i < fileId.Count; i++)
                {
                    Delete(fileId[i]);
                }
            }
        }
    }
}