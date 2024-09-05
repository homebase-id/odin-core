using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableOutbox : TableOutboxCRUD
    {
        public TableOutbox(IdentityDatabase db, CacheHelper cache) : base(db, cache)
        {
        }

        ~TableOutbox()
        {
        }

        public override void Dispose()
        {
            base.Dispose();
            GC.SuppressFinalize(this);
        }

        public List<OutboxRecord> Get(DatabaseConnection conn, Guid driveId, Guid fileId)
        {
            return base.Get(conn, ((IdentityDatabase) conn.db)._identityId, driveId, fileId);
        }

        public OutboxRecord Get(DatabaseConnection conn, Guid driveId, Guid fileId, string recipient)
        {
            return base.Get(conn, ((IdentityDatabase)conn.db)._identityId, driveId, fileId, recipient);
        }

        public new int Insert(DatabaseConnection conn, OutboxRecord item)
        {
            item.identityId = ((IdentityDatabase)conn.db)._identityId;
            item.checkOutCount = 0;
            if (item.nextRunTime.milliseconds == 0)
                item.nextRunTime = UnixTimeUtc.Now();
            if (ByteArrayUtil.muidcmp(item.fileId, item.dependencyFileId) == 0)
                throw new Exception("You're not allowed to make an item dependent on itself as it would deadlock the item.");
            return base.Insert(conn, item);
        }


        public new int Upsert(DatabaseConnection conn, OutboxRecord item)
        {
            if (ByteArrayUtil.muidcmp(item.fileId, item.dependencyFileId) == 0)
                throw new Exception("You're not allowed to make an item dependent on itself as it would deadlock the item.");

            item.identityId = ((IdentityDatabase)conn.db)._identityId;
            if (item.nextRunTime.milliseconds == 0)
                item.nextRunTime = UnixTimeUtc.Now();
            return base.Upsert(conn, item);
        }


        /// <summary>
        /// Will check out the next item to process. Will not check out items scheduled for the future
        /// or items that have unresolved dependencies.
        /// </summary>
        /// <returns></returns>
        public OutboxRecord CheckOutItem(DatabaseConnection conn)
        {
            using (var _popAllCommand = _database.CreateCommand())
            {
                _popAllCommand.CommandText = """
                        UPDATE outbox
                        SET checkOutStamp = $checkOutStamp
                        WHERE identityId=$identityId AND (checkOutStamp is NULL) AND
                          rowId = (
                              SELECT rowId
                              FROM outbox
                              WHERE identityId=$identityId AND checkOutStamp IS NULL
                              AND nextRunTime <= $now
                              AND (
                                (dependencyFileId IS NULL)
                                OR (NOT EXISTS (
                                      SELECT 1
                                      FROM outbox AS ib
                                      WHERE ib.identityId = outbox.identityId
                                      AND ib.fileId = outbox.dependencyFileId
                                      AND ib.recipient = outbox.recipient
                                ))
                              )
                              ORDER BY priority ASC, nextRunTime ASC
                              LIMIT 1
                        );
                        SELECT rowid,identityId,driveId,fileId,recipient,type,priority,dependencyFileId,checkOutCount,nextRunTime,value,checkOutStamp,created,modified
                        FROM outbox
                        WHERE identityId=$identityId AND checkOutStamp=$checkOutStamp;
                        """;
                var _paparam1 = _popAllCommand.CreateParameter();
                var _paparam2 = _popAllCommand.CreateParameter();
                var _paparam3 = _popAllCommand.CreateParameter();

                _paparam1.ParameterName = "$checkOutStamp";
                _paparam2.ParameterName = "$identityId";
                _paparam3.ParameterName = "$now";

                _popAllCommand.Parameters.Add(_paparam1);
                _popAllCommand.Parameters.Add(_paparam2);
                _popAllCommand.Parameters.Add(_paparam3);

                _paparam1.Value = SequentialGuid.CreateGuid().ToByteArray();
                _paparam2.Value = ((IdentityDatabase)conn.db)._identityId.ToByteArray();
                _paparam3.Value = UnixTimeUtc.Now().milliseconds;

                List<OutboxRecord> result = new List<OutboxRecord>();

                lock (conn._lock)
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_popAllCommand, System.Data.CommandBehavior.Default))
                    {
                        if (rdr.Read())
                        {
                            return ReadRecordFromReaderAll(rdr);
                        }
                        else
                            return null;
                    }

                }
            }
        }


        /// <summary>
        /// Returns when the next outbox item ís scheduled to be sent. If the returned time is larger than UnixTimeUtc.Now()
        /// it is in the future and you should schedule a job to activate at that time. If null there is nothing in the outbox 
        /// to be sent.
        /// Remember to cancel any pending outbox jobs, also if you're setting a schedule.
        /// </summary>
        /// <returns>UnixTimeUtc of when the next item should be sent, null if none.</returns>
        /// <exception cref="Exception"></exception>
        public UnixTimeUtc? NextScheduledItem(DatabaseConnection conn)
        {
            using (var _nextScheduleCommand = _database.CreateCommand())
            {
                _nextScheduleCommand.CommandText = "SELECT nextRunTime FROM outbox WHERE identityId=$identityId AND checkOutStamp IS NULL ORDER BY nextRunTime ASC LIMIT 1;";

                var _paparam1 = _nextScheduleCommand.CreateParameter();
                _paparam1.ParameterName = "$identityId";
                _nextScheduleCommand.Parameters.Add(_paparam1);

                _paparam1.Value = ((IdentityDatabase)conn.db)._identityId.ToByteArray();

                lock (conn._lock)
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_nextScheduleCommand, System.Data.CommandBehavior.Default))
                    {
                        // Read the total count
                        if (!rdr.Read())
                            return null;
                        if (rdr.IsDBNull(0))
                            throw new Exception("Not possible");

                        long nextRunTime = rdr.GetInt64(0);
                        return new UnixTimeUtc(nextRunTime);
                    }
                }
            }
        }


        /// <summary>
        /// Cancels the pop of items with the 'checkOutStamp' from a previous pop operation
        /// </summary>
        /// <param name="checkOutStamp"></param>
        public int CheckInAsCancelled(DatabaseConnection conn, Guid checkOutStamp, UnixTimeUtc nextRunTime)
        {
            using (var _popCancelCommand = _database.CreateCommand())
            {
                _popCancelCommand.CommandText = "UPDATE outbox SET checkOutStamp=NULL, checkOutCount=checkOutCount+1, nextRunTime=$nextRunTime WHERE identityId=$identityId AND checkOutStamp=$checkOutStamp";

                var _pcancelparam1 = _popCancelCommand.CreateParameter();
                var _pcancelparam2 = _popCancelCommand.CreateParameter();
                var _pcancelparam3 = _popCancelCommand.CreateParameter();

                _pcancelparam1.ParameterName = "$checkOutStamp";
                _pcancelparam2.ParameterName = "$nextRunTime";
                _pcancelparam3.ParameterName = "$identityId";

                _popCancelCommand.Parameters.Add(_pcancelparam1);
                _popCancelCommand.Parameters.Add(_pcancelparam2);
                _popCancelCommand.Parameters.Add(_pcancelparam3);

                _pcancelparam1.Value = checkOutStamp.ToByteArray();
                _pcancelparam2.Value = nextRunTime.milliseconds;
                _pcancelparam3.Value = ((IdentityDatabase)conn.db)._identityId.ToByteArray();

                return conn.ExecuteNonQuery(_popCancelCommand);
            }
        }



        /// <summary>
        /// Commits (removes) the items previously popped with the supplied 'checkOutStamp'
        /// </summary>
        /// <param name="checkOutStamp"></param>
        public int CompleteAndRemove(DatabaseConnection conn, Guid checkOutStamp)
        {
            using (var _popCommitCommand = _database.CreateCommand())
            {
                _popCommitCommand.CommandText = "DELETE FROM outbox WHERE identityId=$identityId AND checkOutStamp=$checkOutStamp";

                var _pcommitparam1 = _popCommitCommand.CreateParameter();
                var _pcommitparam2 = _popCommitCommand.CreateParameter();

                _pcommitparam1.ParameterName = "$checkOutStamp";
                _pcommitparam2.ParameterName = "$identityId";

                _popCommitCommand.Parameters.Add(_pcommitparam1);
                _popCommitCommand.Parameters.Add(_pcommitparam2);

                _pcommitparam1.Value = checkOutStamp.ToByteArray();
                _pcommitparam2.Value = ((IdentityDatabase)conn.db)._identityId.ToByteArray();

                return conn.ExecuteNonQuery(_popCommitCommand);
            }
        }



        /// <summary>
        /// Recover popped items older than the supplied UnixTime in seconds.
        /// This is how to recover popped items that were never processed for example on a server crash.
        /// Call with e.g. a time of more than 5 minutes ago.
        /// </summary>
        public int RecoverCheckedOutDeadItems(DatabaseConnection conn, UnixTimeUtc pastThreshold)
        {
            using (var _popRecoverCommand = _database.CreateCommand())
            {
                _popRecoverCommand.CommandText = "UPDATE outbox SET checkOutStamp=NULL,checkOutCount=checkOutCount+1 WHERE identityId=$identityId AND checkOutStamp < $checkOutStamp";

                // Should we also reset nextRunTime =$nextRunTime to "now()" or 0?
                //
                // Consider removing any items with checkOutCount == 0 older than X
                // since they are probably circular dependencies

                var _pcrecoverparam1 = _popRecoverCommand.CreateParameter();
                var _pcrecoverparam2 = _popRecoverCommand.CreateParameter();

                _pcrecoverparam1.ParameterName = "$checkOutStamp";
                _pcrecoverparam2.ParameterName = "$identityId";

                _popRecoverCommand.Parameters.Add(_pcrecoverparam1);
                _popRecoverCommand.Parameters.Add(_pcrecoverparam2);

                _pcrecoverparam1.Value = SequentialGuid.CreateGuid(pastThreshold).ToByteArray(); // UnixTimeMiliseconds
                _pcrecoverparam2.Value = ((IdentityDatabase)conn.db)._identityId.ToByteArray();

                return conn.ExecuteNonQuery(_popRecoverCommand);
            }
        }


        /// <summary>
        /// Status on the box
        /// </summary>
        /// <returns>Number of total items in box, number of popped items, the next item schduled time (ZeroTime if none)</returns>
        /// <exception cref="Exception"></exception>
        public (int totalItems, int checkedOutItems, UnixTimeUtc nextRunTime) OutboxStatus(DatabaseConnection conn)
        {
            using (var _popStatusCommand = _database.CreateCommand())
            {
                _popStatusCommand.CommandText =
                    "SELECT count(*) FROM outbox WHERE identityId=$identityId;" +
                    "SELECT count(*) FROM outbox WHERE identityId=$identityId AND checkOutStamp NOT NULL;" +
                    "SELECT nextRunTime FROM outbox WHERE identityId=$identityId AND checkOutStamp IS NULL ORDER BY nextRunTime ASC LIMIT 1;";

                var _pcrecoverparam1 = _popStatusCommand.CreateParameter();
                _pcrecoverparam1.ParameterName = "$identityId";
                _popStatusCommand.Parameters.Add(_pcrecoverparam1);
                _pcrecoverparam1.Value = ((IdentityDatabase)conn.db)._identityId.ToByteArray();

                lock (conn._lock)
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_popStatusCommand, System.Data.CommandBehavior.Default))
                    {
                        // Read the total count
                        if (!rdr.Read())
                            throw new Exception("Not possible");

                        int totalCount = 0;
                        if (!rdr.IsDBNull(0))
                            totalCount = rdr.GetInt32(0);

                        // Read the popped count
                        if (!rdr.NextResult())
                            throw new Exception("Not possible");
                        if (!rdr.Read())
                            throw new Exception("Not possible");

                        int poppedCount = 0;
                        if (!rdr.IsDBNull(0))
                            poppedCount = rdr.GetInt32(0);

                        if (!rdr.NextResult())
                            throw new Exception("Not possible");

                        var utc = UnixTimeUtc.ZeroTime;
                        if (rdr.Read())
                        {
                            // Read the marker, if any
                            if (!rdr.IsDBNull(0))
                            {
                                Int64 t = rdr.GetInt64(0);
                                utc = new UnixTimeUtc(t);
                            }
                        }

                        return (totalCount, poppedCount, utc);
                    }
                }
            }
        }



        /// <summary>
        /// Status on the box
        /// </summary>
        /// <returns>Number of total items in box, number of popped items, the next item schduled time (ZeroTime if none)</returns>
        /// <exception cref="Exception"></exception>
        public (int, int, UnixTimeUtc) OutboxStatusDrive(DatabaseConnection conn, Guid driveId)
        {
            using (var _popStatusSpecificBoxCommand = _database.CreateCommand())
            {
                _popStatusSpecificBoxCommand.CommandText =
                    "SELECT count(*) FROM outbox WHERE identityId=$identityId AND driveId=$driveId;" +
                    "SELECT count(*) FROM outbox WHERE identityId=$identityId AND driveId=$driveId AND checkOutStamp NOT NULL;" +
                    "SELECT nextRunTime FROM outbox WHERE identityId=$identityId AND driveId=$driveId AND checkOutStamp IS NULL ORDER BY nextRunTime ASC LIMIT 1;";

                var _pssbParam1 = _popStatusSpecificBoxCommand.CreateParameter();
                var _pssbParam2 = _popStatusSpecificBoxCommand.CreateParameter();

                _pssbParam1.ParameterName = "$driveId";
                _pssbParam2.ParameterName = "$identityId";

                _popStatusSpecificBoxCommand.Parameters.Add(_pssbParam1);
                _popStatusSpecificBoxCommand.Parameters.Add(_pssbParam2);

                _pssbParam1.Value = driveId.ToByteArray();
                _pssbParam2.Value = ((IdentityDatabase)conn.db)._identityId.ToByteArray();

                lock (conn._lock)
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_popStatusSpecificBoxCommand, System.Data.CommandBehavior.Default))
                    {
                        // Read the total count
                        if (!rdr.Read())
                            throw new Exception("Not possible");

                        int totalCount = 0;
                        if (!rdr.IsDBNull(0))
                            totalCount = rdr.GetInt32(0);

                        // Read the popped count
                        if (!rdr.NextResult())
                            throw new Exception("Not possible");
                        if (!rdr.Read())
                            throw new Exception("Not possible");

                        int poppedCount = 0;
                        if (!rdr.IsDBNull(0))
                            poppedCount = rdr.GetInt32(0);

                        if (!rdr.NextResult())
                            throw new Exception("Not possible");

                        var utc = UnixTimeUtc.ZeroTime;
                        if (rdr.Read())
                        {
                            // Read the marker, if any
                            if (!rdr.IsDBNull(0))
                            {
                                Int64 t = rdr.GetInt64(0);
                                utc = new UnixTimeUtc(t);
                            }
                        }
                        return (totalCount, poppedCount, utc);
                    }
                }
            }
        }
    }
}

