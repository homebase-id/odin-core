using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace Youverse.Core.Storage.SQLite.KeyValue
{
    public class OutboxItem
    {
        public byte[] boxId;
        public byte[] fileId;
        public UInt32 priority;
        public UnixTimeUtc timeStamp;
        public byte[] value;
    }

    public class TableOutbox: TableBase
    {
        const int MAX_VALUE_LENGTH = 65535;  // Stored value cannot be longer than this

        private SQLiteCommand _insertCommand = null;
        private SQLiteParameter _iparam1 = null;
        private SQLiteParameter _iparam2 = null;
        private SQLiteParameter _iparam3 = null;
        private SQLiteParameter _iparam4 = null;
        private SQLiteParameter _iparam5 = null;
        private static Object _insertLock = new Object();

        private SQLiteCommand _popCommand = null;
        private SQLiteParameter _pparam1 = null;
        private SQLiteParameter _pparam2 = null;
        private SQLiteParameter _pparam3 = null;
        private static Object _popLock = new Object();

        private SQLiteCommand _popAllCommand = null;
        private SQLiteParameter _paparam1 = null;
        private static Object _popAllLock = new Object();

        private SQLiteCommand _popCancelCommand = null;
        private SQLiteParameter _pcancelparam1 = null;

        private SQLiteCommand _popCancelListCommand = null;
        private SQLiteParameter _pcancellistparam1 = null;
        private SQLiteParameter _pcancellistparam2 = null;
        private static Object _popCancelListLock = new Object();

        private SQLiteCommand _popCommitCommand = null;
        private SQLiteParameter _pcommitparam1 = null;

        private SQLiteCommand _popCommitListCommand = null;
        private SQLiteParameter _pcommitlistparam1 = null;
        private SQLiteParameter _pcommitlistparam2 = null;
        private static Object _popCommitListLock = new Object();

        private SQLiteCommand _popRecoverCommand = null;
        private SQLiteParameter _pcrecoverparam1 = null;

        private SQLiteCommand _selectCommand = null;
        private SQLiteParameter _sparam1 = null;
        private static Object _selectLock = new Object();


        public TableOutbox(IdentityDatabase db) : base(db)
        {
        }

        ~TableOutbox()
        {
        }

        public override void Dispose()
        {
            _insertCommand?.Dispose();
            _insertCommand = null;

            _popCommand?.Dispose();
            _popCommand = null;

            _popCancelCommand?.Dispose();
            _popCancelCommand = null;

            _popCommitCommand?.Dispose();
            _popCommitCommand = null;

            _popRecoverCommand?.Dispose();
            _popRecoverCommand = null;

            _selectCommand?.Dispose();
            _selectCommand = null;
        }


        /// <summary>
        /// Table description:
        /// fileId is a SequentialGuid.CreateGuid() because it's unique & contains a timestamp
        /// priority not currently used, but an integer to indicate priority (lower is higher? or higher is higher? :)
        /// timestamp is the UnixTime in seconds for when this item was inserted into the DB (kind of not needed since we have the fileId)
        /// popstamp is a SequentialGuid.CreateGuid() used to handle multi-threaded popping of items in the outbox.
        ///    An item first needs to be popped (but isn't removed from the table yet)
        ///    Once the item is safely handled, the pop can be committed and the item is removed from the outbox.
        ///    There'll be a function to recover 'hanging' pops for threads that died.
        /// </summary>
        public override void EnsureTableExists(bool dropExisting = false)
        {
            using (var cmd = _database.CreateCommand())
            {
                if (dropExisting)
                {
                    cmd.CommandText = "DROP TABLE IF EXISTS outbox;";
                    cmd.ExecuteNonQuery();
                }

                cmd.CommandText =
                    @"CREATE TABLE if not exists outbox(
                     fileid BLOB NOT NULL, 
                     boxid BLOB NOT NULL,
                     priority INT NOT NULL,
                     timestamp INT NOT NULL,
                     value BLOB,
                     popstamp BLOB); "
                    + "CREATE INDEX if not exists outboxtimestampidx ON outbox(timestamp);"
                    + "CREATE INDEX if not exists outboxboxidx ON outbox(boxid);"
                    + "CREATE INDEX if not exists outboxpopidx ON outbox(popstamp);";

                // Get() is only used for testing. We don't have an index on fileId
                // because only Get() retrieves by the fileId()

                cmd.ExecuteNonQuery();
            }
        }


        public OutboxItem Get(byte[] fileId)
        {
            lock (_selectLock)
            {
                // Make sure we only prep once 
                if (_selectCommand == null)
                {
                    _selectCommand = _database.CreateCommand();
                    _selectCommand.CommandText =
                        $"SELECT priority, timestamp, value FROM outbox WHERE fileid=$fileid";
                    _sparam1 = _selectCommand.CreateParameter();
                    _sparam1.ParameterName = "$fileid";
                    _selectCommand.Parameters.Add(_sparam1);
                    _selectCommand.Prepare();
                }

                _sparam1.Value = fileId;

                using (SQLiteDataReader rdr = _selectCommand.ExecuteReader(System.Data.CommandBehavior.SingleRow))
                {
                    if (!rdr.Read())
                        return null;

                    if (rdr.IsDBNull(0))
                        return null;

                    var item = new OutboxItem();

                    item.fileId = fileId; // Should I duplicate it? :-/ Hm...
                    item.priority = (UInt32) rdr.GetInt32(0);
                    item.timeStamp = new UnixTimeUtc((UInt64) rdr.GetInt64(1));

                    if (rdr.IsDBNull(2))
                    {
                        item.value = null;
                    }
                    else
                    {
                        byte[] _tmpbuf = new byte[MAX_VALUE_LENGTH];
                        long n = rdr.GetBytes(2, 0, _tmpbuf, 0, MAX_VALUE_LENGTH);
                        if (n != 16)
                            throw new Exception("Unexpected fileId");
                        if (n >= MAX_VALUE_LENGTH)
                            throw new Exception("Too much data...");

                        item.value = new byte[n];
                        Buffer.BlockCopy(_tmpbuf, 0, item.value, 0, (int)n);
                    }

                    return item;
                }
            }
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="boxId">Is the outbox, e.g. the outbox for drive A</param>
        /// <param name="fileId">Should be a SequentialGuid (because it then contains the timestamp)</param>
        /// <param name="priority">Placeholder, but not currently used</param>
        /// <param name="value">Custom value, e.g. the appId or the senderId</param>
        public void InsertRow(byte[] boxId, byte[] fileId, int priority, byte[] value)
        {
            lock (_insertLock)
            {
                // Make sure we only prep once 
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand();
                    _insertCommand.CommandText = @"INSERT INTO outbox(boxid, fileid, priority, timestamp, popstamp, value) "+
                                                  "VALUES ($boxid, $fileid, $priority, $timestamp, NULL, $value)";

                    _iparam1 = _insertCommand.CreateParameter();
                    _iparam1.ParameterName = "$fileid";
                    _iparam2 = _insertCommand.CreateParameter();
                    _iparam2.ParameterName = "$priority";
                    _iparam3 = _insertCommand.CreateParameter();
                    _iparam3.ParameterName = "$timestamp";
                    _iparam4 = _insertCommand.CreateParameter();
                    _iparam4.ParameterName = "$value";
                    _iparam5 = _insertCommand.CreateParameter();
                    _iparam5.ParameterName = "$boxid";

                    _insertCommand.Parameters.Add(_iparam1);
                    _insertCommand.Parameters.Add(_iparam2);
                    _insertCommand.Parameters.Add(_iparam3);
                    _insertCommand.Parameters.Add(_iparam4);
                    _insertCommand.Parameters.Add(_iparam5);
                    _insertCommand.Prepare();
                }

                _iparam1.Value = fileId;
                _iparam2.Value = priority;
                _iparam3.Value = UnixTimeUtc.Now().milliseconds;
                _iparam4.Value = value;
                _iparam5.Value = boxId;

                _database.BeginTransaction();
                _insertCommand.ExecuteNonQuery();
            }
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
        public List<OutboxItem> Pop(byte[] boxId, int count, out byte[] popStamp)
        {
            lock (_popLock)
            {
                // Make sure we only prep once 
                if (_popCommand == null)
                {
                    _popCommand = _database.CreateCommand();
                    _popCommand.CommandText = "UPDATE outbox SET popstamp=$popstamp WHERE boxid=$boxid AND popstamp IS NULL ORDER BY timestamp ASC LIMIT $count; " +
                                              "SELECT fileid, priority, timestamp, value from outbox WHERE popstamp=$popstamp";

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

                List<OutboxItem> result = new List<OutboxItem>();

                _database.BeginTransaction();

                using (_database.CreateCommitUnitOfWork())
                {
                    using (SQLiteDataReader rdr = _popCommand.ExecuteReader(System.Data.CommandBehavior.Default))
                    {
                        OutboxItem item;

                        while (rdr.Read())
                        {
                            if (rdr.IsDBNull(0))
                                throw new Exception("Not possible");

                            item = new OutboxItem();
                            item.boxId = boxId;
                            item.fileId = new byte[16];
                            var n = rdr.GetBytes(0, 0, item.fileId, 0, 16);
                            if (n != 16)
                                throw new Exception("Invalid fileId");
                            item.priority = (UInt32)rdr.GetInt32(1);
                            item.timeStamp = new UnixTimeUtc((UInt64)rdr.GetInt64(2));

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

        public List<OutboxItem> PopAll(out byte[] popStamp)
        {
            lock (_popAllLock)
            {
                // Make sure we only prep once 
                if (_popAllCommand == null)
                {
                    _popAllCommand = _database.CreateCommand();
                    _popAllCommand.CommandText = "UPDATE outbox SET popstamp=$popstamp WHERE popstamp is NULL and fileId IN (SELECT fileid FROM outbox WHERE popstamp is NULL GROUP BY boxid ORDER BY timestamp ASC); " +
                                              "SELECT fileid, priority, timestamp, value, boxid from outbox WHERE popstamp=$popstamp";

                    _paparam1 = _popAllCommand.CreateParameter();
                    _paparam1.ParameterName = "$popstamp";
                    _popAllCommand.Parameters.Add(_paparam1);

                    _popAllCommand.Prepare();
                }

                popStamp = SequentialGuid.CreateGuid().ToByteArray();
                _paparam1.Value = popStamp;

                List<OutboxItem> result = new List<OutboxItem>();

                _database.BeginTransaction();

                using (_database.CreateCommitUnitOfWork())
                {
                    using (SQLiteDataReader rdr = _popAllCommand.ExecuteReader(System.Data.CommandBehavior.Default))
                    {
                        OutboxItem item;

                        while (rdr.Read())
                        {
                            if (rdr.IsDBNull(0))
                                throw new Exception("Not possible");

                            item = new OutboxItem();

                            item.fileId = new byte[16];
                            var n = rdr.GetBytes(0, 0, item.fileId, 0, 16);
                            if (n != 16)
                                throw new Exception("Invalid fileId");
                            item.priority = (UInt32)rdr.GetInt32(1);
                            item.timeStamp = new UnixTimeUtc((UInt64)rdr.GetInt64(2));

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

                            item.boxId = new byte[16];
                            n = rdr.GetBytes(4, 0, item.boxId, 0, 16);

                            if (n != 16)
                                throw new Exception("Invalid boxId");

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
                    _popCancelCommand.CommandText = "UPDATE outbox SET popstamp=NULL WHERE popstamp=$popstamp";

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

        public void PopCancelList(byte[] popstamp, List<byte[]> listFileId)
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

                _pcancellistparam1.Value = popstamp;

                _database.BeginTransaction();

                using (_database.CreateCommitUnitOfWork())
                {
                    // I'd rather not do a TEXT statement, this seems safer but slower.
                    for (int i = 0; i < listFileId.Count; i++)
                    {
                        _pcancellistparam2.Value = listFileId[i];
                        _popCancelListCommand.ExecuteNonQuery();
                    }
                }
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
                    _popCommitCommand.CommandText = "DELETE FROM outbox WHERE popstamp=$popstamp";

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
        /// Commits (removes) the items previously popped with the supplied 'popstamp'
        /// </summary>
        /// <param name="popstamp"></param>
        public void PopCommitList(byte[] popstamp, List<byte[]> listFileId)
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

                _pcommitlistparam1.Value = popstamp;

                _database.BeginTransaction();

                using (_database.CreateCommitUnitOfWork())
                {
                    // I'd rather not do a TEXT statement, this seems safer but slower.
                    for (int i = 0; i < listFileId.Count; i++)
                    {
                        _pcommitlistparam2.Value = listFileId[i];
                        _popCommitListCommand.ExecuteNonQuery();
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
                _popRecoverCommand.ExecuteNonQuery();
            }
        }
    }
}
