using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace Youverse.Core.Storage.Sqlite.IdentityDatabase
{
    public class TableOutbox: TableOutboxCRUD
    {
        const int MAX_VALUE_LENGTH = 65535;  // Stored value cannot be longer than this

        private SqliteCommand _popCommand = null;
        private SqliteParameter _pparam1 = null;
        private SqliteParameter _pparam2 = null;
        private SqliteParameter _pparam3 = null;
        private static Object _popLock = new Object();

        private SqliteCommand _popAllCommand = null;
        private SqliteParameter _paparam1 = null;
        private static Object _popAllLock = new Object();

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

        public TableOutbox(IdentityDatabase db) : base(db)
        {
        }

        ~TableOutbox()
        {
        }

        public override void Dispose()
        {
            _popCommand?.Dispose();
            _popCommand = null;

            _popAllCommand?.Dispose();
            _popAllCommand = null;

            _popCancelCommand?.Dispose();
            _popCancelCommand = null;

            _popCancelListCommand?.Dispose();
            _popCancelListCommand = null;

            _popCommitCommand?.Dispose();
            _popCommitCommand = null;

            _popRecoverCommand?.Dispose();
            _popRecoverCommand = null;

            base.Dispose();
        }

        public override int Insert(OutboxRecord item)
        {
            if (item.timeStamp.milliseconds == 0)
                item.timeStamp = UnixTimeUtc.Now();
            return base.Insert(item);
        }

        public override int Upsert(OutboxRecord item)
        {
            if (item.timeStamp.milliseconds == 0)
                item.timeStamp = UnixTimeUtc.Now();
            return base.Insert(item);
        }


        /// <summary>
        /// Pops 'count' items from the outbox. The items remain in the DB with the 'popstamp' unique identifier.
        /// Popstamp is used by the caller to release the items when they have been successfully processed, or
        /// to cancel the transaction and restore the items to the outbox.
        /// </summary
        /// <param name="boxId">Is the outbox to pop from, e.g. Drive A, or App B</param>
        /// <param name="count">How many items to 'pop' (reserve)</param>
        /// <param name="popStamp">The unique identifier for the items reserved for pop</param>
        /// <returns></returns>
        public List<OutboxRecord> Pop(Guid boxId, int count, out Guid popStamp)
        {
            lock (_popLock)
            {
                // Make sure we only prep once 
                if (_popCommand == null)
                {
                    _popCommand = _database.CreateCommand();
                    _popCommand.CommandText = "UPDATE outbox SET popStamp=$popstamp WHERE rowid IN (SELECT rowid FROM outbox WHERE boxId=$boxid AND popStamp IS NULL ORDER BY timeStamp ASC LIMIT $count); " +
                                              "SELECT fileId, priority, timeStamp, value, recipient from outbox WHERE popstamp=$popstamp";

                    _pparam1 = _popCommand.CreateParameter();
                    _pparam1.ParameterName = "$popstamp";
                    _popCommand.Parameters.Add(_pparam1);

                    _pparam2 = _popCommand.CreateParameter();
                    _pparam2.ParameterName = "$count";
                    _popCommand.Parameters.Add(_pparam2);

                    _pparam3 = _popCommand.CreateParameter();
                    _pparam3.ParameterName = "$boxid";
                    _popCommand.Parameters.Add(_pparam3);

                    _popCommand.Prepare();
                }

                popStamp = SequentialGuid.CreateGuid();
                _pparam1.Value = popStamp.ToByteArray();
                _pparam2.Value = count;
                _pparam3.Value = boxId.ToByteArray();

                List<OutboxRecord> result = new List<OutboxRecord>();

                _database.BeginTransaction();
                _popCommand.Transaction = _database.Transaction;

                using (_database.CreateCommitUnitOfWork())
                {
                    using (SqliteDataReader rdr = _popCommand.ExecuteReader(System.Data.CommandBehavior.Default))
                    {
                        OutboxRecord item;

                        while (rdr.Read())
                        {
                            if (rdr.IsDBNull(0))
                                throw new Exception("Not possible");

                            item = new OutboxRecord();
                            item.boxId = boxId;
                            var _guid = new byte[16];
                            var n = rdr.GetBytes(0, 0, _guid, 0, 16);
                            if (n != 16)
                                throw new Exception("Invalid fileId");
                            item.fileId = new Guid(_guid);
                            item.priority = rdr.GetInt32(1);
                            item.timeStamp = new UnixTimeUtc(rdr.GetInt64(2));

                            if (rdr.IsDBNull(3))
                            {
                                item.value = null;
                            }
                            else
                            {
                                byte[] _tmpbuf = new byte[MAX_VALUE_LENGTH];
                                n = rdr.GetBytes(3, 0, _tmpbuf, 0, MAX_VALUE_LENGTH);
                                if (n >= MAX_VALUE_LENGTH)
                                    throw new Exception("Too much data...");
                                if (n == 0)
                                    throw new Exception("Is that possible?");

                                item.value = new byte[n];
                                Buffer.BlockCopy(_tmpbuf, 0, item.value, 0, (int)n);
                            }
                            item.recipient = rdr.GetString(4);

                            result.Add(item);
                        }
                    }

                    return result;
                }
            }
        }

        public List<OutboxRecord> PopAll(out Guid popStamp)
        {
            lock (_popAllLock)
            {
                // Make sure we only prep once 
                if (_popAllCommand == null)
                {
                    _popAllCommand = _database.CreateCommand();
                    _popAllCommand.CommandText = "UPDATE outbox SET popstamp=$popstamp WHERE popstamp is NULL and fileId IN (SELECT fileid FROM outbox WHERE popstamp is NULL GROUP BY boxid ORDER BY timestamp ASC); " +
                                              "SELECT fileid, priority, timestamp, value, boxid, recipient from outbox WHERE popstamp=$popstamp";

                    _paparam1 = _popAllCommand.CreateParameter();
                    _paparam1.ParameterName = "$popstamp";
                    _popAllCommand.Parameters.Add(_paparam1);

                    _popAllCommand.Prepare();
                }

                popStamp = SequentialGuid.CreateGuid();
                _paparam1.Value = popStamp.ToByteArray();

                List<OutboxRecord> result = new List<OutboxRecord>();

                _database.BeginTransaction();

                using (_database.CreateCommitUnitOfWork())
                {
                    using (SqliteDataReader rdr = _popAllCommand.ExecuteReader(System.Data.CommandBehavior.Default, _database))
                    {
                        OutboxRecord item;

                        while (rdr.Read())
                        {
                            if (rdr.IsDBNull(0))
                                throw new Exception("Not possible");

                            item = new OutboxRecord();

                            var _guid  = new byte[16];
                            var n = rdr.GetBytes(0, 0, _guid, 0, 16);
                            if (n != 16)
                                throw new Exception("Invalid fileId");
                            item.fileId = new Guid(_guid);
                            item.priority = rdr.GetInt32(1);
                            item.timeStamp = new UnixTimeUtc(rdr.GetInt64(2));

                            if (rdr.IsDBNull(3))
                            {
                                item.value = null;
                            }
                            else
                            {
                                byte[] _tmpbuf = new byte[MAX_VALUE_LENGTH];
                                n = rdr.GetBytes(3, 0, _tmpbuf, 0, MAX_VALUE_LENGTH);
                                if (n >= MAX_VALUE_LENGTH)
                                    throw new Exception("Too much data...");
                                if (n == 0)
                                    throw new Exception("Is that possible?");

                                item.value = new byte[n];
                                Buffer.BlockCopy(_tmpbuf, 0, item.value, 0, (int)n);
                            }

                            n = rdr.GetBytes(4, 0, _guid, 0, 16);

                            if (n != 16)
                                throw new Exception("Invalid boxId");
                            item.boxId = new Guid(_guid);
                            item.recipient = rdr.GetString(5);

                            result.Add(item);
                        }
                    }

                    return result;
                }
            }
        }

        /// <summary>
        /// Cancels the pop of items with the 'popstamp' from a previous pop operation
        /// </summary>
        /// <param name="popstamp"></param>
        public void PopCancel(Guid popstamp)
        {
            lock (_popLock)
            {
                // Make sure we only prep once 
                if (_popCancelCommand == null)
                {
                    _popCancelCommand = _database.CreateCommand();
                    _popCancelCommand.CommandText = "UPDATE outbox SET popstamp=NULL WHERE popstamp=$popstamp";

                    _pcancelparam1 = _popCancelCommand.CreateParameter();

                    _pcancelparam1.ParameterName = "$popstamp";
                    _popCancelCommand.Parameters.Add(_pcancelparam1);

                    _popCancelCommand.Prepare();
                }

                _pcancelparam1.Value = popstamp.ToByteArray();

                _database.BeginTransaction();
                _popCancelCommand.ExecuteNonQuery(_database);
            }
        }

        public void PopCancelList(Guid popstamp, List<Guid> listFileId)
        {
            lock (_popCancelListLock)
            {
                // Make sure we only prep once 
                if (_popCancelListCommand == null)
                {
                    _popCancelListCommand = _database.CreateCommand();
                    _popCancelListCommand.CommandText = "UPDATE outbox SET popstamp=NULL WHERE fileid=$fileid AND popstamp=$popstamp";

                    _pcancellistparam1 = _popCancelListCommand.CreateParameter();
                    _pcancellistparam1.ParameterName = "$popstamp";
                    _popCancelListCommand.Parameters.Add(_pcancellistparam1);

                    _pcancellistparam2 = _popCancelListCommand.CreateParameter();
                    _pcancellistparam2.ParameterName = "$fileid";
                    _popCancelListCommand.Parameters.Add(_pcancellistparam2);

                    _popCancelListCommand.Prepare();
                }

                _pcancellistparam1.Value = popstamp.ToByteArray();

                _database.BeginTransaction();

                using (_database.CreateCommitUnitOfWork())
                {
                    // I'd rather not do a TEXT statement, this seems safer but slower.
                    for (int i = 0; i < listFileId.Count; i++)
                    {
                        _pcancellistparam2.Value = listFileId[i].ToByteArray();
                        _popCancelListCommand.ExecuteNonQuery(_database);
                    }
                }
            }
        }


        /// <summary>
        /// Commits (removes) the items previously popped with the supplied 'popstamp'
        /// </summary>
        /// <param name="popstamp"></param>
        public void PopCommit(Guid popstamp)
        {
            lock (_popLock)
            {
                // Make sure we only prep once 
                if (_popCommitCommand == null)
                {
                    _popCommitCommand = _database.CreateCommand();
                    _popCommitCommand.CommandText = "DELETE FROM outbox WHERE popstamp=$popstamp";

                    _pcommitparam1 = _popCommitCommand.CreateParameter();
                    _pcommitparam1.ParameterName = "$popstamp";
                    _popCommitCommand.Parameters.Add(_pcommitparam1);

                    _popCommitCommand.Prepare();
                }

                _pcommitparam1.Value = popstamp.ToByteArray();
                _database.BeginTransaction();
                _popCommitCommand.ExecuteNonQuery(_database);
            }
        }


        /// <summary>
        /// Commits (removes) the items previously popped with the supplied 'popstamp'
        /// </summary>
        /// <param name="popstamp"></param>
        public void PopCommitList(Guid popstamp, List<Guid> listFileId)
        {
            lock (_popCommitListLock)
            {
                // Make sure we only prep once 
                if (_popCommitListCommand == null)
                {
                    _popCommitListCommand = _database.CreateCommand();
                    _popCommitListCommand.CommandText = "DELETE FROM outbox WHERE fileid=$fileid AND popstamp=$popstamp";

                    _pcommitlistparam1 = _popCommitListCommand.CreateParameter();
                    _pcommitlistparam1.ParameterName = "$popstamp";
                    _popCommitListCommand.Parameters.Add(_pcommitlistparam1);

                    _pcommitlistparam2 = _popCommitListCommand.CreateParameter();
                    _pcommitlistparam2.ParameterName = "$fileid";
                    _popCommitListCommand.Parameters.Add(_pcommitlistparam2);

                    _popCommitListCommand.Prepare();
                }

                _pcommitlistparam1.Value = popstamp.ToByteArray();

                _database.BeginTransaction();

                using (_database.CreateCommitUnitOfWork())
                {
                    // I'd rather not do a TEXT statement, this seems safer but slower.
                    for (int i = 0; i < listFileId.Count; i++)
                    {
                        _pcommitlistparam2.Value = listFileId[i].ToByteArray();
                        _popCommitListCommand.ExecuteNonQuery(_database);
                    }
                }
            }
        }


        /// <summary>
        /// Recover popped items older than the supplied UnixTime in seconds.
        /// This is how to recover popped items that were never processed for example on a server crash.
        /// Call with e.g. a time of more than 5 minutes ago.
        /// </summary>
        public void PopRecoverDead(UnixTimeUtc UnixTimeSeconds)
        {
            lock (_popLock)
            {
                if (_popRecoverCommand == null)
                {
                    _popRecoverCommand = _database.CreateCommand();
                    _popRecoverCommand.CommandText = "UPDATE outbox SET popstamp=NULL WHERE popstamp < $popstamp";

                    _pcrecoverparam1 = _popRecoverCommand.CreateParameter();

                    _pcrecoverparam1.ParameterName = "$popstamp";
                    _popRecoverCommand.Parameters.Add(_pcrecoverparam1);

                    _popRecoverCommand.Prepare();
                }

                _pcrecoverparam1.Value = SequentialGuid.CreateGuid(UnixTimeSeconds).ToByteArray(); // UnixTimeMiliseconds

                _database.BeginTransaction();
                _popRecoverCommand.ExecuteNonQuery(_database);
            }
        }
    }
}
