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

        public FeedDistributionOutboxRecord Get(DatabaseConnection conn, Guid fileId, Guid driveId, string recipient)
        {
            return base.Get(conn, ((IdentityDatabase)conn.db)._identityId, fileId, driveId, recipient);
        }

        public new int Insert(DatabaseConnection conn, FeedDistributionOutboxRecord item)
        {
            item.identityId = ((IdentityDatabase)conn.db)._identityId;

            if (item.timeStamp.milliseconds == 0)
                item.timeStamp = UnixTimeUtc.Now();
            return base.Insert(conn, item);
        }


        public new int Upsert(DatabaseConnection conn, FeedDistributionOutboxRecord item)
        {
            item.identityId = ((IdentityDatabase)conn.db)._identityId;

            if (item.timeStamp.milliseconds == 0)
                item.timeStamp = UnixTimeUtc.Now();
            return base.Upsert(conn, item);
        }


        public List<FeedDistributionOutboxRecord> Pop(DatabaseConnection conn, int count)
        {
            using (var _popAllCommand = _database.CreateCommand())
            {
                _popAllCommand.CommandText = "UPDATE feedDistributionOutbox SET popstamp=$popstamp WHERE identityId=$identityId AND popstamp is NULL and fileId IN (SELECT fileid FROM feedDistributionOutbox WHERE identityId=$identityId AND popstamp is NULL ORDER BY timestamp ASC LIMIT $count); " +
                                          "SELECT identityId,fileId,driveId,recipient,timeStamp,value,popStamp,created,modified FROM feedDistributionOutbox WHERE identityId=$identityId AND popstamp=$popstamp";

                var _paparam1 = _popAllCommand.CreateParameter();
                var _paparam2 = _popAllCommand.CreateParameter();
                var _paparam3 = _popAllCommand.CreateParameter();

                _paparam1.ParameterName = "$popstamp";
                _paparam2.ParameterName = "$count";
                _paparam3.ParameterName = "$identityId";

                _popAllCommand.Parameters.Add(_paparam1);
                _popAllCommand.Parameters.Add(_paparam2);
                _popAllCommand.Parameters.Add(_paparam3);

                _paparam1.Value = SequentialGuid.CreateGuid().ToByteArray();
                _paparam2.Value = count;
                _paparam3.Value = ((IdentityDatabase)conn.db)._identityId.ToByteArray();

                var result = new List<FeedDistributionOutboxRecord>();

                conn.CreateCommitUnitOfWork(() =>
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_popAllCommand, System.Data.CommandBehavior.Default))
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
                    "SELECT count(*) FROM feedDistributionOutbox WHERE identityId=$identityId;" +
                    "SELECT count(*) FROM feedDistributionOutbox WHERE identityId=$identityId AND popstamp NOT NULL;" +
                    "SELECT popstamp FROM feedDistributionOutbox WHERE identityId=$identityId ORDER BY popstamp DESC LIMIT 1;";

                var _paparam1 = _popStatusCommand.CreateParameter();
                _paparam1.ParameterName = "$identityId";
                _popStatusCommand.Parameters.Add(_paparam1);
                _paparam1.Value = ((IdentityDatabase)conn.db)._identityId.ToByteArray();

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

                        // Read the marker, if any
                        if (!rdr.Read())
                            throw new Exception("Not possible");

                        var poptime = UnixTimeUtc.ZeroTime;

                        if (!rdr.IsDBNull(0))
                        {
                            var _guid = new byte[16];
                            var n = rdr.GetBytes(0, 0, _guid, 0, 16);
                            if (n != 16)
                                throw new Exception("Invalid stamp");

                            var guid = new Guid(_guid);
                            poptime = SequentialGuid.ToUnixTimeUtc(guid);
                        }

                        return (totalCount, poppedCount, poptime);
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
                _popCancelCommand.CommandText = "UPDATE feedDistributionOutbox SET popstamp=NULL WHERE identityId=$identityId AND popstamp=$popstamp";

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

        public void PopCancelList(DatabaseConnection conn, Guid popstamp, List<Guid> listFileId)
        {
            using (var _popCancelListCommand = _database.CreateCommand())
            {
                _popCancelListCommand.CommandText = "UPDATE feedDistributionOutbox SET popstamp=NULL WHERE identityId=$identityId AND fileid=$fileid AND popstamp=$popstamp";

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
        public int PopCommitAll(DatabaseConnection conn, Guid popstamp)
        {
            using (var _popCommitCommand = _database.CreateCommand())
            {
                _popCommitCommand.CommandText = "DELETE FROM feedDistributionOutbox WHERE identityId=$identityId AND popstamp=$popstamp";

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
        public void PopCommitList(DatabaseConnection conn, Guid popstamp, List<Guid> listFileId)
        {
            using (var _popCommitListCommand = _database.CreateCommand())
            {
                _popCommitListCommand.CommandText = "DELETE FROM feedDistributionOutbox WHERE identityId=$identityId AND fileid=$fileid AND popstamp=$popstamp";

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
        public int PopRecoverDead(DatabaseConnection conn, UnixTimeUtc UnixTimeSeconds)
        {
            using (var _popRecoverCommand = _database.CreateCommand())
            {
                _popRecoverCommand.CommandText = "UPDATE feedDistributionOutbox SET popstamp=NULL WHERE identityId=$identityId AND popstamp < $popstamp";

                var _pcrecoverparam1 = _popRecoverCommand.CreateParameter();
                var _pcrecoverparam2 = _popRecoverCommand.CreateParameter();

                _pcrecoverparam1.ParameterName = "$popstamp";
                _pcrecoverparam2.ParameterName = "$identityId";

                _popRecoverCommand.Parameters.Add(_pcrecoverparam1);
                _popRecoverCommand.Parameters.Add(_pcrecoverparam2);

                _pcrecoverparam1.Value = SequentialGuid.CreateGuid(UnixTimeSeconds).ToByteArray(); // UnixTimeMiliseconds
                _pcrecoverparam2.Value = ((IdentityDatabase)conn.db)._identityId.ToByteArray();

                return conn.ExecuteNonQuery(_popRecoverCommand);
            }
        }
    }
}