//
// Any checks for circular dependencies?
// Any method for removing outbox items that are circular?
//    Perhaps remove any item with a popCount of 0 and nextRunTime older than X.
//    Maybe just build that into RecoverDead.
//

/*
        /// <summary>
        /// Pops 'count' items from the outbox. The items remain in the DB with the 'checkOutStamp' unique identifier.
        /// checkOutStamp is used by the caller to release the items when they have been successfully processed, or
        /// to cancel the transaction and restore the items to the outbox.
        /// </summary
        /// <param name="driveId">Is the outbox to pop from, e.g. Drive A, or App B</param>
        /// <param name="count">How many items to 'pop' (reserve)</param>
        /// <param name="checkOutStamp">The unique identifier for the items reserved for pop</param>
        /// <returns></returns>
        public List<OutboxRecord> CheckOutItemsForProcessingSpecificBox(Guid driveId, int count)
        {
            lock (_popLock)
            {
                // Make sure we only prep once 
                if (_popSpecificBoxCommand == null)
                {
                    _popSpecificBoxCommand = _database.CreateCommand(conn);
                    _popSpecificBoxCommand.CommandText =
                        "UPDATE outbox SET checkOutStamp=$checkOutStamp "+
                        "WHERE (checkOutStamp is NULL) AND "+
                        "fileId IN (SELECT fileid FROM outbox WHERE driveId=$driveId AND checkOutStamp is NULL ORDER BY priority ASC, nextRunStamp ASC LIMIT $count);" +
                        "SELECT rowid,driveId,fileId,recipient,type,priority,checkOutCount,timeStamp,nextRunStamp,value,checkOutStamp,created,modified FROM outbox WHERE checkOutStamp=$checkOutStamp";

                    _psbparam1 = _popSpecificBoxCommand.CreateParameter();
                    _psbparam1.ParameterName = "$checkOutStamp";
                    _popSpecificBoxCommand.Parameters.Add(_psbparam1);

                    _psbparam2 = _popSpecificBoxCommand.CreateParameter();
                    _psbparam2.ParameterName = "$count";
                    _popSpecificBoxCommand.Parameters.Add(_psbparam2);

                    _psbparam3 = _popSpecificBoxCommand.CreateParameter();
                    _psbparam3.ParameterName = "$driveId";
                    _popSpecificBoxCommand.Parameters.Add(_psbparam3);

                    _popSpecificBoxCommand.Prepare();
                }

                _psbparam1.Value = SequentialGuid.CreateGuid().ToByteArray();
                _psbparam2.Value = count;
                _psbparam3.Value = driveId.ToByteArray();

                List<OutboxRecord> result = new List<OutboxRecord>();

                using (_database.CreateCommitUnitOfWork())
                {
                    using (SqliteDataReader rdr = _database.ExecuteReader(_popSpecificBoxCommand, System.Data.CommandBehavior.Default))
                    {
                        while (rdr.Read())
                        {
                            result.Add(ReadRecordFromReaderAll(rdr));
                        }
                    }

                    return result;
                }
            }
        }
*/


