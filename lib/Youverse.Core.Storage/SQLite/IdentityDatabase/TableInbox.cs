using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace Youverse.Core.Storage.SQLite.IdentityDatabase
{
    public class TableInbox : TableInboxCRUD
    {
        const int MAX_VALUE_LENGTH = 65535;  // Stored value cannot be longer than this

        private SQLiteCommand _popCommand = null;
        private SQLiteParameter _pparam1 = null;
        private SQLiteParameter _pparam2 = null;
        private SQLiteParameter _pparam3 = null;
        private static Object _popLock = new Object();

        private SQLiteCommand _popCancelCommand = null;
        private SQLiteParameter _pcancelparam1 = null;

        private SQLiteCommand _popCommitCommand = null;
        private SQLiteParameter _pcommitparam1 = null;

        private SQLiteCommand _popRecoverCommand = null;
        private SQLiteParameter _pcrecoverparam1 = null;


        public TableInbox(IdentityDatabase db) : base(db)
        {
        }

        ~TableInbox()
        {
        }

        public override void Dispose()
        {
            _popCommand?.Dispose();
            _popCommand = null;

            _popCancelCommand?.Dispose();
            _popCancelCommand = null;

            _popCommitCommand?.Dispose();
            _popCommitCommand = null;

            _popRecoverCommand?.Dispose();
            _popRecoverCommand = null;

            base.Dispose();
        }

        /// <summary>
        /// Pops 'count' items from the inbox. The items remain in the DB with the 'popstamp' unique identifier.
        /// Popstamp is used by the caller to release the items when they have been successfully processed, or
        /// to cancel the transaction and restore the items to the inbox.
        /// </summary
        /// <param name="boxId">Is the inbox to pop from, e.g. Drive A, or App B</param>
        /// <param name="count">How many items to 'pop' (reserve)</param>
        /// <param name="popStamp">The unique identifier for the items reserved for pop</param>
        /// <returns></returns>
        public List<InboxItem> Pop(Guid boxId, int count, out byte[] popStamp)
        {
            lock (_popLock)
            {
                // Make sure we only prep once 
                if (_popCommand == null)
                {
                    _popCommand = _database.CreateCommand();
                    _popCommand.CommandText = "UPDATE inbox SET popstamp=$popstamp WHERE boxid=$boxid AND popstamp IS NULL ORDER BY timeStamp ASC LIMIT $count; " +
                                              "SELECT fileid, priority, timeStamp, value from inbox WHERE popstamp=$popstamp";

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

                popStamp = SequentialGuid.CreateGuid().ToByteArray();
                _pparam1.Value = popStamp;
                _pparam2.Value = count;
                _pparam3.Value = boxId;

                _database.BeginTransaction();

                using (_database.CreateCommitUnitOfWork())
                {
                    List<InboxItem> result = new List<InboxItem>();
                    using (SQLiteDataReader rdr = _popCommand.ExecuteReader(System.Data.CommandBehavior.Default))
                    {
                        InboxItem item;

                        while (rdr.Read())
                        {
                            if (rdr.IsDBNull(0))
                                throw new Exception("Not possible");

                            item = new InboxItem();
                            item.boxId = boxId;
                            var _guid = new byte[16];
                            var n = rdr.GetBytes(0, 0, _guid, 0, 16);
                            if (n != 16)
                                throw new Exception("Invalid fileId");
                            item.fileId = new Guid(_guid);
                            item.priority = (Int32)rdr.GetInt32(1);
                            if (rdr.IsDBNull(2))
                                throw new Exception("wooot");

                            var l = rdr.GetInt64(2);
                            item.timeStamp = new UnixTimeUtc((UInt64) l);

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
        public void PopCancel(byte[] popstamp)
        {
            lock (_popLock)
            {
                // Make sure we only prep once 
                if (_popCancelCommand == null)
                {
                    _popCancelCommand = _database.CreateCommand();
                    _popCancelCommand.CommandText = "UPDATE inbox SET popstamp=NULL WHERE popstamp=$popstamp";

                    _pcancelparam1 = _popCancelCommand.CreateParameter();

                    _pcancelparam1.ParameterName = "$popstamp";
                    _popCancelCommand.Parameters.Add(_pcancelparam1);

                    _popCancelCommand.Prepare();
                }

                _pcancelparam1.Value = popstamp;
                _database.BeginTransaction();
                _popCancelCommand.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Commits (removes) the items previously popped with the supplied 'popstamp'
        /// </summary>
        /// <param name="popstamp"></param>
        public void PopCommit(byte[] popstamp)
        {
            lock (_popLock)
            {
                // Make sure we only prep once 
                if (_popCommitCommand == null)
                {
                    _popCommitCommand = _database.CreateCommand();
                    _popCommitCommand.CommandText = "DELETE FROM inbox WHERE popstamp=$popstamp";

                    _pcommitparam1 = _popCommitCommand.CreateParameter();
                    _pcommitparam1.ParameterName = "$popstamp";
                    _popCommitCommand.Parameters.Add(_pcommitparam1);

                    _popCommitCommand.Prepare();
                }

                _pcommitparam1.Value = popstamp;
                _database.BeginTransaction();
                _popCommitCommand.ExecuteNonQuery();
            }
        }


        /// <summary>
        /// Recover popped items older than the supplied UnixTime in seconds.
        /// This is how to recover popped items that were never processed for example on a server crash.
        /// Call with e.g. a time of more than 5 minutes ago.
        /// </summary>
        public void PopRecoverDead(UnixTimeUtc ut)
        {
            lock (_popLock)
            {
                if (_popRecoverCommand == null)
                {
                    _popRecoverCommand = _database.CreateCommand();
                    _popRecoverCommand.CommandText = "UPDATE inbox SET popstamp=NULL WHERE popstamp < $popstamp";

                    _pcrecoverparam1 = _popRecoverCommand.CreateParameter();

                    _pcrecoverparam1.ParameterName = "$popstamp";
                    _popRecoverCommand.Parameters.Add(_pcrecoverparam1);

                    _popRecoverCommand.Prepare();
                }

                _pcrecoverparam1.Value = SequentialGuid.CreateGuid(new UnixTimeUtc(ut)).ToByteArray(); // UnixTimeMiliseconds

                _database.BeginTransaction();
                _popRecoverCommand.ExecuteNonQuery();
            }
        }
    }
}
