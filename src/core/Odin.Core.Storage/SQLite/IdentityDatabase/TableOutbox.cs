using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableOutbox : TableOutboxCRUD
    {
        private readonly IdentityDatabase _db;

        public TableOutbox(IdentityDatabase db, CacheHelper cache) : base(cache)
        {
            _db = db;
        }

        ~TableOutbox()
        {
        }

        public override void Dispose()
        {
            base.Dispose();
            GC.SuppressFinalize(this);
        }

        public List<OutboxRecord> Get(Guid driveId, Guid fileId)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.Get(conn, _db._identityId, driveId, fileId);
            }
        }

        public OutboxRecord Get(Guid driveId, Guid fileId, string recipient)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.Get(conn, _db._identityId, driveId, fileId, recipient);
            }
        }

        public int Insert(OutboxRecord item)
        {
            item.identityId = _db._identityId;
            item.checkOutCount = 0;
            if (item.nextRunTime.milliseconds == 0)
                item.nextRunTime = UnixTimeUtc.Now();
            if (ByteArrayUtil.muidcmp(item.fileId, item.dependencyFileId) == 0)
                throw new Exception("You're not allowed to make an item dependent on itself as it would deadlock the item.");

            using (var conn = _db.CreateDisposableConnection())
            {
                return base.Insert(conn, item);
            }
        }


        public int Upsert(OutboxRecord item)
        {
            if (ByteArrayUtil.muidcmp(item.fileId, item.dependencyFileId) == 0)
                throw new Exception("You're not allowed to make an item dependent on itself as it would deadlock the item.");

            item.identityId = _db._identityId;
            if (item.nextRunTime.milliseconds == 0)
                item.nextRunTime = UnixTimeUtc.Now();

            using (var conn = _db.CreateDisposableConnection())
            {
                return base.Upsert(conn, item);
            }
        }


        /// <summary>
        /// Will check out the next item to process. Will not check out items scheduled for the future
        /// or items that have unresolved dependencies.
        /// </summary>
        /// <returns></returns>
        public OutboxRecord CheckOutItem()
        {
            using (var _popAllCommand = _db.CreateCommand())
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
                _paparam2.Value = _db._identityId.ToByteArray();
                _paparam3.Value = UnixTimeUtc.Now().milliseconds;

                List<OutboxRecord> result = new List<OutboxRecord>();

                using (var conn = _db.CreateDisposableConnection())
                {
                    using (var rdr = conn.ExecuteReader(_popAllCommand, System.Data.CommandBehavior.Default))
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
        public UnixTimeUtc? NextScheduledItem()
        {
            using (var _nextScheduleCommand = _db.CreateCommand())
            {
                _nextScheduleCommand.CommandText = "SELECT nextRunTime FROM outbox WHERE identityId=$identityId AND checkOutStamp IS NULL ORDER BY nextRunTime ASC LIMIT 1;";

                var _paparam1 = _nextScheduleCommand.CreateParameter();
                _paparam1.ParameterName = "$identityId";
                _nextScheduleCommand.Parameters.Add(_paparam1);

                _paparam1.Value = _db._identityId.ToByteArray();

                using (var conn = _db.CreateDisposableConnection())
                {
                    using (var rdr = conn.ExecuteReader(_nextScheduleCommand, System.Data.CommandBehavior.Default))
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
        public int CheckInAsCancelled(Guid checkOutStamp, UnixTimeUtc nextRunTime)
        {
            using (var _popCancelCommand = _db.CreateCommand())
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
                _pcancelparam3.Value = _db._identityId.ToByteArray();

                using (var conn = _db.CreateDisposableConnection())
                {
                    return conn.ExecuteNonQuery(_popCancelCommand);
                }
            }
        }



        /// <summary>
        /// Commits (removes) the items previously popped with the supplied 'checkOutStamp'
        /// </summary>
        /// <param name="checkOutStamp"></param>
        public int CompleteAndRemove(Guid checkOutStamp)
        {
            using (var _popCommitCommand = _db.CreateCommand())
            {
                _popCommitCommand.CommandText = "DELETE FROM outbox WHERE identityId=$identityId AND checkOutStamp=$checkOutStamp";

                var _pcommitparam1 = _popCommitCommand.CreateParameter();
                var _pcommitparam2 = _popCommitCommand.CreateParameter();

                _pcommitparam1.ParameterName = "$checkOutStamp";
                _pcommitparam2.ParameterName = "$identityId";

                _popCommitCommand.Parameters.Add(_pcommitparam1);
                _popCommitCommand.Parameters.Add(_pcommitparam2);

                _pcommitparam1.Value = checkOutStamp.ToByteArray();
                _pcommitparam2.Value = _db._identityId.ToByteArray();

                using (var conn = _db.CreateDisposableConnection())
                {
                    return conn.ExecuteNonQuery(_popCommitCommand);
                }
            }
        }



        /// <summary>
        /// Recover popped items older than the supplied UnixTime in seconds.
        /// This is how to recover popped items that were never processed for example on a server crash.
        /// Call with e.g. a time of more than 5 minutes ago.
        /// </summary>
        public int RecoverCheckedOutDeadItems(UnixTimeUtc pastThreshold)
        {
            using (var _popRecoverCommand = _db.CreateCommand())
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
                _pcrecoverparam2.Value = _db._identityId.ToByteArray();

                using (var conn = _db.CreateDisposableConnection())
                {
                    return conn.ExecuteNonQuery(_popRecoverCommand);
                }
            }
        }


        /// <summary>
        /// Status on the box
        /// </summary>
        /// <returns>Number of total items in box, number of popped items, the next item schduled time (ZeroTime if none)</returns>
        /// <exception cref="Exception"></exception>
        public (int totalItems, int checkedOutItems, UnixTimeUtc nextRunTime) OutboxStatus()
        {
            using (var _popStatusCommand = _db.CreateCommand())
            {
                _popStatusCommand.CommandText =
                    "SELECT count(*) FROM outbox WHERE identityId=$identityId;" +
                    "SELECT count(*) FROM outbox WHERE identityId=$identityId AND checkOutStamp NOT NULL;" +
                    "SELECT nextRunTime FROM outbox WHERE identityId=$identityId AND checkOutStamp IS NULL ORDER BY nextRunTime ASC LIMIT 1;";

                var _pcrecoverparam1 = _popStatusCommand.CreateParameter();
                _pcrecoverparam1.ParameterName = "$identityId";
                _popStatusCommand.Parameters.Add(_pcrecoverparam1);
                _pcrecoverparam1.Value = _db._identityId.ToByteArray();

                using (var conn = _db.CreateDisposableConnection())
                {
                    using (var rdr = conn.ExecuteReader(_popStatusCommand, System.Data.CommandBehavior.Default))
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
        public (int, int, UnixTimeUtc) OutboxStatusDrive(Guid driveId)
        {
            using (var _popStatusSpecificBoxCommand = _db.CreateCommand())
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
                _pssbParam2.Value = _db._identityId.ToByteArray();

                using (var conn = _db.CreateDisposableConnection())
                {
                    using (var rdr = conn.ExecuteReader(_popStatusSpecificBoxCommand, System.Data.CommandBehavior.Default))
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
