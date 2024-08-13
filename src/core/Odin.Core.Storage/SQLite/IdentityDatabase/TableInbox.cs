using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableInbox : TableInboxCRUD
    {
        public TableInbox(IdentityDatabase db, CacheHelper cache) : base(db, cache)
        {
        }

        ~TableInbox()
        {
        }

        public override void Dispose()
        {
            base.Dispose();
            GC.SuppressFinalize(this);
        }

        public InboxRecord Get(DatabaseConnection conn, Guid fileId)
        {
            return base.Get(conn, ((IdentityDatabase)conn.db)._identityId, fileId);
        }

        public new int Insert(DatabaseConnection conn, InboxRecord item)
        {
            item.identityId = ((IdentityDatabase)conn.db)._identityId;

            if (item.timeStamp.milliseconds == 0)
                item.timeStamp = UnixTimeUtc.Now();

            return base.Insert(conn, item);
        }

        public new int Upsert(DatabaseConnection conn, InboxRecord item)
        {
            item.identityId = ((IdentityDatabase)conn.db)._identityId;

            if (item.timeStamp.milliseconds == 0)
                item.timeStamp = UnixTimeUtc.Now();

            return base.Upsert(conn, item);
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
        public List<InboxRecord> PopSpecificBox(DatabaseConnection conn, Guid boxId, int count)
        {
            using (var _popCommand = _database.CreateCommand())
            {
                _popCommand.CommandText = "UPDATE inbox SET popstamp=$popstamp WHERE rowid IN (SELECT rowid FROM inbox WHERE identityId=$identityId AND boxId=$boxId AND popstamp IS NULL ORDER BY rowId ASC LIMIT $count); " +
                                          "SELECT identityId,fileId,boxId,priority,timeStamp,value,popStamp,created,modified FROM inbox WHERE identityId = $identityId AND popstamp=$popstamp";

                var _pparam1 = _popCommand.CreateParameter();
                var _pparam2 = _popCommand.CreateParameter();
                var _pparam3 = _popCommand.CreateParameter();
                var _pparam4 = _popCommand.CreateParameter();

                _pparam1.ParameterName = "$popstamp";
                _pparam2.ParameterName = "$count";
                _pparam3.ParameterName = "$boxId";
                _pparam4.ParameterName = "$identityId";

                _popCommand.Parameters.Add(_pparam1);
                _popCommand.Parameters.Add(_pparam2);
                _popCommand.Parameters.Add(_pparam3);
                _popCommand.Parameters.Add(_pparam4);

                _pparam1.Value = SequentialGuid.CreateGuid().ToByteArray();
                _pparam2.Value = count;
                _pparam3.Value = boxId.ToByteArray();
                _pparam4.Value = ((IdentityDatabase)conn.db)._identityId.ToByteArray();

                List<InboxRecord> result = new List<InboxRecord>();

                conn.CreateCommitUnitOfWork(() =>
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_popCommand, System.Data.CommandBehavior.Default))
                    {
                        while (rdr.Read())
                        {
                            result.Add(ReadRecordFromReaderAll(rdr));
                        }
                    }
                });
                return result;
            }
        }


        /// <summary>
        /// Status on the box
        /// </summary>
        /// <returns>Number of total items in box, number of popped items, the oldest popped item (ZeroTime if none)</returns>
        /// <exception cref="Exception"></exception>
        public (int, int, UnixTimeUtc) PopStatus(DatabaseConnection conn)
        {
            using (var _popStatusCommand = _database.CreateCommand())
            {
                _popStatusCommand.CommandText =
                    "SELECT count(*) FROM inbox WHERE identityId=$identityId;" +
                    "SELECT count(*) FROM inbox WHERE identityId=$identityId AND popstamp NOT NULL;" +
                    "SELECT popstamp FROM inbox WHERE identityId=$identityId ORDER BY popstamp DESC LIMIT 1;";

                var _pparam1 = _popStatusCommand.CreateParameter();
                _pparam1.ParameterName = "$identityId";
                _popStatusCommand.Parameters.Add(_pparam1);
                _pparam1.Value = ((IdentityDatabase)conn.db)._identityId.ToByteArray();

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
                            if (!rdr.IsDBNull(0))
                            {
                                var _guid = new byte[16];
                                var n = rdr.GetBytes(0, 0, _guid, 0, 16);
                                if (n != 16)
                                    throw new Exception("Invalid stamp");

                                var guid = new Guid(_guid);
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
        public (int totalCount, int poppedCount, UnixTimeUtc oldestItemTime) PopStatusSpecificBox(DatabaseConnection conn, Guid boxId)
        {
            using (var _popStatusSpecificBoxCommand = _database.CreateCommand())
            {
                _popStatusSpecificBoxCommand.CommandText =
                    "SELECT count(*) FROM inbox WHERE identityId=$identityId AND boxId=$boxId;" +
                    "SELECT count(*) FROM inbox WHERE identityId=$identityId AND boxId=$boxId AND popstamp NOT NULL;" +
                    "SELECT popstamp FROM inbox WHERE identityId=$identityId AND boxId=$boxId ORDER BY popstamp DESC LIMIT 1;";
                var _pssbparam1 = _popStatusSpecificBoxCommand.CreateParameter();
                var _pssbparam2 = _popStatusSpecificBoxCommand.CreateParameter();

                _pssbparam1.ParameterName = "$boxId";
                _pssbparam2.ParameterName = "$identityId";

                _popStatusSpecificBoxCommand.Parameters.Add(_pssbparam1);
                _popStatusSpecificBoxCommand.Parameters.Add(_pssbparam2);

                _pssbparam1.Value = boxId.ToByteArray();
                _pssbparam2.Value = ((IdentityDatabase)conn.db)._identityId.ToByteArray();

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

                        // Read the marker, if any
                        if (rdr.Read())
                        {
                            if (!rdr.IsDBNull(0))
                            {
                                var _guid = new byte[16];
                                var n = rdr.GetBytes(0, 0, _guid, 0, 16);
                                if (n != 16)
                                    throw new Exception("Invalid stamp");

                                var guid = new Guid(_guid);
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
        public int PopCancelAll(DatabaseConnection conn, Guid popstamp)
        {
            using (var _popCancelCommand = _database.CreateCommand())
            {
                _popCancelCommand.CommandText = "UPDATE inbox SET popstamp=NULL WHERE identityId=$identityId AND popstamp=$popstamp";

                var _pcancelparam1 = _popCancelCommand.CreateParameter();
                var _pcancelparam2 = _popCancelCommand.CreateParameter();

                _pcancelparam1.ParameterName = "$popstamp";
                _pcancelparam2.ParameterName = "$identityId";

                _popCancelCommand.Parameters.Add(_pcancelparam1);
                _popCancelCommand.Parameters.Add(_pcancelparam2);

                _pcancelparam1.Value = popstamp.ToByteArray();
                _pcancelparam2.Value = ((IdentityDatabase)conn.db)._identityId.ToByteArray();

                return conn.ExecuteNonQuery(_popCancelCommand);
            }
        }

        public void PopCancelList(DatabaseConnection conn, Guid popstamp, Guid driveId, List<Guid> listFileId)
        {
            using (var _popCancelListCommand = _database.CreateCommand())
            {
                _popCancelListCommand.CommandText = "UPDATE inbox SET popstamp=NULL WHERE identityId=$identityId AND fileid=$fileid AND popstamp=$popstamp";

                var _pcancellistparam1 = _popCancelListCommand.CreateParameter();
                var _pcancellistparam2 = _popCancelListCommand.CreateParameter();
                var _pcancellistparam3 = _popCancelListCommand.CreateParameter();

                _pcancellistparam1.ParameterName = "$popstamp";
                _pcancellistparam2.ParameterName = "$fileid";
                _pcancellistparam3.ParameterName = "$identityId";

                _popCancelListCommand.Parameters.Add(_pcancellistparam1);
                _popCancelListCommand.Parameters.Add(_pcancellistparam2);
                _popCancelListCommand.Parameters.Add(_pcancellistparam3);

                _pcancellistparam1.Value = popstamp.ToByteArray();
                _pcancellistparam3.Value = ((IdentityDatabase)conn.db)._identityId.ToByteArray();

                conn.CreateCommitUnitOfWork(() =>
                {
                    for (int i = 0; i < listFileId.Count; i++)
                    {
                        _pcancellistparam2.Value = listFileId[i].ToByteArray();
                        conn.ExecuteNonQuery(_popCancelListCommand);
                    }
                });
            }
        }


        /// <summary>
        /// Commits (removes) the items previously popped with the supplied 'popstamp'
        /// </summary>
        /// <param name="popstamp"></param>
        public int PopCommitAll(DatabaseConnection conn, Guid popstamp)
        {
            using (var _popCommitCommand = _database.CreateCommand())
            {
                _popCommitCommand.CommandText = "DELETE FROM inbox WHERE identityId=$identityId AND popstamp=$popstamp";

                var _pcommitparam1 = _popCommitCommand.CreateParameter();
                var _pcommitparam2 = _popCommitCommand.CreateParameter();

                _pcommitparam1.ParameterName = "$popstamp";
                _pcommitparam2.ParameterName = "$identityId";

                _popCommitCommand.Parameters.Add(_pcommitparam1);
                _popCommitCommand.Parameters.Add(_pcommitparam2);

                _pcommitparam1.Value = popstamp.ToByteArray();
                _pcommitparam2.Value = ((IdentityDatabase)conn.db)._identityId.ToByteArray();

                return conn.ExecuteNonQuery(_popCommitCommand);
            }
        }


        /// <summary>
        /// Commits (removes) the items previously popped with the supplied 'popstamp'
        /// </summary>
        /// <param name="popstamp"></param>
        public void PopCommitList(DatabaseConnection conn, Guid popstamp, Guid driveId, List<Guid> listFileId)
        {
            using (var _popCommitListCommand = _database.CreateCommand())
            {
                _popCommitListCommand.CommandText = "DELETE FROM inbox WHERE identityId=$identityId AND fileid=$fileid AND popstamp=$popstamp";

                var _pcommitlistparam1 = _popCommitListCommand.CreateParameter();
                var _pcommitlistparam2 = _popCommitListCommand.CreateParameter();
                var _pcommitlistparam3 = _popCommitListCommand.CreateParameter();

                _pcommitlistparam1.ParameterName = "$popstamp";
                _pcommitlistparam2.ParameterName = "$fileid";
                _pcommitlistparam3.ParameterName = "$identityId";

                _popCommitListCommand.Parameters.Add(_pcommitlistparam1);
                _popCommitListCommand.Parameters.Add(_pcommitlistparam2);
                _popCommitListCommand.Parameters.Add(_pcommitlistparam3);

                _pcommitlistparam1.Value = popstamp.ToByteArray();
                _pcommitlistparam3.Value = ((IdentityDatabase)conn.db)._identityId.ToByteArray();

                conn.CreateCommitUnitOfWork(() =>
                {
                    // I'd rather not do a TEXT statement, this seems safer but slower.
                    for (int i = 0; i < listFileId.Count; i++)
                    {
                        _pcommitlistparam2.Value = listFileId[i].ToByteArray();
                        conn.ExecuteNonQuery(_popCommitListCommand);
                    }
                });
            }
        }


        /// <summary>
        /// Recover popped items older than the supplied UnixTime in seconds.
        /// This is how to recover popped items that were never processed for example on a server crash.
        /// Call with e.g. a time of more than 5 minutes ago.
        /// </summary>
        public int PopRecoverDead(DatabaseConnection conn, UnixTimeUtc ut)
        {
            using (var _popRecoverCommand = _database.CreateCommand())
            {
                _popRecoverCommand.CommandText = "UPDATE inbox SET popstamp=NULL WHERE identityId=$identityId AND popstamp < $popstamp";

                var _pcrecoverparam1 = _popRecoverCommand.CreateParameter();
                var _pcrecoverparam2 = _popRecoverCommand.CreateParameter();

                _pcrecoverparam1.ParameterName = "$popstamp";
                _pcrecoverparam2.ParameterName = "$identityId";

                _popRecoverCommand.Parameters.Add(_pcrecoverparam1);
                _popRecoverCommand.Parameters.Add(_pcrecoverparam2);

                _pcrecoverparam1.Value = SequentialGuid.CreateGuid(new UnixTimeUtc(ut)).ToByteArray(); // UnixTimeMiliseconds
                _pcrecoverparam2.Value = ((IdentityDatabase)conn.db)._identityId.ToByteArray();

                return conn.ExecuteNonQuery(_popRecoverCommand);
            }
        }
    }
}