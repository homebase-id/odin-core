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
        private static Object _popLock = new Object();

        private SqliteCommand _popAllCommand = null;
        private SqliteParameter _paparam1 = null;
        private SqliteParameter _paparam2 = null;
        private SqliteParameter _paparam3 = null;
        private static Object _popAllLock = new Object();

        private SqliteCommand _popStatusCommand = null;

        private SqliteCommand _popStatusSpecificBoxCommand = null;
        private SqliteParameter _pssbparam1 = null;

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
                item.nextRunTime = UnixTimeUtc.Now().AddSeconds(-5);
            return base.Insert(item);
        }


        public override int Upsert(OutboxRecord item)
        {
            if (item.nextRunTime.milliseconds == 0)
                item.nextRunTime = UnixTimeUtc.Now().AddSeconds(-5);
            return base.Upsert(item);
        }

        public OutboxRecord CheckOutItem()
        {
            lock (_popAllLock)
            {
                // Make sure we only prep once 
                if (_popAllCommand == null)
                {
                    _popAllCommand = _database.CreateCommand();
                    _popAllCommand.CommandText = 
                        "UPDATE outbox "+
                        "SET checkOutStamp=$checkOutStamp "+
                        "WHERE (checkOutStamp is NULL) AND "+
                        "   fileId IN ("+
                        "      SELECT fileid "+
                        "      FROM outbox "+
                        "      WHERE "+
                        "        checkOutStamp is NULL AND "+
                        // "        nextRunTime <= $now AND "+
                        "        ((dependencyFileId IS NULL) OR " +
                        "         (NOT EXISTS (SELECT 1 FROM outbox AS ib WHERE ib.fileId = outbox.dependencyFileId AND ib.recipient = outbox.recipient)))" +
                        "      ORDER BY priority ASC, nextRunTime ASC LIMIT 1); " +
                        "SELECT rowid,driveId,fileId,recipient,type,priority,dependencyFileId,checkOutCount,nextRunTime,value,checkOutStamp,created,modified " +
                        "FROM outbox "+
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
        /// Cancels the pop of items with the 'checkOutStamp' from a previous pop operation
        /// </summary>
        /// <param name="checkOutStamp"></param>
        public void CheckInAsCancelled(Guid checkOutStamp, UnixTimeUtc nextRunTime)
        {
            lock (_popLock)
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
            lock (_popLock)
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
            lock (_popLock)
            {
                if (_popRecoverCommand == null)
                {
                    _popRecoverCommand = _database.CreateCommand();
                    _popRecoverCommand.CommandText = "UPDATE outbox SET checkOutStamp=NULL WHERE checkOutStamp < $checkOutStamp";

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
            lock (_popLock)
            {
                // Make sure we only prep once 
                if (_popStatusCommand == null)
                {
                    _popStatusCommand = _database.CreateCommand();
                    _popStatusCommand.CommandText =
                        "SELECT count(*) FROM outbox;" +
                        "SELECT count(*) FROM outbox WHERE checkOutStamp NOT NULL;" +
                        "SELECT nextRunTime FROM outbox ORDER BY nextRunTime ASC LIMIT 1;";
                    _popStatusCommand.Prepare();
                }

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
        public (int, int, UnixTimeUtc) OutboxStatusSpecificBox(Guid driveId)
        {
            lock (_popLock)
            {
                // Make sure we only prep once 
                if (_popStatusSpecificBoxCommand == null)
                {
                    _popStatusSpecificBoxCommand = _database.CreateCommand();
                    _popStatusSpecificBoxCommand.CommandText =
                        "SELECT count(*) FROM outbox WHERE driveId=$driveId;" +
                        "SELECT count(*) FROM outbox WHERE driveId=$driveId AND checkOutStamp NOT NULL;" +
                        "SELECT nextRunTime FROM outbox WHERE driveId=$driveId ORDER BY nextRunTime ASC LIMIT 1;";
                    _pssbparam1 = _popStatusSpecificBoxCommand.CreateParameter();
                    _pssbparam1.ParameterName = "$driveId";
                    _popStatusSpecificBoxCommand.Parameters.Add(_pssbparam1);

                    _popStatusSpecificBoxCommand.Prepare();
                }

                _pssbparam1.Value = driveId.ToByteArray();

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