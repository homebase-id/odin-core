using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableDriveCommandMessageQueue : TableDriveCommandMessageQueueCRUD
    {
        private SqliteCommand _selectCommand = null;
        
        private Object _selectLock = new Object();


        public TableDriveCommandMessageQueue(IdentityDatabase db, CacheHelper cache) : base(db, cache)
        {
        }

        ~TableDriveCommandMessageQueue()
        {
        }

        public override void Dispose()
        {
            _selectCommand?.Dispose();
            _selectCommand = null;

            base.Dispose();
            GC.SuppressFinalize(this);
        }


        // Returns up to count items
        public List<DriveCommandMessageQueueRecord> Get(DatabaseBase.DatabaseConnection conn, Guid driveId, int count)
        {
            lock (_selectLock)
            {
                using (_selectCommand = _database.CreateCommand(conn))
                {
                    _selectCommand.CommandText = $"SELECT driveid,fileid,timestamp FROM driveCommandMessageQueue WHERE driveId = x'{Convert.ToHexString(driveId.ToByteArray())}' ORDER BY fileid ASC LIMIT {count}";

                    using (SqliteDataReader rdr = _database.ExecuteReader(conn, _selectCommand, System.Data.CommandBehavior.SingleResult))
                    {
                        int i = 0;
                        var queue = new List<DriveCommandMessageQueueRecord>();

                        while (rdr.Read())
                        {
                            queue.Add(ReadRecordFromReaderAll(rdr));
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

        public void InsertRows(DatabaseBase.DatabaseConnection conn, Guid driveId, List<Guid> fileId)
        {
            if (fileId == null)
                return;

            // Since we are writing multiple rows we do a logic unit here
            using (conn.CreateCommitUnitOfWork())
            {
                var item = new DriveCommandMessageQueueRecord() { driveId = driveId, timeStamp = new UnixTimeUtc(0) };
                for (int i = 0; i < fileId.Count; i++)
                {
                    item.fileId = fileId[i];
                    Insert(conn, item);
                }
            }
        }

        public void DeleteRow(DatabaseBase.DatabaseConnection conn, Guid driveId, List<Guid> fileId)
        {
            if (fileId == null)
                return;

            // Since we are deletign multiple rows we do a logic unit here
            using (conn.CreateCommitUnitOfWork())
            {
                for (int i = 0; i < fileId.Count; i++)
                {
                    Delete(conn, driveId, fileId[i]);
                }
            }
        }
    }
}