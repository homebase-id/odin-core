using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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

        public override void Dispose()
        {
            base.Dispose();
            GC.SuppressFinalize(this);
        }

        public async Task<List<OutboxRecord>> GetAsync(Guid driveId, Guid fileId)
        {
            using var conn = _db.CreateDisposableConnection();
            return await base.GetAsync(conn, _db._identityId, driveId, fileId);
        }

        public async Task<OutboxRecord> GetAsync(Guid driveId, Guid fileId, string recipient)
        {
            using var conn = _db.CreateDisposableConnection();
            return await base.GetAsync(conn, _db._identityId, driveId, fileId, recipient);
        }

        public async Task<int> InsertAsync(OutboxRecord item)
        {
            item.identityId = _db._identityId;
            item.checkOutCount = 0;
            if (item.nextRunTime.milliseconds == 0)
                item.nextRunTime = UnixTimeUtc.Now();
            if (ByteArrayUtil.muidcmp(item.fileId, item.dependencyFileId) == 0)
                throw new Exception("You're not allowed to make an item dependent on itself as it would deadlock the item.");

            using var conn = _db.CreateDisposableConnection();
            return await base.InsertAsync(conn, item);
        }


        public async Task<int> UpsertAsync(OutboxRecord item)
        {
            if (ByteArrayUtil.muidcmp(item.fileId, item.dependencyFileId) == 0)
                throw new Exception("You're not allowed to make an item dependent on itself as it would deadlock the item.");

            item.identityId = _db._identityId;
            if (item.nextRunTime.milliseconds == 0)
                item.nextRunTime = UnixTimeUtc.Now();

            using var conn = _db.CreateDisposableConnection();
            return await base.UpsertAsync(conn, item);
        }


        /// <summary>
        /// Will check out the next item to process. Will not check out items scheduled for the future
        /// or items that have unresolved dependencies.
        /// </summary>
        /// <returns></returns>
        public async Task<OutboxRecord> CheckOutItemAsync()
        {
            using (var popAllCommand = _db.CreateCommand())
            {
                popAllCommand.CommandText = """
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
                var paparam1 = popAllCommand.CreateParameter();
                var paparam2 = popAllCommand.CreateParameter();
                var paparam3 = popAllCommand.CreateParameter();

                paparam1.ParameterName = "$checkOutStamp";
                paparam2.ParameterName = "$identityId";
                paparam3.ParameterName = "$now";

                popAllCommand.Parameters.Add(paparam1);
                popAllCommand.Parameters.Add(paparam2);
                popAllCommand.Parameters.Add(paparam3);

                paparam1.Value = SequentialGuid.CreateGuid().ToByteArray();
                paparam2.Value = _db._identityId.ToByteArray();
                paparam3.Value = UnixTimeUtc.Now().milliseconds;

                List<OutboxRecord> result = new List<OutboxRecord>();

                using (var conn = _db.CreateDisposableConnection())
                {
                    using (var rdr = await conn.ExecuteReaderAsync(popAllCommand, System.Data.CommandBehavior.Default))
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
        public async Task<UnixTimeUtc?> NextScheduledItemAsync()
        {
            using (var nextScheduleCommand = _db.CreateCommand())
            {
                nextScheduleCommand.CommandText = "SELECT nextRunTime FROM outbox WHERE identityId=$identityId AND checkOutStamp IS NULL ORDER BY nextRunTime ASC LIMIT 1;";

                var paparam1 = nextScheduleCommand.CreateParameter();
                paparam1.ParameterName = "$identityId";
                nextScheduleCommand.Parameters.Add(paparam1);

                paparam1.Value = _db._identityId.ToByteArray();

                using (var conn = _db.CreateDisposableConnection())
                {
                    using (var rdr = await conn.ExecuteReaderAsync(nextScheduleCommand, System.Data.CommandBehavior.Default))
                    {
                        // Read the total count
                        if (await rdr.ReadAsync() == false)
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
        public async Task<int> CheckInAsCancelledAsync(Guid checkOutStamp, UnixTimeUtc nextRunTime)
        {
            using (var popCancelCommand = _db.CreateCommand())
            {
                popCancelCommand.CommandText = "UPDATE outbox SET checkOutStamp=NULL, checkOutCount=checkOutCount+1, nextRunTime=$nextRunTime WHERE identityId=$identityId AND checkOutStamp=$checkOutStamp";

                var pcancelparam1 = popCancelCommand.CreateParameter();
                var pcancelparam2 = popCancelCommand.CreateParameter();
                var pcancelparam3 = popCancelCommand.CreateParameter();

                pcancelparam1.ParameterName = "$checkOutStamp";
                pcancelparam2.ParameterName = "$nextRunTime";
                pcancelparam3.ParameterName = "$identityId";

                popCancelCommand.Parameters.Add(pcancelparam1);
                popCancelCommand.Parameters.Add(pcancelparam2);
                popCancelCommand.Parameters.Add(pcancelparam3);

                pcancelparam1.Value = checkOutStamp.ToByteArray();
                pcancelparam2.Value = nextRunTime.milliseconds;
                pcancelparam3.Value = _db._identityId.ToByteArray();

                using (var conn = _db.CreateDisposableConnection())
                {
                    return await conn.ExecuteNonQueryAsync(popCancelCommand);
                }
            }
        }



        /// <summary>
        /// Commits (removes) the items previously popped with the supplied 'checkOutStamp'
        /// </summary>
        /// <param name="checkOutStamp"></param>
        public async Task<int> CompleteAndRemoveAsync(Guid checkOutStamp)
        {
            using (var popCommitCommand = _db.CreateCommand())
            {
                popCommitCommand.CommandText = "DELETE FROM outbox WHERE identityId=$identityId AND checkOutStamp=$checkOutStamp";

                var pcommitparam1 = popCommitCommand.CreateParameter();
                var pcommitparam2 = popCommitCommand.CreateParameter();

                pcommitparam1.ParameterName = "$checkOutStamp";
                pcommitparam2.ParameterName = "$identityId";

                popCommitCommand.Parameters.Add(pcommitparam1);
                popCommitCommand.Parameters.Add(pcommitparam2);

                pcommitparam1.Value = checkOutStamp.ToByteArray();
                pcommitparam2.Value = _db._identityId.ToByteArray();

                using (var conn = _db.CreateDisposableConnection())
                {
                    return await conn.ExecuteNonQueryAsync(popCommitCommand);
                }
            }
        }



        /// <summary>
        /// Recover popped items older than the supplied UnixTime in seconds.
        /// This is how to recover popped items that were never processed for example on a server crash.
        /// Call with e.g. a time of more than 5 minutes ago.
        /// </summary>
        public async Task<int> RecoverCheckedOutDeadItemsAsync(UnixTimeUtc pastThreshold)
        {
            using (var popRecoverCommand = _db.CreateCommand())
            {
                popRecoverCommand.CommandText = "UPDATE outbox SET checkOutStamp=NULL,checkOutCount=checkOutCount+1 WHERE identityId=$identityId AND checkOutStamp < $checkOutStamp";

                // Should we also reset nextRunTime =$nextRunTime to "now()" or 0?
                //
                // Consider removing any items with checkOutCount == 0 older than X
                // since they are probably circular dependencies

                var pcrecoverparam1 = popRecoverCommand.CreateParameter();
                var pcrecoverparam2 = popRecoverCommand.CreateParameter();

                pcrecoverparam1.ParameterName = "$checkOutStamp";
                pcrecoverparam2.ParameterName = "$identityId";

                popRecoverCommand.Parameters.Add(pcrecoverparam1);
                popRecoverCommand.Parameters.Add(pcrecoverparam2);

                pcrecoverparam1.Value = SequentialGuid.CreateGuid(pastThreshold).ToByteArray(); // UnixTimeMiliseconds
                pcrecoverparam2.Value = _db._identityId.ToByteArray();

                using (var conn = _db.CreateDisposableConnection())
                {
                    return await conn.ExecuteNonQueryAsync(popRecoverCommand);
                }
            }
        }


        /// <summary>
        /// Status on the box
        /// </summary>
        /// <returns>Number of total items in box, number of popped items, the next item schduled time (ZeroTime if none)</returns>
        /// <exception cref="Exception"></exception>
        public async Task<(int totalItems, int checkedOutItems, UnixTimeUtc nextRunTime)> OutboxStatusAsync()
        {
            using (var popStatusCommand = _db.CreateCommand())
            {
                popStatusCommand.CommandText =
                    "SELECT count(*) FROM outbox WHERE identityId=$identityId;" +
                    "SELECT count(*) FROM outbox WHERE identityId=$identityId AND checkOutStamp NOT NULL;" +
                    "SELECT nextRunTime FROM outbox WHERE identityId=$identityId AND checkOutStamp IS NULL ORDER BY nextRunTime ASC LIMIT 1;";

                var pcrecoverparam1 = popStatusCommand.CreateParameter();
                pcrecoverparam1.ParameterName = "$identityId";
                popStatusCommand.Parameters.Add(pcrecoverparam1);
                pcrecoverparam1.Value = _db._identityId.ToByteArray();

                using (var conn = _db.CreateDisposableConnection())
                {
                    using (var rdr = await conn.ExecuteReaderAsync(popStatusCommand, System.Data.CommandBehavior.Default))
                    {
                        // Read the total count
                        if (await rdr.ReadAsync() == false)
                            throw new Exception("Not possible");

                        int totalCount = 0;
                        if (!rdr.IsDBNull(0))
                            totalCount = rdr.GetInt32(0);

                        // Read the popped count
                        if (await rdr.NextResultAsync() == false)
                            throw new Exception("Not possible");
                        if (await rdr.ReadAsync() == false)
                            throw new Exception("Not possible");

                        int poppedCount = 0;
                        if (!rdr.IsDBNull(0))
                            poppedCount = rdr.GetInt32(0);

                        if (await rdr.NextResultAsync() == false)
                            throw new Exception("Not possible");

                        var utc = UnixTimeUtc.ZeroTime;
                        if (await rdr.ReadAsync())
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
        public async Task<(int, int, UnixTimeUtc)> OutboxStatusDriveAsync(Guid driveId)
        {
            using (var popStatusSpecificBoxCommand = _db.CreateCommand())
            {
                popStatusSpecificBoxCommand.CommandText =
                    "SELECT count(*) FROM outbox WHERE identityId=$identityId AND driveId=$driveId;" +
                    "SELECT count(*) FROM outbox WHERE identityId=$identityId AND driveId=$driveId AND checkOutStamp NOT NULL;" +
                    "SELECT nextRunTime FROM outbox WHERE identityId=$identityId AND driveId=$driveId AND checkOutStamp IS NULL ORDER BY nextRunTime ASC LIMIT 1;";

                var pssbParam1 = popStatusSpecificBoxCommand.CreateParameter();
                var pssbParam2 = popStatusSpecificBoxCommand.CreateParameter();

                pssbParam1.ParameterName = "$driveId";
                pssbParam2.ParameterName = "$identityId";

                popStatusSpecificBoxCommand.Parameters.Add(pssbParam1);
                popStatusSpecificBoxCommand.Parameters.Add(pssbParam2);

                pssbParam1.Value = driveId.ToByteArray();
                pssbParam2.Value = _db._identityId.ToByteArray();

                using (var conn = _db.CreateDisposableConnection())
                {
                    using (var rdr = await conn.ExecuteReaderAsync(popStatusSpecificBoxCommand, System.Data.CommandBehavior.Default))
                    {
                        // Read the total count
                        if (await rdr.ReadAsync() == false)
                            throw new Exception("Not possible");

                        int totalCount = 0;
                        if (!rdr.IsDBNull(0))
                            totalCount = rdr.GetInt32(0);

                        // Read the popped count
                        if (await rdr.NextResultAsync() == false)
                            throw new Exception("Not possible");
                        if (await rdr.ReadAsync() == false)
                            throw new Exception("Not possible");

                        int poppedCount = 0;
                        if (!rdr.IsDBNull(0))
                            poppedCount = rdr.GetInt32(0);

                        if (await rdr.ReadAsync() == false)
                            throw new Exception("Not possible");

                        var utc = UnixTimeUtc.ZeroTime;
                        if (await rdr.ReadAsync())
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
