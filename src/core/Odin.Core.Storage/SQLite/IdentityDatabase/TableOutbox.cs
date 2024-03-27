using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableOutbox: TableOutboxCRUD
    {
        private SqliteCommand _popSpecificBoxCommand = null;
        //private SqliteParameter _psbparam1 = null;
        //private SqliteParameter _psbparam2 = null;
        //private SqliteParameter _psbparam3 = null;
        private static Object _outboxLock = new Object();

        private SqliteCommand _popAllCommand = null;
        private SqliteParameter _paparam1 = null;
        private SqliteParameter _paparam2 = null;
        private SqliteParameter _paparam3 = null;
        private static Object _popAllLock = new Object();

        private SqliteCommand _nextScheduleCommand = null;
        private SqliteParameter _nextScheduleParam1 = null;
        private static Object _nextScheduleLock = new Object();


        private SqliteCommand _popStatusCommand = null;
        private SqliteParameter _pscParam1 = null;

        private SqliteCommand _popStatusSpecificBoxCommand = null;
        private SqliteParameter _pssbParam1 = null;
        private SqliteParameter _pssbParam2 = null;

        private SqliteCommand _popCancelCommand = null;
        private SqliteParameter _pcancelparam1 = null;
        private SqliteParameter _pcancelparam2 = null;

        private SqliteCommand _popCancelListCommand = null;
        //private SqliteParameter _pcancellistparam1 = null;
        //private SqliteParameter _pcancellistparam2 = null;
        //private static Object _popCancelListLock = new Object();

        private SqliteCommand _popCommitCommand = null;
        private SqliteParameter _pcommitparam1 = null;

        private SqliteCommand _popCommitListCommand = null;
        //private SqliteParameter _pcommitlistparam1 = null;
        //private SqliteParameter _pcommitlistparam2 = null;
        //private static Object _popCommitListLock = new Object();

        private SqliteCommand _popRecoverCommand = null;
        private SqliteParameter _pcrecoverparam1 = null;

        private const string whereClause = 
            "WHERE (checkOutStamp is NULL) AND " +
                        "   fileId IN (" +
                        "      SELECT fileid " +
                        "      FROM outbox " +
                        "      WHERE " +
                        "        checkOutStamp is NULL AND " +
                        "        nextRunTime <= $now AND " +
                        "        ((dependencyFileId IS NULL) OR " +
                        "         (NOT EXISTS (SELECT 1 FROM outbox AS ib WHERE ib.fileId = outbox.dependencyFileId AND ib.recipient = outbox.recipient)))" +
                        "      ORDER BY priority ASC, nextRunTime ASC LIMIT 1) ";

        public TableOutbox(IdentityDatabase db, CacheHelper cache) : base(db, cache)
        {
        }

        ~TableOutbox()
        {
        }

        public override void Dispose()
        {
            _popSpecificBoxCommand?.Dispose();
            _popSpecificBoxCommand = null;

            _popAllCommand?.Dispose();
            _popAllCommand = null;

            _popStatusCommand?.Dispose();
            _popStatusCommand = null;

            _popStatusSpecificBoxCommand?.Dispose();
            _popStatusSpecificBoxCommand = null;

            _popCancelCommand?.Dispose();
            _popCancelCommand = null;

            _popCancelListCommand?.Dispose();
            _popCancelListCommand = null;

            _popCommitCommand?.Dispose();
            _popCommitCommand = null;

            _popCommitListCommand?.Dispose();
            _popCommitListCommand = null;

            _popRecoverCommand?.Dispose();
            _popRecoverCommand = null;

            base.Dispose();
        }


        public override int Insert(OutboxRecord item)
        {
            item.checkOutCount = 0;
            if (item.nextRunTime.milliseconds == 0)
                item.nextRunTime = UnixTimeUtc.Now();
            if (ByteArrayUtil.muidcmp(item.fileId, item.dependencyFileId) == 0)
                throw new Exception("You're not allowed to make an item dependent on itself as it would deadlock the item.");
            return base.Insert(item);
        }


        public override int Upsert(OutboxRecord item)
        {
            if (item.nextRunTime.milliseconds == 0)
                item.nextRunTime = UnixTimeUtc.Now();
            if (ByteArrayUtil.muidcmp(item.fileId, item.dependencyFileId) == 0)
                throw new Exception("You're not allowed to make an item dependent on itself as it would deadlock the item.");
            return base.Upsert(item);
        }


        /// <summary>
        /// Will check out the next item to process. Will not check out items scheduled for the future
        /// or items that have unresolved dependencies.
        /// </summary>
        /// <returns></returns>
        public OutboxRecord CheckOutItem()
        {
            lock (_popAllLock)
            {
                // Make sure we only prep once 
                if (_popAllCommand == null)
                {
                    _popAllCommand = _database.CreateCommand();
                    _popAllCommand.CommandText =
                        "UPDATE outbox " +
                        "SET checkOutStamp=$checkOutStamp " +
                        whereClause +
                        " ; " +
                        "SELECT rowid,driveId,fileId,recipient,type,priority,dependencyFileId,checkOutCount,nextRunTime,value,checkOutStamp,created,modified " +
                        "FROM outbox " +
                        "WHERE checkOutStamp=$checkOutStamp";
                    _paparam1 = _popAllCommand.CreateParameter();
                    _paparam1.ParameterName = "$checkOutStamp";
                    _popAllCommand.Parameters.Add(_paparam1);

                    // 2 & 3 ARE OBSOLETE - DELETE LATER
                    _paparam2 = _popAllCommand.CreateParameter();
                    _paparam2.ParameterName = "$count";
                    _popAllCommand.Parameters.Add(_paparam2);

                    _paparam3 = _popAllCommand.CreateParameter();
                    _paparam3.ParameterName = "$now";
                    _popAllCommand.Parameters.Add(_paparam3);

                    _popAllCommand.Prepare();
                }

                _paparam1.Value = SequentialGuid.CreateGuid().ToByteArray();
                _paparam2.Value = 1;
                _paparam3.Value = UnixTimeUtc.Now().milliseconds;

                List<OutboxRecord> result = new List<OutboxRecord>();

                using (SqliteDataReader rdr = _database.ExecuteReader(_popAllCommand, System.Data.CommandBehavior.Default))
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


        /// <summary>
        /// Returns when the next outbox item ís scheduled to be sent. If the returned time is larger than UnixTimeUtc.Now()
        /// it is in the future and you should schedule a job to activate at that time. If null there is nothing in the outbox 
        /// to be sent.
        /// Remember to cancel any pending outbox jobs, also if you're setting a schedule.
        /// </summary>
        /// <returns>UnixTimeUtc of when the next item should be sent, null if none.</returns>
        /// <exception cref="Exception"></exception>
        public UnixTimeUtc? NextScheduledItem()
        {
            lock (_nextScheduleLock)
            {
                if (_nextScheduleCommand == null)
                {
                    _nextScheduleCommand = _database.CreateCommand();
                    _nextScheduleCommand.CommandText = "SELECT nextRunTime FROM outbox " + whereClause + ";";

                    _nextScheduleParam1 = _nextScheduleCommand.CreateParameter();

                    _nextScheduleParam1.ParameterName = "$now";
                    _nextScheduleCommand.Parameters.Add(_nextScheduleParam1);

                    _nextScheduleCommand.Prepare();
                }

                _nextScheduleParam1.Value = UnixTimeUtc.MaxTime.milliseconds;

                using (SqliteDataReader rdr = _database.ExecuteReader(_nextScheduleCommand, System.Data.CommandBehavior.Default))
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


        /// <summary>
        /// Cancels the pop of items with the 'checkOutStamp' from a previous pop operation
        /// </summary>
        /// <param name="checkOutStamp"></param>
        public void CheckInAsCancelled(Guid checkOutStamp, UnixTimeUtc nextRunTime)
        {
            lock (_outboxLock)
            {
                // Make sure we only prep once 
                if (_popCancelCommand == null)
                {
                    _popCancelCommand = _database.CreateCommand();
                    _popCancelCommand.CommandText = "UPDATE outbox SET checkOutStamp=NULL, checkOutCount=checkOutCount+1, nextRunTime=$nextRunTime WHERE checkOutStamp=$checkOutStamp";

                    _pcancelparam1 = _popCancelCommand.CreateParameter();
                    _pcancelparam2 = _popCancelCommand.CreateParameter();

                    _pcancelparam1.ParameterName = "$checkOutStamp";
                    _popCancelCommand.Parameters.Add(_pcancelparam1);
                    
                    _pcancelparam2.ParameterName = "$nextRunTime";
                    _popCancelCommand.Parameters.Add(_pcancelparam2);


                    _popCancelCommand.Prepare();
                }

                _pcancelparam1.Value = checkOutStamp.ToByteArray();
                _pcancelparam2.Value = nextRunTime.milliseconds;

                _database.ExecuteNonQuery(_popCancelCommand);
            }
        }



        /// <summary>
        /// Commits (removes) the items previously popped with the supplied 'checkOutStamp'
        /// </summary>
        /// <param name="checkOutStamp"></param>
        public void CompleteAndRemove(Guid checkOutStamp)
        {
            lock (_outboxLock)
            {
                // Make sure we only prep once 
                if (_popCommitCommand == null)
                {
                    _popCommitCommand = _database.CreateCommand();
                    _popCommitCommand.CommandText = "DELETE FROM outbox WHERE checkOutStamp=$checkOutStamp";

                    _pcommitparam1 = _popCommitCommand.CreateParameter();
                    _pcommitparam1.ParameterName = "$checkOutStamp";
                    _popCommitCommand.Parameters.Add(_pcommitparam1);

                    _popCommitCommand.Prepare();
                }

                _pcommitparam1.Value = checkOutStamp.ToByteArray();

                _database.ExecuteNonQuery(_popCommitCommand);
            }
        }



        /// <summary>
        /// Recover popped items older than the supplied UnixTime in seconds.
        /// This is how to recover popped items that were never processed for example on a server crash.
        /// Call with e.g. a time of more than 5 minutes ago.
        /// </summary>
        public void RecoverCheckedOutDeadItems(UnixTimeUtc UnixTimeSeconds)
        {
            lock (_outboxLock)
            {
                if (_popRecoverCommand == null)
                {
                    _popRecoverCommand = _database.CreateCommand();
                    _popRecoverCommand.CommandText = "UPDATE outbox SET checkOutStamp=NULL,checkOutCount=checkOutCount+1 WHERE checkOutStamp < $checkOutStamp";

                    _pcrecoverparam1 = _popRecoverCommand.CreateParameter();

                    _pcrecoverparam1.ParameterName = "$checkOutStamp";
                    _popRecoverCommand.Parameters.Add(_pcrecoverparam1);

                    _popRecoverCommand.Prepare();
                }

                _pcrecoverparam1.Value = SequentialGuid.CreateGuid(UnixTimeSeconds).ToByteArray(); // UnixTimeMiliseconds

                _database.ExecuteNonQuery(_popRecoverCommand);
            }
        }


        /// <summary>
        /// Status on the box
        /// </summary>
        /// <returns>Number of total items in box, number of popped items, the next item schduled time (ZeroTime if none)</returns>
        /// <exception cref="Exception"></exception>
        public (int totalItems, int checkedOutItems, UnixTimeUtc nextRunTime) OutboxStatus()
        {
            lock (_outboxLock)
            {
                // Make sure we only prep once 
                if (_popStatusCommand == null)
                {
                    _popStatusCommand = _database.CreateCommand();
                    _popStatusCommand.CommandText =
                        "SELECT count(*) FROM outbox;" +
                        "SELECT count(*) FROM outbox WHERE checkOutStamp NOT NULL;" +
                        "SELECT nextRunTime FROM outbox "+whereClause+";";

                    _pscParam1 = _popStatusCommand.CreateParameter();
                    _pscParam1.ParameterName = "$now";
                    _popStatusCommand.Parameters.Add(_pscParam1);

                    _popStatusCommand.Prepare();
                }

                _pscParam1.Value = UnixTimeUtc.MaxTime.milliseconds;

                using (SqliteDataReader rdr = _database.ExecuteReader(_popStatusCommand, System.Data.CommandBehavior.Default))
                {
                    // Read the total count
                    if (!rdr.Read())
                        throw new Exception("Not possible");
                    if (rdr.IsDBNull(0))
                        throw new Exception("Not possible");

                    int totalCount = rdr.GetInt32(0);

                    // Read the popped count
                    if (!rdr.NextResult())
                        throw new Exception("Not possible");

                    if (!rdr.Read())
                        throw new Exception("Not possible");
                    if (rdr.IsDBNull(0))
                        throw new Exception("Not possible");

                    int poppedCount = rdr.GetInt32(0);

                    if (!rdr.NextResult())
                        throw new Exception("Not possible");
                    // Read the marker, if any
                    if (!rdr.Read() || rdr.IsDBNull(0))
                        return (totalCount, poppedCount, UnixTimeUtc.ZeroTime);

                    Int64 t = rdr.GetInt64(0);
                    var utc = new UnixTimeUtc(t);
                    return (totalCount, poppedCount, utc);
                }
            }
        }



        /// <summary>
        /// Status on the box
        /// </summary>
        /// <returns>Number of total items in box, number of popped items, the next item schduled time (ZeroTime if none)</returns>
        /// <exception cref="Exception"></exception>
        public (int, int, UnixTimeUtc) OutboxStatusDrive(Guid driveId)
        {
            lock (_outboxLock)
            {
                // Make sure we only prep once 
                if (_popStatusSpecificBoxCommand == null)
                {
                    _popStatusSpecificBoxCommand = _database.CreateCommand();
                    _popStatusSpecificBoxCommand.CommandText =
                        "SELECT count(*) FROM outbox WHERE driveId=$driveId;" +
                        "SELECT count(*) FROM outbox WHERE driveId=$driveId AND checkOutStamp NOT NULL;" +
                        "SELECT nextRunTime FROM outbox "+whereClause+";";

                    _pssbParam1 = _popStatusSpecificBoxCommand.CreateParameter();
                    _pssbParam1.ParameterName = "$driveId";
                    _popStatusSpecificBoxCommand.Parameters.Add(_pssbParam1);

                    _pssbParam2 = _popStatusSpecificBoxCommand.CreateParameter();
                    _pssbParam2.ParameterName = "$now";
                    _popStatusSpecificBoxCommand.Parameters.Add(_pssbParam2);

                    _popStatusSpecificBoxCommand.Prepare();
                }

                _pssbParam1.Value = driveId.ToByteArray();
                _pssbParam2.Value = UnixTimeUtc.MaxTime.milliseconds;

                using (SqliteDataReader rdr = _database.ExecuteReader(_popStatusSpecificBoxCommand, System.Data.CommandBehavior.Default))
                {
                    // Read the total count
                    if (!rdr.Read())
                        throw new Exception("Not possible");
                    if (rdr.IsDBNull(0))
                        throw new Exception("Not possible");

                    int totalCount = rdr.GetInt32(0);

                    // Read the popped count
                    if (!rdr.NextResult())
                        throw new Exception("Not possible");

                    if (!rdr.Read())
                        throw new Exception("Not possible");
                    if (rdr.IsDBNull(0))
                        throw new Exception("Not possible");

                    int poppedCount = rdr.GetInt32(0);

                    if (!rdr.NextResult())
                        throw new Exception("Not possible");
                    // Read the marker, if any
                    if (!rdr.Read() || rdr.IsDBNull(0))
                        return (totalCount, poppedCount, UnixTimeUtc.ZeroTime);

                    Int64 t = rdr.GetInt64(0);
                    var utc = new UnixTimeUtc(t);
                    return (totalCount, poppedCount, utc);
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
                    _popSpecificBoxCommand = _database.CreateCommand();
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
                    _popCommitListCommand = _database.CreateCommand();
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