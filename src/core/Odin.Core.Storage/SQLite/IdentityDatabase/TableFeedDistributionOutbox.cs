using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableFeedDistributionOutbox: TableFeedDistributionOutboxCRUD
    {
        public TableFeedDistributionOutbox(IdentityDatabase db, CacheHelper cache) : base(db, cache)
        {
        }

        ~TableFeedDistributionOutbox()
        {
        }

        public override void Dispose()
        {
            base.Dispose();
            GC.SuppressFinalize(this);
        }


        public override int Insert(DatabaseBase.DatabaseConnection conn, FeedDistributionOutboxRecord item)
        {
            if (item.timeStamp.milliseconds == 0)
                item.timeStamp = UnixTimeUtc.Now();
            return base.Insert(conn, item);
        }


        public override int Upsert(DatabaseBase.DatabaseConnection conn, FeedDistributionOutboxRecord item)
        {
            if (item.timeStamp.milliseconds == 0)
                item.timeStamp = UnixTimeUtc.Now();
            return base.Upsert(conn, item);
        }


        public List<FeedDistributionOutboxRecord> Pop(DatabaseBase.DatabaseConnection conn, int count)
        {
            using (var _popAllCommand = _database.CreateCommand())
            {
                _popAllCommand.CommandText = "UPDATE feedDistributionOutbox SET popstamp=$popstamp WHERE popstamp is NULL and fileId IN (SELECT fileid FROM feedDistributionOutbox WHERE popstamp is NULL ORDER BY timestamp ASC LIMIT $count); " +
                                          "SELECT fileId,driveId,recipient,timeStamp,value,popStamp,created,modified FROM feedDistributionOutbox WHERE popstamp=$popstamp";

                var _paparam1 = _popAllCommand.CreateParameter();
                _paparam1.ParameterName = "$popstamp";
                _popAllCommand.Parameters.Add(_paparam1);

                var _paparam2 = _popAllCommand.CreateParameter();
                _paparam2.ParameterName = "$count";
                _popAllCommand.Parameters.Add(_paparam2);

                _paparam1.Value = SequentialGuid.CreateGuid().ToByteArray();
                _paparam2.Value = count;

                var result = new List<FeedDistributionOutboxRecord>();

                lock (conn._lock)
                {
                    using (conn.CreateCommitUnitOfWork())
                    {
                        using (SqliteDataReader rdr = _database.ExecuteReader(conn, _popAllCommand, System.Data.CommandBehavior.Default))
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
        }


        /// <summary>
        /// Status on the box
        /// </summary>
        /// <returns>Number of total items in box, number of popped items, the oldest popped item (ZeroTime if none)</returns>
        /// <exception cref="Exception"></exception>
        public (int, int, UnixTimeUtc) PopStatus(DatabaseBase.DatabaseConnection conn)
        {
            using (var _popStatusCommand = _database.CreateCommand())
            {
                _popStatusCommand.CommandText =
                    "SELECT count(*) FROM feedDistributionOutbox;" +
                    "SELECT count(*) FROM feedDistributionOutbox WHERE popstamp NOT NULL;" +
                    "SELECT popstamp FROM feedDistributionOutbox ORDER BY popstamp DESC LIMIT 1;";

                lock (conn._lock)
                {
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
        }




        /// <summary>
        /// Cancels the pop of items with the 'popstamp' from a previous pop operation
        /// </summary>
        /// <param name="popstamp"></param>
        public void PopCancelAll(DatabaseBase.DatabaseConnection conn, Guid popstamp)
        {
            using (var _popCancelCommand = _database.CreateCommand())
            {
                _popCancelCommand.CommandText = "UPDATE feedDistributionOutbox SET popstamp=NULL WHERE popstamp=$popstamp";

                var _pcancelparam1 = _popCancelCommand.CreateParameter();
                _pcancelparam1.ParameterName = "$popstamp";
                _popCancelCommand.Parameters.Add(_pcancelparam1);

                _pcancelparam1.Value = popstamp.ToByteArray();

                _database.ExecuteNonQuery(conn, _popCancelCommand);
            }
        }

        public void PopCancelList(DatabaseBase.DatabaseConnection conn, Guid popstamp, List<Guid> listFileId)
        {
            using (var _popCancelListCommand = _database.CreateCommand())
            {
                _popCancelListCommand.CommandText = "UPDATE feedDistributionOutbox SET popstamp=NULL WHERE fileid=$fileid AND popstamp=$popstamp";

                var _pcancellistparam1 = _popCancelListCommand.CreateParameter();
                _pcancellistparam1.ParameterName = "$popstamp";
                _popCancelListCommand.Parameters.Add(_pcancellistparam1);

                var _pcancellistparam2 = _popCancelListCommand.CreateParameter();
                _pcancellistparam2.ParameterName = "$fileid";
                _popCancelListCommand.Parameters.Add(_pcancellistparam2);

                _pcancellistparam1.Value = popstamp.ToByteArray();

                lock (conn._lock)
                {
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
        }


        /// <summary>
        /// Commits (removes) the items previously popped with the supplied 'popstamp'
        /// </summary>
        /// <param name="popstamp"></param>
        public void PopCommitAll(DatabaseBase.DatabaseConnection conn, Guid popstamp)
        {
            using (var _popCommitCommand = _database.CreateCommand())
            {
                _popCommitCommand.CommandText = "DELETE FROM feedDistributionOutbox WHERE popstamp=$popstamp";

                var _pcommitparam1 = _popCommitCommand.CreateParameter();
                _pcommitparam1.ParameterName = "$popstamp";
                _popCommitCommand.Parameters.Add(_pcommitparam1);

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
                using (var _popCommitListCommand = _database.CreateCommand())
                {
                    _popCommitListCommand.CommandText = "DELETE FROM feedDistributionOutbox WHERE fileid=$fileid AND popstamp=$popstamp";

                    var _pcommitlistparam1 = _popCommitListCommand.CreateParameter();
                    _pcommitlistparam1.ParameterName = "$popstamp";
                    _popCommitListCommand.Parameters.Add(_pcommitlistparam1);

                    var _pcommitlistparam2 = _popCommitListCommand.CreateParameter();
                    _pcommitlistparam2.ParameterName = "$fileid";
                    _popCommitListCommand.Parameters.Add(_pcommitlistparam2);

                _pcommitlistparam1.Value = popstamp.ToByteArray();

                lock (conn._lock)
                {
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
        }


        /// <summary>
        /// Recover popped items older than the supplied UnixTime in seconds.
        /// This is how to recover popped items that were never processed for example on a server crash.
        /// Call with e.g. a time of more than 5 minutes ago.
        /// </summary>
        public void PopRecoverDead(DatabaseBase.DatabaseConnection conn, UnixTimeUtc UnixTimeSeconds)
        {
            using (var _popRecoverCommand = _database.CreateCommand())
            {
                _popRecoverCommand.CommandText = "UPDATE feedDistributionOutbox SET popstamp=NULL WHERE popstamp < $popstamp";

                var _pcrecoverparam1 = _popRecoverCommand.CreateParameter();
                _pcrecoverparam1.ParameterName = "$popstamp";
                _popRecoverCommand.Parameters.Add(_pcrecoverparam1);

                _pcrecoverparam1.Value = SequentialGuid.CreateGuid(UnixTimeSeconds).ToByteArray(); // UnixTimeMiliseconds

                _database.ExecuteNonQuery(conn, _popRecoverCommand);
            }
        }
    }
}
