using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableInbox : TableInboxCRUD
    {
        private SqliteCommand _popCommand = null;
        private SqliteParameter _pparam1 = null;
        private SqliteParameter _pparam2 = null;
        private SqliteParameter _pparam3 = null;
        private static Object _popLock = new Object();

        private SqliteCommand _popStatusCommand = null;

        private SqliteCommand _popStatusSpecificBoxCommand = null;
        private SqliteParameter _pssbparam1 = null;

        private SqliteCommand _popCancelCommand = null;
        private SqliteParameter _pcancelparam1 = null;

        private SqliteCommand _popCancelListCommand = null;
        private SqliteParameter _pcancellistparam1 = null;
        private SqliteParameter _pcancellistparam2 = null;
        private static Object _popCancelListLock = new Object();

        private SqliteCommand _popCommitCommand = null;
        private SqliteParameter _pcommitparam1 = null;

        private SqliteCommand _popCommitListCommand = null;
        private SqliteParameter _pcommitlistparam1 = null;
        private SqliteParameter _pcommitlistparam2 = null;
        private static Object _popCommitListLock = new Object();

        private SqliteCommand _popRecoverCommand = null;
        private SqliteParameter _pcrecoverparam1 = null;


        public TableInbox(IdentityDatabase db, CacheHelper cache) : base(db, cache)
        {
        }

        ~TableInbox()
        {
        }

        public override void Dispose()
        {
            _popCommand?.Dispose();
            _popCommand = null;

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

        public override int Insert(DatabaseBase.DatabaseConnection conn, InboxRecord item)
        {
            if (item.timeStamp.milliseconds == 0)
                item.timeStamp = UnixTimeUtc.Now();
            if (item.sender == null)
                item.sender = "";
            return base.Insert(conn, item);
        }

        public override int Upsert(DatabaseBase.DatabaseConnection conn, InboxRecord item)
        {
            if (item.timeStamp.milliseconds == 0)
                item.timeStamp = UnixTimeUtc.Now();
            if (item.sender == null)
                item.sender = "";
            return base.Insert(conn, item);
        }


        /// <summary>
        /// Pops 'count' items from the table. The items remain in the DB with the 'popstamp' unique identifier.
        /// Popstamp is used by the caller to release the items when they have been successfully processed, or
        /// to cancel the transaction and restore the items to the inbox.
        /// </summary
        /// <param name="driveId">Is the box to pop from, e.g. Drive A, or App B</param>
        /// <param name="count">How many items to 'pop' (reserve)</param>
        /// <param name="popStamp">The unique identifier for the items reserved for pop</param>
        /// <returns>List of records</returns>
        public List<InboxRecord> PopSpecificBox(DatabaseBase.DatabaseConnection conn, Guid driveId, int count)
        {
            lock (_popLock)
            {
                // Make sure we only prep once 
                if (_popCommand == null)
                {
                    _popCommand = _database.CreateCommand(conn);
                    _popCommand.CommandText = "UPDATE inbox SET popstamp=$popstamp WHERE rowid IN (SELECT rowid FROM inbox WHERE driveId=$driveId AND popstamp IS NULL ORDER BY rowId ASC LIMIT $count); " +
                                              "SELECT rowId,driveId,fileId,sender,type,priority,timeStamp,value,popStamp,created,modified FROM inbox WHERE popstamp=$popstamp";

                    _pparam1 = _popCommand.CreateParameter();
                    _pparam1.ParameterName = "$popstamp";
                    _popCommand.Parameters.Add(_pparam1);

                    _pparam2 = _popCommand.CreateParameter();
                    _pparam2.ParameterName = "$count";
                    _popCommand.Parameters.Add(_pparam2);

                    _pparam3 = _popCommand.CreateParameter();
                    _pparam3.ParameterName = "$driveId";
                    _popCommand.Parameters.Add(_pparam3);

                    _popCommand.Prepare();
                }

                _pparam1.Value = SequentialGuid.CreateGuid().ToByteArray();
                _pparam2.Value = count;
                _pparam3.Value = driveId.ToByteArray();

                using (conn.CreateCommitUnitOfWork())
                {
                    List<InboxRecord> result = new List<InboxRecord>();
                    using (SqliteDataReader rdr = _database.ExecuteReader(conn, _popCommand, System.Data.CommandBehavior.Default))
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


        /// <summary>
        /// Status on the box
        /// </summary>
        /// <returns>Number of total items in box, number of popped items, the oldest popped item (ZeroTime if none)</returns>
        /// <exception cref="Exception"></exception>
        public (int, int, UnixTimeUtc) PopStatus(DatabaseBase.DatabaseConnection conn)
        {
            lock (_popLock)
            {
                // Make sure we only prep once 
                if (_popStatusCommand == null)
                {
                    _popStatusCommand = _database.CreateCommand(conn);
                    _popStatusCommand.CommandText =
                        "SELECT count(*) FROM inbox;" +
                        "SELECT count(*) FROM inbox WHERE popstamp NOT NULL;" +
                        "SELECT popstamp FROM inbox ORDER BY popstamp DESC LIMIT 1;";
                    _popStatusCommand.Prepare();
                }

                using (SqliteDataReader rdr = _database.ExecuteReader(conn, _popStatusCommand, System.Data.CommandBehavior.Default))
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


        /// <summary>
        /// Status on the box
        /// </summary>
        /// <returns>Number of total items in box, number of popped items, the oldest popped item (ZeroTime if none)</returns>
        /// <exception cref="Exception"></exception>
        public (int totalCount, int poppedCount, UnixTimeUtc oldestItemTime) PopStatusSpecificBox(DatabaseBase.DatabaseConnection conn, Guid driveId)
        {
            lock (_popLock)
            {
                // Make sure we only prep once 
                if (_popStatusSpecificBoxCommand == null)
                {
                    _popStatusSpecificBoxCommand = _database.CreateCommand(conn);
                    _popStatusSpecificBoxCommand.CommandText =
                        "SELECT count(*) FROM inbox WHERE driveId=$driveId;" +
                        "SELECT count(*) FROM inbox WHERE driveId=$driveId AND popstamp NOT NULL;" +
                        "SELECT popstamp FROM inbox WHERE driveId=$driveId ORDER BY popstamp DESC LIMIT 1;";
                    _pssbparam1 = _popStatusSpecificBoxCommand.CreateParameter();
                    _pssbparam1.ParameterName = "$driveId";
                    _popStatusSpecificBoxCommand.Parameters.Add(_pssbparam1);

                    _popStatusSpecificBoxCommand.Prepare();
                }

                _pssbparam1.Value = driveId.ToByteArray();

                using (SqliteDataReader rdr = _database.ExecuteReader(conn, _popStatusSpecificBoxCommand, System.Data.CommandBehavior.Default))
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



        /// <summary>
        /// Cancels the pop of items with the 'popstamp' from a previous pop operation
        /// </summary>
        /// <param name="popstamp"></param>
        public void PopCancelAll(DatabaseBase.DatabaseConnection conn, Guid popstamp)
        {
            lock (_popLock)
            {
                // Make sure we only prep once 
                if (_popCancelCommand == null)
                {
                    _popCancelCommand = _database.CreateCommand(conn);
                    _popCancelCommand.CommandText = "UPDATE inbox SET popstamp=NULL WHERE popstamp=$popstamp";

                    _pcancelparam1 = _popCancelCommand.CreateParameter();

                    _pcancelparam1.ParameterName = "$popstamp";
                    _popCancelCommand.Parameters.Add(_pcancelparam1);

                    _popCancelCommand.Prepare();
                }

                _pcancelparam1.Value = popstamp.ToByteArray();

                _database.ExecuteNonQuery(conn, _popCancelCommand);
            }
        }

        public void PopCancelList(DatabaseBase.DatabaseConnection conn, Guid popstamp, List<Guid> listFileId)
        {
            lock (_popCancelListLock)
            {
                // Make sure we only prep once 
                if (_popCancelListCommand == null)
                {
                    _popCancelListCommand = _database.CreateCommand(conn);
                    _popCancelListCommand.CommandText = "UPDATE inbox SET popstamp=NULL WHERE fileid=$fileid AND popstamp=$popstamp";

                    _pcancellistparam1 = _popCancelListCommand.CreateParameter();
                    _pcancellistparam1.ParameterName = "$popstamp";
                    _popCancelListCommand.Parameters.Add(_pcancellistparam1);

                    _pcancellistparam2 = _popCancelListCommand.CreateParameter();
                    _pcancellistparam2.ParameterName = "$fileid";
                    _popCancelListCommand.Parameters.Add(_pcancellistparam2);

                    _popCancelListCommand.Prepare();
                }

                _pcancellistparam1.Value = popstamp.ToByteArray();

                using (conn.CreateCommitUnitOfWork())
                {
                    // I'd rather not do a TEXT statement, this seems safer but slower.
                    for (int i = 0; i < listFileId.Count; i++)
                    {
                        _pcancellistparam2.Value = listFileId[i].ToByteArray();
                        _database.ExecuteNonQuery(conn, _popCancelListCommand);
                    }
                }
            }
        }


        /// <summary>
        /// Commits (removes) the items previously popped with the supplied 'popstamp'
        /// </summary>
        /// <param name="popstamp"></param>
        public void PopCommitAll(DatabaseBase.DatabaseConnection conn, Guid popstamp)
        {
            lock (_popLock)
            {
                // Make sure we only prep once 
                if (_popCommitCommand == null)
                {
                    _popCommitCommand = _database.CreateCommand(conn);
                    _popCommitCommand.CommandText = "DELETE FROM inbox WHERE popstamp=$popstamp";

                    _pcommitparam1 = _popCommitCommand.CreateParameter();
                    _pcommitparam1.ParameterName = "$popstamp";
                    _popCommitCommand.Parameters.Add(_pcommitparam1);

                    _popCommitCommand.Prepare();
                }

                _pcommitparam1.Value = popstamp.ToByteArray();

                _database.ExecuteNonQuery(conn, _popCommitCommand);
            }
        }


        /// <summary>
        /// Commits (removes) the items previously popped with the supplied 'popstamp'
        /// </summary>
        /// <param name="popstamp"></param>
        public void PopCommitList(DatabaseBase.DatabaseConnection conn, Guid popstamp, List<Guid> listFileId)
        {
            lock (_popCommitListLock)
            {
                // Make sure we only prep once 
                if (_popCommitListCommand == null)
                {
                    _popCommitListCommand = _database.CreateCommand(conn);
                    _popCommitListCommand.CommandText = "DELETE FROM inbox WHERE fileid=$fileid AND popstamp=$popstamp";

                    _pcommitlistparam1 = _popCommitListCommand.CreateParameter();
                    _pcommitlistparam1.ParameterName = "$popstamp";
                    _popCommitListCommand.Parameters.Add(_pcommitlistparam1);

                    _pcommitlistparam2 = _popCommitListCommand.CreateParameter();
                    _pcommitlistparam2.ParameterName = "$fileid";
                    _popCommitListCommand.Parameters.Add(_pcommitlistparam2);

                    _popCommitListCommand.Prepare();
                }

                _pcommitlistparam1.Value = popstamp.ToByteArray();

                using (conn.CreateCommitUnitOfWork())
                {
                    // I'd rather not do a TEXT statement, this seems safer but slower.
                    for (int i = 0; i < listFileId.Count; i++)
                    {
                        _pcommitlistparam2.Value = listFileId[i].ToByteArray();
                        _database.ExecuteNonQuery(conn, _popCommitListCommand);
                    }
                }
            }
        }


        /// <summary>
        /// Recover popped items older than the supplied UnixTime in seconds.
        /// This is how to recover popped items that were never processed for example on a server crash.
        /// Call with e.g. a time of more than 5 minutes ago.
        /// </summary>
        public void PopRecoverDead(DatabaseBase.DatabaseConnection conn, UnixTimeUtc ut)
        {
            lock (_popLock)
            {
                if (_popRecoverCommand == null)
                {
                    _popRecoverCommand = _database.CreateCommand(conn);
                    _popRecoverCommand.CommandText = "UPDATE inbox SET popstamp=NULL WHERE popstamp < $popstamp";

                    _pcrecoverparam1 = _popRecoverCommand.CreateParameter();

                    _pcrecoverparam1.ParameterName = "$popstamp";
                    _popRecoverCommand.Parameters.Add(_pcrecoverparam1);

                    _popRecoverCommand.Prepare();
                }

                _pcrecoverparam1.Value = SequentialGuid.CreateGuid(new UnixTimeUtc(ut)).ToByteArray(); // UnixTimeMiliseconds

                _database.ExecuteNonQuery(conn, _popRecoverCommand);
            }
        }
    }
}