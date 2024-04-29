using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;

namespace Odin.Core.Storage.SQLite.ServerDatabase
{
    public class TableCron : TableCronCRUD
    {
        public TableCron(ServerDatabase db, CacheHelper cache) : base(db, cache)
        {
        }

        ~TableCron()
        {
        }

        public override void Dispose()
        {
            base.Dispose();
            GC.SuppressFinalize(this);
        }

        public override int Insert(DatabaseBase.DatabaseConnection conn, CronRecord item)
        {
            // If no nextRun has been set, presume it is 'now'
            if (item.nextRun.milliseconds == 0)
                item.nextRun = UnixTimeUtc.Now();
            return base.Upsert(conn, item);
        }

        public override int Upsert(DatabaseBase.DatabaseConnection conn, CronRecord item)
        {
            // If no nextRun has been set, presume it is 'now'
            if (item.nextRun.milliseconds == 0)
                item.nextRun = UnixTimeUtc.Now();
            return base.Upsert(conn, item);
        }


        /// <summary>
        /// Pops 'count' items from the table. The items remain in the DB with the 'popstamp' unique identifier.
        /// Popstamp is used by the caller to release the items when they have been successfully processed, or
        /// to cancel the transaction and restore the items to the cron.
        /// </summary
        /// <param name="boxId">Is the cron to pop from, e.g. Drive A, or App B</param>
        /// <param name="count">How many items to 'pop' (reserve)</param>
        /// <param name="popStamp">The unique identifier for the items reserved for pop</param>
        /// <returns></returns>
        public List<CronRecord> Pop(DatabaseBase.DatabaseConnection conn, int count)
        {
            using (var _popCommand = _database.CreateCommand())
            {
                _popCommand.CommandText =
                    "UPDATE cron SET popstamp=$popstamp, runcount=runcount+1, nextRun = 1000 * (60 * (runcount+1)) + unixepoch() " +
                    "WHERE rowid IN (SELECT rowid FROM cron WHERE (popstamp IS NULL) ORDER BY nextrun ASC LIMIT $count); " +
                    "SELECT identityId,type,data,runCount,nextRun,lastRun,popStamp,created,modified FROM cron WHERE popstamp=$popstamp";

                var _pparam1 = _popCommand.CreateParameter();
                _pparam1.ParameterName = "$popstamp";
                _popCommand.Parameters.Add(_pparam1);

                var _pparam2 = _popCommand.CreateParameter();
                _pparam2.ParameterName = "$count";
                _popCommand.Parameters.Add(_pparam2);

                _pparam1.Value = SequentialGuid.CreateGuid().ToByteArray();
                _pparam2.Value = count;

                List<CronRecord> result = new List<CronRecord>();

                lock (conn._lock)
                {
                    using (SqliteDataReader rdr = _database.ExecuteReader(conn, _popCommand, System.Data.CommandBehavior.Default))
                    {
                        while (rdr.Read())
                        {
                            result.Add(ReadRecordFromReaderAll(rdr));
                        }
                        return result;
                    }
                }
            }
        }



        /// <summary>
        /// The List<> of identityIds will be unpopped, i.e. they're back in the CRON table and are active
        /// </summary>
        /// <param name="listIdentityId"></param>
        public void PopCancelList(DatabaseBase.DatabaseConnection conn, List<Guid> listIdentityId)
        {
            using (var _popCancelListCommand = _database.CreateCommand())
            {
                _popCancelListCommand.CommandText = "UPDATE cron SET popstamp=NULL WHERE identityid=$identityid";

                var _pcancellistparam1 = _popCancelListCommand.CreateParameter();
                _pcancellistparam1.ParameterName = "$identityid";
                _popCancelListCommand.Parameters.Add(_pcancellistparam1);

                lock (conn._lock)
                {
                    using (conn.CreateCommitUnitOfWork())
                    {
                        // I'd rather not do a TEXT statement, this seems safer but slower.
                        for (int i = 0; i < listIdentityId.Count; i++)
                        {
                            _pcancellistparam1.Value = listIdentityId[i].ToByteArray();
                            _database.ExecuteNonQuery(conn, _popCancelListCommand);
                        }
                    }
                }
            }
        }


        /// <summary>
        /// Finally commits (removes) the items previously popped using Pop()
        /// </summary>
        public void PopCommitList(DatabaseBase.DatabaseConnection conn, List<Guid> listIdentityId)
        {
            using (var _popCommitListCommand = _database.CreateCommand())
            {
                _popCommitListCommand.CommandText = "DELETE FROM cron WHERE identityid=$identityid";

                var _pcommitlistparam1 = _popCommitListCommand.CreateParameter();
                _pcommitlistparam1.ParameterName = "$identityid";
                _popCommitListCommand.Parameters.Add(_pcommitlistparam1);

                lock (conn._lock)
                {
                    using (conn.CreateCommitUnitOfWork())
                    {
                        // I'd rather not do a TEXT statement, this seems safer but slower.
                        for (int i = 0; i < listIdentityId.Count; i++)
                        {
                            _pcommitlistparam1.Value = listIdentityId[i].ToByteArray();
                            _database.ExecuteNonQuery(conn, _popCommitListCommand);
                        }
                    }
                }
            }
        }


        /// <summary>
        /// Recover popped items older than the supplied UnixTime in seconds.
        /// This is how to recover popped items that were never processed for example on a server crash.
        /// Call with e.g. a time of more than 5 minutes ago.
        /// </summary>
        public void PopRecoverDead(DatabaseBase.DatabaseConnection conn, UnixTimeUtc t)
        {
            using (var _popRecoverCommand = _database.CreateCommand())
            {
                _popRecoverCommand.CommandText = "UPDATE cron SET popstamp=NULL WHERE popstamp <= $popstamp";

                var _pcrecoverparam1 = _popRecoverCommand.CreateParameter();
                _pcrecoverparam1.ParameterName = "$popstamp";
                _popRecoverCommand.Parameters.Add(_pcrecoverparam1);

                _pcrecoverparam1.Value = SequentialGuid.CreateGuid(t).ToByteArray();

                _database.ExecuteNonQuery(conn, _popRecoverCommand);
            }
        }
    }
}
