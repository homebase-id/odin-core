using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableInbox : TableInboxCRUD
    {
        private readonly IdentityDatabase _db;

        public TableInbox(IdentityDatabase db, CacheHelper cache) : base(cache)
        {
            _db = db;
        }

        public override void Dispose()
        {
            base.Dispose();
            GC.SuppressFinalize(this);
        }

        public async Task<InboxRecord> GetAsync(Guid fileId)
        {
            using var conn = _db.CreateDisposableConnection();
            return await base.GetAsync(conn, _db._identityId, fileId);
        }

        public async Task<int> InsertAsync(InboxRecord item)
        {
            item.identityId = _db._identityId;

            if (item.timeStamp.milliseconds == 0)
                item.timeStamp = UnixTimeUtc.Now();

            using var conn = _db.CreateDisposableConnection();
            return await base.InsertAsync(conn, item);
        }

        public async Task<int> UpsertAsync(InboxRecord item)
        {
            item.identityId = _db._identityId;

            if (item.timeStamp.milliseconds == 0)
                item.timeStamp = UnixTimeUtc.Now();

            using var conn = _db.CreateDisposableConnection();
            return await base.UpsertAsync(conn, item);
        }


        /// <summary>
        /// Pops 'count' items from the table. The items remain in the DB with the 'popstamp' unique identifier.
        /// Popstamp is used by the caller to release the items when they have been successfully processed, or
        /// to cancel the transaction and restore the items to the inbox.
        /// </summary
        /// <param name="boxId">Is the box to pop from, e.g. Drive A, or App B</param>
        /// <param name="count">How many items to 'pop' (reserve)</param>
        /// <param name="popStamp">The unique identifier for the items reserved for pop</param>
        /// <returns>List of records</returns>
        public async Task<List<InboxRecord>> PopSpecificBoxAsync(Guid boxId, int count)
        {
            using (var popCommand = _db.CreateCommand())
            {
                popCommand.CommandText = "UPDATE inbox SET popstamp=$popstamp WHERE rowid IN (SELECT rowid FROM inbox WHERE identityId=$identityId AND boxId=$boxId AND popstamp IS NULL ORDER BY rowId ASC LIMIT $count); " +
                                          "SELECT identityId,fileId,boxId,priority,timeStamp,value,popStamp,created,modified FROM inbox WHERE identityId = $identityId AND popstamp=$popstamp";

                var pparam1 = popCommand.CreateParameter();
                var pparam2 = popCommand.CreateParameter();
                var pparam3 = popCommand.CreateParameter();
                var pparam4 = popCommand.CreateParameter();

                pparam1.ParameterName = "$popstamp";
                pparam2.ParameterName = "$count";
                pparam3.ParameterName = "$boxId";
                pparam4.ParameterName = "$identityId";

                popCommand.Parameters.Add(pparam1);
                popCommand.Parameters.Add(pparam2);
                popCommand.Parameters.Add(pparam3);
                popCommand.Parameters.Add(pparam4);

                pparam1.Value = SequentialGuid.CreateGuid().ToByteArray();
                pparam2.Value = count;
                pparam3.Value = boxId.ToByteArray();
                pparam4.Value = _db._identityId.ToByteArray();

                List<InboxRecord> result = new List<InboxRecord>();

                using (var conn = _db.CreateDisposableConnection())
                {
                    await conn.CreateCommitUnitOfWorkAsync(async () =>
                    {
                        using (var rdr = await conn.ExecuteReaderAsync(popCommand, System.Data.CommandBehavior.Default))
                        {
                            while (await rdr.ReadAsync())
                            {
                                result.Add(ReadRecordFromReaderAll(rdr));
                            }
                        }
                    });

                    return result;
                }
            }
        }


        /// <summary>
        /// Status on the box
        /// </summary>
        /// <returns>Number of total items in box, number of popped items, the oldest popped item (ZeroTime if none)</returns>
        /// <exception cref="Exception"></exception>
        public async Task<(int, int, UnixTimeUtc)> PopStatusAsync()
        {
            using (var popStatusCommand = _db.CreateCommand())
            {
                popStatusCommand.CommandText =
                    "SELECT count(*) FROM inbox WHERE identityId=$identityId;" +
                    "SELECT count(*) FROM inbox WHERE identityId=$identityId AND popstamp NOT NULL;" +
                    "SELECT popstamp FROM inbox WHERE identityId=$identityId ORDER BY popstamp DESC LIMIT 1;";

                var pparam1 = popStatusCommand.CreateParameter();
                pparam1.ParameterName = "$identityId";
                popStatusCommand.Parameters.Add(pparam1);
                pparam1.Value = _db._identityId.ToByteArray();

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
                            if (!rdr.IsDBNull(0))
                            {
                                var bytes = new byte[16];
                                var n = rdr.GetBytes(0, 0, bytes, 0, 16);
                                if (n != 16)
                                    throw new Exception("Invalid stamp");

                                var guid = new Guid(bytes);
                                utc = SequentialGuid.ToUnixTimeUtc(guid);
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
        /// <returns>Number of total items in box, number of popped items, the oldest popped item (ZeroTime if none)</returns>
        /// <exception cref="Exception"></exception>
        public async Task<(int totalCount, int poppedCount, UnixTimeUtc oldestItemTime)> PopStatusSpecificBoxAsync(Guid boxId)
        {
            using (var popStatusSpecificBoxCommand = _db.CreateCommand())
            {
                popStatusSpecificBoxCommand.CommandText =
                    "SELECT count(*) FROM inbox WHERE identityId=$identityId AND boxId=$boxId;" +
                    "SELECT count(*) FROM inbox WHERE identityId=$identityId AND boxId=$boxId AND popstamp NOT NULL;" +
                    "SELECT popstamp FROM inbox WHERE identityId=$identityId AND boxId=$boxId ORDER BY popstamp DESC LIMIT 1;";
                var pssbparam1 = popStatusSpecificBoxCommand.CreateParameter();
                var pssbparam2 = popStatusSpecificBoxCommand.CreateParameter();

                pssbparam1.ParameterName = "$boxId";
                pssbparam2.ParameterName = "$identityId";

                popStatusSpecificBoxCommand.Parameters.Add(pssbparam1);
                popStatusSpecificBoxCommand.Parameters.Add(pssbparam2);

                pssbparam1.Value = boxId.ToByteArray();
                pssbparam2.Value = _db._identityId.ToByteArray();

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

                        if (await rdr.NextResultAsync() == false)
                            throw new Exception("Not possible");

                        var utc = UnixTimeUtc.ZeroTime;

                        // Read the marker, if any
                        if (await rdr.ReadAsync())
                        {
                            if (!rdr.IsDBNull(0))
                            {
                                var bytes = new byte[16];
                                var n = rdr.GetBytes(0, 0, bytes, 0, 16);
                                if (n != 16)
                                    throw new Exception("Invalid stamp");

                                var guid = new Guid(bytes);
                                utc = SequentialGuid.ToUnixTimeUtc(guid);
                            }
                        }
                        return (totalCount, poppedCount, utc);
                    }
                }
            }
        }



        /// <summary>
        /// Cancels the pop of items with the 'popstamp' from a previous pop operation
        /// </summary>
        /// <param name="popstamp"></param>
        public async Task<int> PopCancelAllAsync(Guid popstamp)
        {
            using (var popCancelCommand = _db.CreateCommand())
            {
                popCancelCommand.CommandText = "UPDATE inbox SET popstamp=NULL WHERE identityId=$identityId AND popstamp=$popstamp";

                var pcancelparam1 = popCancelCommand.CreateParameter();
                var pcancelparam2 = popCancelCommand.CreateParameter();

                pcancelparam1.ParameterName = "$popstamp";
                pcancelparam2.ParameterName = "$identityId";

                popCancelCommand.Parameters.Add(pcancelparam1);
                popCancelCommand.Parameters.Add(pcancelparam2);

                pcancelparam1.Value = popstamp.ToByteArray();
                pcancelparam2.Value = _db._identityId.ToByteArray();

                using (var conn = _db.CreateDisposableConnection())
                {
                    return await conn.ExecuteNonQueryAsync(popCancelCommand);
                }
            }
        }

        public async Task PopCancelListAsync(Guid popstamp, Guid driveId, List<Guid> listFileId)
        {
            using (var popCancelListCommand = _db.CreateCommand())
            {
                popCancelListCommand.CommandText = "UPDATE inbox SET popstamp=NULL WHERE identityId=$identityId AND fileid=$fileid AND popstamp=$popstamp";

                var pcancellistparam1 = popCancelListCommand.CreateParameter();
                var pcancellistparam2 = popCancelListCommand.CreateParameter();
                var pcancellistparam3 = popCancelListCommand.CreateParameter();

                pcancellistparam1.ParameterName = "$popstamp";
                pcancellistparam2.ParameterName = "$fileid";
                pcancellistparam3.ParameterName = "$identityId";

                popCancelListCommand.Parameters.Add(pcancellistparam1);
                popCancelListCommand.Parameters.Add(pcancellistparam2);
                popCancelListCommand.Parameters.Add(pcancellistparam3);

                pcancellistparam1.Value = popstamp.ToByteArray();
                pcancellistparam3.Value = _db._identityId.ToByteArray();

                using (var conn = _db.CreateDisposableConnection())
                {
                    await conn.CreateCommitUnitOfWorkAsync(async () =>
                    {
                        for (int i = 0; i < listFileId.Count; i++)
                        {
                            pcancellistparam2.Value = listFileId[i].ToByteArray();
                            await conn.ExecuteNonQueryAsync(popCancelListCommand);
                        }
                    });
                }
            }
        }


        /// <summary>
        /// Commits (removes) the items previously popped with the supplied 'popstamp'
        /// </summary>
        /// <param name="popstamp"></param>
        public async Task<int> PopCommitAllAsync(Guid popstamp)
        {
            using (var popCommitCommand = _db.CreateCommand())
            {
                popCommitCommand.CommandText = "DELETE FROM inbox WHERE identityId=$identityId AND popstamp=$popstamp";

                var pcommitparam1 = popCommitCommand.CreateParameter();
                var pcommitparam2 = popCommitCommand.CreateParameter();

                pcommitparam1.ParameterName = "$popstamp";
                pcommitparam2.ParameterName = "$identityId";

                popCommitCommand.Parameters.Add(pcommitparam1);
                popCommitCommand.Parameters.Add(pcommitparam2);

                pcommitparam1.Value = popstamp.ToByteArray();
                pcommitparam2.Value = _db._identityId.ToByteArray();

                using (var conn = _db.CreateDisposableConnection())
                {
                    return await conn.ExecuteNonQueryAsync(popCommitCommand);
                }
            }
        }


        /// <summary>
        /// Commits (removes) the items previously popped with the supplied 'popstamp'
        /// </summary>
        /// <param name="popstamp"></param>
        public async Task PopCommitListAsync(Guid popstamp, Guid driveId, List<Guid> listFileId)
        {
            using (var popCommitListCommand = _db.CreateCommand())
            {
                popCommitListCommand.CommandText = "DELETE FROM inbox WHERE identityId=$identityId AND fileid=$fileid AND popstamp=$popstamp";

                var pcommitlistparam1 = popCommitListCommand.CreateParameter();
                var pcommitlistparam2 = popCommitListCommand.CreateParameter();
                var pcommitlistparam3 = popCommitListCommand.CreateParameter();

                pcommitlistparam1.ParameterName = "$popstamp";
                pcommitlistparam2.ParameterName = "$fileid";
                pcommitlistparam3.ParameterName = "$identityId";

                popCommitListCommand.Parameters.Add(pcommitlistparam1);
                popCommitListCommand.Parameters.Add(pcommitlistparam2);
                popCommitListCommand.Parameters.Add(pcommitlistparam3);

                pcommitlistparam1.Value = popstamp.ToByteArray();
                pcommitlistparam3.Value = _db._identityId.ToByteArray();

                using (var conn = _db.CreateDisposableConnection())
                {
                    await conn.CreateCommitUnitOfWorkAsync(async () =>
                    {
                        // I'd rather not do a TEXT statement, this seems safer but slower.
                        for (int i = 0; i < listFileId.Count; i++)
                        {
                            pcommitlistparam2.Value = listFileId[i].ToByteArray();
                            await conn.ExecuteNonQueryAsync(popCommitListCommand);
                        }
                    });
                }
            }
        }


        /// <summary>
        /// Recover popped items older than the supplied UnixTime in seconds.
        /// This is how to recover popped items that were never processed for example on a server crash.
        /// Call with e.g. a time of more than 5 minutes ago.
        /// </summary>
        public async Task<int> PopRecoverDeadAsync(UnixTimeUtc ut)
        {
            using (var popRecoverCommand = _db.CreateCommand())
            {
                popRecoverCommand.CommandText = "UPDATE inbox SET popstamp=NULL WHERE identityId=$identityId AND popstamp < $popstamp";

                var pcrecoverparam1 = popRecoverCommand.CreateParameter();
                var pcrecoverparam2 = popRecoverCommand.CreateParameter();

                pcrecoverparam1.ParameterName = "$popstamp";
                pcrecoverparam2.ParameterName = "$identityId";

                popRecoverCommand.Parameters.Add(pcrecoverparam1);
                popRecoverCommand.Parameters.Add(pcrecoverparam2);

                pcrecoverparam1.Value = SequentialGuid.CreateGuid(new UnixTimeUtc(ut)).ToByteArray(); // UnixTimeMilliseconds
                pcrecoverparam2.Value = _db._identityId.ToByteArray();

                using (var conn = _db.CreateDisposableConnection())
                {
                    return await conn.ExecuteNonQueryAsync(popRecoverCommand);
                }
            }
        }
    }
}