/*
        /// <summary>
        /// Commits (removes) the items previously popped with the supplied 'checkOutStamp'
        /// </summary>
        /// <param name="checkOutStamp"></param>
        public void CompleteAndRemoveList(Guid checkOutStamp, List<Guid> listFileId)
        {
            lock (_popCommitListLock)
            {
                // Make sure we only prep once 
                if (_popCommitListCommand == null)
                {
                    _popCommitListCommand = _database.CreateCommand(conn);
                    _popCommitListCommand.CommandText = "DELETE FROM outbox WHERE fileid=$fileid AND checkOutStamp=$checkOutStamp";

                    _pcommitlistparam1 = _popCommitListCommand.CreateParameter();
                    _pcommitlistparam1.ParameterName = "$checkOutStamp";
                    _popCommitListCommand.Parameters.Add(_pcommitlistparam1);

                    _pcommitlistparam2 = _popCommitListCommand.CreateParameter();
                    _pcommitlistparam2.ParameterName = "$fileid";
                    _popCommitListCommand.Parameters.Add(_pcommitlistparam2);

                    _popCommitListCommand.Prepare();
                }

                _pcommitlistparam1.Value = checkOutStamp.ToByteArray();

                using (_database.CreateCommitUnitOfWork())
                {
                    // I'd rather not do a TEXT statement, this seems safer but slower.
                    for (int i = 0; i < listFileId.Count; i++)
                    {
                        _pcommitlistparam2.Value = listFileId[i].ToByteArray();
                        _database.ExecuteNonQuery(_popCommitListCommand);
                    }
                }
            }
        }
*/