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

        public override int Insert(DatabaseConnection conn, InboxRecord item)
        {
            if (item.timeStamp.milliseconds == 0)
                item.timeStamp = UnixTimeUtc.Now();
            return base.Insert(conn, item);
        }

        public override int Upsert(DatabaseConnection conn, InboxRecord item)
        {
            if (item.timeStamp.milliseconds == 0)
                item.timeStamp = UnixTimeUtc.Now();
            return base.Insert(conn, item);
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
                _popCommand.CommandText = "UPDATE inbox SET popstamp=$popstamp WHERE rowid IN (SELECT rowid FROM inbox WHERE boxId=$boxId AND popstamp IS NULL ORDER BY rowId ASC LIMIT $count); " +
                                          "SELECT fileId,boxId,priority,timeStamp,value,popStamp,created,modified FROM inbox WHERE popstamp=$popstamp";

                var _pparam1 = _popCommand.CreateParameter();
                _pparam1.ParameterName = "$popstamp";
                _popCommand.Parameters.Add(_pparam1);

                var _pparam2 = _popCommand.CreateParameter();
                _pparam2.ParameterName = "$count";
                _popCommand.Parameters.Add(_pparam2);

                var _pparam3 = _popCommand.CreateParameter();
                _pparam3.ParameterName = "$boxId";
                _popCommand.Parameters.Add(_pparam3);

                _pparam1.Value = SequentialGuid.CreateGuid().ToByteArray();
                _pparam2.Value = count;
                _pparam3.Value = boxId.ToByteArray();

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
                    "SELECT count(*) FROM inbox;" +
                    "SELECT count(*) FROM inbox WHERE popstamp NOT NULL;" +
                    "SELECT popstamp FROM inbox ORDER BY popstamp DESC LIMIT 1;";

                lock (conn._lock)
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_popStatusCommand, System.Data.CommandBehavior.Default))
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

                        var _guid = new byte[16];
                        var n = rdr.GetBytes(0, 0, _guid, 0, 16);
                        if (n != 16)
                            throw new Exception("Invalid stamp");

                        var guid = new Guid(_guid);
                        var utc = SequentialGuid.ToUnixTimeUtc(guid);
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
                    "SELECT count(*) FROM inbox WHERE boxId=$boxId;" +
                    "SELECT count(*) FROM inbox WHERE boxId=$boxId AND popstamp NOT NULL;" +
                    "SELECT popstamp FROM inbox WHERE boxId=$boxId ORDER BY popstamp DESC LIMIT 1;";
                var _pssbparam1 = _popStatusSpecificBoxCommand.CreateParameter();
                _pssbparam1.ParameterName = "$boxId";
                _popStatusSpecificBoxCommand.Parameters.Add(_pssbparam1);

                _pssbparam1.Value = boxId.ToByteArray();

                lock (conn._lock)
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_popStatusSpecificBoxCommand, System.Data.CommandBehavior.Default))
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

                        var _guid = new byte[16];
                        var n = rdr.GetBytes(0, 0, _guid, 0, 16);
                        if (n != 16)
                            throw new Exception("Invalid stamp");

                        var guid = new Guid(_guid);
                        var utc = SequentialGuid.ToUnixTimeUtc(guid);
                        return (totalCount, poppedCount, utc);
                    }
                }
            }
        }



        /// <summary>
        /// Cancels the pop of items with the 'popstamp' from a previous pop operation
        /// </summary>
        /// <param name="popstamp"></param>
        public void PopCancelAll(DatabaseConnection conn, Guid popstamp)
        {
            using (var _popCancelCommand = _database.CreateCommand())
            {
                _popCancelCommand.CommandText = "UPDATE inbox SET popstamp=NULL WHERE popstamp=$popstamp";

                var _pcancelparam1 = _popCancelCommand.CreateParameter();
                _pcancelparam1.ParameterName = "$popstamp";
                _popCancelCommand.Parameters.Add(_pcancelparam1);

                _pcancelparam1.Value = popstamp.ToByteArray();

                conn.ExecuteNonQuery(_popCancelCommand);
            }
        }

        public void PopCancelList(DatabaseConnection conn, Guid popstamp, Guid driveId, List<Guid> listFileId)
        {
            using (var _popCancelListCommand = _database.CreateCommand())
            {
                _popCancelListCommand.CommandText = "UPDATE inbox SET popstamp=NULL WHERE boxId=$driveId AND fileid=$fileid AND popstamp=$popstamp";

                var _pcancellistparam1 = _popCancelListCommand.CreateParameter();
                _pcancellistparam1.ParameterName = "$popstamp";
                _popCancelListCommand.Parameters.Add(_pcancellistparam1);

                var _pcancellistparam2 = _popCancelListCommand.CreateParameter();
                _pcancellistparam2.ParameterName = "$fileid";
                _popCancelListCommand.Parameters.Add(_pcancellistparam2);

                var _pcancellistparam3 = _popCancelListCommand.CreateParameter();
                _pcancellistparam3.ParameterName = "$driveId";
                _popCancelListCommand.Parameters.Add(_pcancellistparam3);

                _pcancellistparam1.Value = popstamp.ToByteArray();
                _pcancellistparam3.Value = driveId.ToByteArray();

                conn.CreateCommitUnitOfWork(() =>
                {
                    // I'd rather not do a TEXT statement, this seems safer but slower.
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
        public void PopCommitAll(DatabaseConnection conn, Guid popstamp)
        {
            using (var _popCommitCommand = _database.CreateCommand())
            {
                _popCommitCommand.CommandText = "DELETE FROM inbox WHERE popstamp=$popstamp";

                var _pcommitparam1 = _popCommitCommand.CreateParameter();
                _pcommitparam1.ParameterName = "$popstamp";
                _popCommitCommand.Parameters.Add(_pcommitparam1);

                _pcommitparam1.Value = popstamp.ToByteArray();

                conn.ExecuteNonQuery(_popCommitCommand);
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
                _popCommitListCommand.CommandText = "DELETE FROM inbox WHERE boxId=$driveId AND fileid=$fileid AND popstamp=$popstamp";

                var _pcommitlistparam1 = _popCommitListCommand.CreateParameter();
                _pcommitlistparam1.ParameterName = "$popstamp";
                _popCommitListCommand.Parameters.Add(_pcommitlistparam1);

                var _pcommitlistparam2 = _popCommitListCommand.CreateParameter();
                _pcommitlistparam2.ParameterName = "$fileid";
                _popCommitListCommand.Parameters.Add(_pcommitlistparam2);

                var _pcommitlistparam3 = _popCommitListCommand.CreateParameter();
                _pcommitlistparam3.ParameterName = "$driveId";
                _popCommitListCommand.Parameters.Add(_pcommitlistparam3);

                _pcommitlistparam1.Value = popstamp.ToByteArray();
                _pcommitlistparam3.Value = driveId.ToByteArray();

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
        public void PopRecoverDead(DatabaseConnection conn, UnixTimeUtc ut)
        {
            using (var _popRecoverCommand = _database.CreateCommand())
            {
                _popRecoverCommand.CommandText = "UPDATE inbox SET popstamp=NULL WHERE popstamp < $popstamp";

                var _pcrecoverparam1 = _popRecoverCommand.CreateParameter();
                _pcrecoverparam1.ParameterName = "$popstamp";
                _popRecoverCommand.Parameters.Add(_pcrecoverparam1);


                _pcrecoverparam1.Value = SequentialGuid.CreateGuid(new UnixTimeUtc(ut)).ToByteArray(); // UnixTimeMiliseconds

                conn.ExecuteNonQuery(_popRecoverCommand);
            }
        }
    }
}