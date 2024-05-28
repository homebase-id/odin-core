using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableDriveCommandMessageQueue : TableDriveCommandMessageQueueCRUD
    {
        public TableDriveCommandMessageQueue(IdentityDatabase db, CacheHelper cache) : base(db, cache)
        {
        }

        ~TableDriveCommandMessageQueue()
        {
        }

        public override void Dispose()
        {
            base.Dispose();
            GC.SuppressFinalize(this);
        }


        // Returns up to count items
        public List<DriveCommandMessageQueueRecord> Get(DatabaseConnection conn, Guid driveId, int count)
        {
            using (var _selectCommand = _database.CreateCommand())
            {
                _selectCommand.CommandText = $"SELECT identityId,driveid,fileid,timestamp FROM driveCommandMessageQueue WHERE driveId = x'{Convert.ToHexString(driveId.ToByteArray())}' ORDER BY fileid ASC LIMIT {count}";

                var _selectParam1 = _selectCommand.CreateParameter();
                _selectParam1.ParameterName = "$identityId";
                _selectCommand.Parameters.Add(_selectParam1);

                _selectParam1.Value = ((IdentityDatabase)conn.db)._identityId.ToByteArray();

                lock (conn._lock)
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_selectCommand, System.Data.CommandBehavior.SingleResult))
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

        public void InsertRows(DatabaseConnection conn, Guid driveId, List<Guid> fileId)
        {
            if (fileId == null)
                return;

            // Since we are writing multiple rows we do a logic unit here
            conn.CreateCommitUnitOfWork(() =>
            {
                var item = new DriveCommandMessageQueueRecord() { identityId = ((IdentityDatabase)conn.db)._identityId, driveId = driveId, timeStamp = new UnixTimeUtc(0) };
                for (int i = 0; i < fileId.Count; i++)
                {
                    item.fileId = fileId[i];
                    Insert(conn, item);
                }
            });
        }

        public void DeleteRow(DatabaseConnection conn, Guid driveId, List<Guid> fileId)
        {
            if (fileId == null)
                return;

            // Since we are deletign multiple rows we do a logic unit here
            conn.CreateCommitUnitOfWork(() =>
            {
                for (int i = 0; i < fileId.Count; i++)
                {
                    Delete(conn, ((IdentityDatabase)conn.db)._identityId, driveId, fileId[i]);
                }
            });
        }
    }
}