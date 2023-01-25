using SQLitePCL;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using Youverse.Core.Util;

namespace Youverse.Core.Storage.SQLite.ServerDatabase
{
    public class CronItem
    {
        public Guid identityGuid;   // frodobaggins.me
        public Int32 type;          // E.g. 1 = outbox, 2 = ...
        public byte[] data;         // E.g. guid of the drive, json text, etc. - PROBABLY NOT NEEDED
        public UInt32 runCount;     // number of triggers / runs / attempts
        public UnixTimeUtc nextRun; // When this will trigger next
        public UnixTimeUtc lastRun; // The last time this was run
        public byte[] popStamp;     // fileId (unique time-stamp)
    }


    public class TableCron: TableBase
    {
        const int MAX_DATA_LENGTH = 65535;  // Stored data value cannot be longer than this

        private SQLiteCommand _insertCommand = null;
        private SQLiteParameter _iparam1 = null;
        private SQLiteParameter _iparam2 = null;
        private SQLiteParameter _iparam3 = null;
        private SQLiteParameter _iparam4 = null;
        private static Object _insertLock = new Object();

        private SQLiteCommand _popCommand = null;
        private SQLiteParameter _pparam1 = null;
        private SQLiteParameter _pparam2 = null;
        private static Object _popLock = new Object();

        private SQLiteCommand _popCancelListCommand = null;
        private SQLiteParameter _pcancellistparam1 = null;
        private static Object _popCancelListLock = new Object();

        private SQLiteCommand _popCommitListCommand = null;
        private SQLiteParameter _pcommitlistparam1 = null;
        private static Object _popCommitListLock = new Object();

        private SQLiteCommand _popRecoverCommand = null;
        private SQLiteParameter _pcrecoverparam1 = null;

        private SQLiteCommand _selectCommand = null;
        private SQLiteParameter _sparam1 = null;
        private SQLiteParameter _sparam2 = null;
        private static Object _selectLock = new Object();


        public TableCron(ServerDatabase db) : base(db)
        {
        }

        ~TableCron()
        {
        }

        public override void Dispose()
        {
            _insertCommand?.Dispose();
            _insertCommand = null;

            _popCommand?.Dispose();
            _popCommand = null;

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
        /// popstamp is a SequentialGuid.CreateGuid() used to handle multi-threaded popping of items in the cron.
        ///    An item first needs to be popped (but isn't removed from the table yet)
        ///    Once the item is safely handled, the pop can be committed and the item is removed from the cron.
        ///    There'll be a function to recover 'hanging' pops for threads that died.
        /// </summary>
        public override void EnsureTableExists(bool dropExisting = false)
        {
            using (var cmd = _database.CreateCommand())
            {
                if (dropExisting)
                {
                    cmd.CommandText = "DROP TABLE IF EXISTS cron;";
                    cmd.ExecuteNonQuery();
                }

                cmd.CommandText =
                    @"CREATE TABLE if not exists cron(
                     identityid BLOB NOT NULL, 
                     type INT NOT NULL,
                     data BLOB NOT NULL,
                     runcount INT NOT NULL,
                     nextrun INT NOT NULL,
                     lastrun INT NOT NULL,
                     popstamp BLOB,
                     UNIQUE(identityid, type)); "
                    + "CREATE INDEX if not exists cronnextrun ON cron(nextrun);";

                cmd.ExecuteNonQuery();
            }
        }


        public CronItem Get(Guid identityGuid, int type)
        {
            lock (_selectLock)
            {
                // Make sure we only prep once 
                if (_selectCommand == null)
                {
                    _selectCommand = _database.CreateCommand();
                    _selectCommand.CommandText =
                        $"SELECT identityid, type, data, runcount, nextrun, lastrun, popstamp FROM cron WHERE identityid=$identityid AND type=$type";
                    _sparam1 = _selectCommand.CreateParameter();
                    _sparam2 = _selectCommand.CreateParameter();
                    _sparam1.ParameterName = "$identityid";
                    _sparam2.ParameterName = "$type";
                    _selectCommand.Parameters.Add(_sparam1);
                    _selectCommand.Parameters.Add(_sparam2);
                    _selectCommand.Prepare();
                }

                _sparam1.Value = identityGuid.ToByteArray();
                _sparam2.Value = type;

                var _tmpbuf = new byte[MAX_DATA_LENGTH];
                var _g = new byte[16];

                using (SQLiteDataReader rdr = _selectCommand.ExecuteReader(System.Data.CommandBehavior.SingleRow))
                {
                    if (!rdr.Read())
                        return null;

                    var item = new CronItem();

                    // 0: identityid
                    long n = rdr.GetBytes(0, 0, _g, 0, _g.Length);
                    if (n != 16)
                        throw new Exception("IdentityId not a guid");
                    item.identityGuid = new Guid(_g);

                    // 1: type
                    item.type = rdr.GetInt32(1);

                    // 2: data
                    n = rdr.GetBytes(2, 0, _tmpbuf, 0, MAX_DATA_LENGTH);
                    if (n >= MAX_DATA_LENGTH)
                        throw new Exception("Too much data...");

                    item.data = new byte[n];
                    Buffer.BlockCopy(_tmpbuf, 0, item.data, 0, (int) n);

                    // 3: runCount
                    item.runCount = (UInt32) rdr.GetInt32(3);

                    // 4: nextRun
                    item.nextRun = new UnixTimeUtc((UInt64)rdr.GetInt64(4));

                    // 5: lastRun
                    item.lastRun = new UnixTimeUtc((UInt64)rdr.GetInt64(5));

                    // 6: popStamp
                    if (rdr.IsDBNull(6))
                    {
                        item.popStamp = null;
                    }
                    else
                    {
                        item.popStamp = new byte[16];
                        n = rdr.GetBytes(6, 0, _tmpbuf, 0, MAX_DATA_LENGTH);
                        if (n != 16)
                            throw new Exception("Incorrect popStamp");
                        Buffer.BlockCopy(_tmpbuf, 0, item.popStamp, 0, (int) n);
                    }
                    return item;
                }
            }
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="identityGuid">Is the cron, e.g. the cron for drive A</param>
        /// <param name="type">Placeholder, but not currently used</param>
        /// <param name="data">Should be a SequentialGuid (because it then contains the timestamp)</param>
        /// identityId, type must be unique
        public void UpsertRow(Guid identityGuid, int type, byte[] data)
        {
            if (identityGuid.Equals(Guid.Empty))
                throw new Exception("Can't be empty");

            lock (_insertLock)
            {
                // Make sure we only prep once 
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand();

                    _insertCommand.CommandText = 
                       @"INSERT INTO cron(identityid, type, data, runcount, nextrun, lastrun, popstamp) "+
                        "VALUES ($identityid, $type, $data, 0, $nextrun, 0, NULL) "+
                        "ON CONFLICT (identityid, type) DO UPDATE SET data=$data, runcount=0, nextrun=$nextrun";

                    _iparam1 = _insertCommand.CreateParameter();
                    _iparam1.ParameterName = "$identityid";
                    _iparam2 = _insertCommand.CreateParameter();
                    _iparam2.ParameterName = "$type";
                    _iparam3 = _insertCommand.CreateParameter();
                    _iparam3.ParameterName = "$data";
                    _iparam4 = _insertCommand.CreateParameter();
                    _iparam4.ParameterName = "$nextrun";

                    _insertCommand.Parameters.Add(_iparam1);
                    _insertCommand.Parameters.Add(_iparam2);
                    _insertCommand.Parameters.Add(_iparam3);
                    _insertCommand.Parameters.Add(_iparam4);

                    _insertCommand.Prepare();
                }

                _iparam1.Value = identityGuid;
                _iparam2.Value = type;
                _iparam3.Value = data;
                _iparam4.Value = UnixTimeUtc.Now().milliseconds;

                _database.BeginTransaction();
                _insertCommand.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Pops 'count' items from the cron. The items remain in the DB with the 'popstamp' unique identifier.
        /// Popstamp is used by the caller to release the items when they have been successfully processed, or
        /// to cancel the transaction and restore the items to the cron.
        /// </summary
        /// <param name="boxId">Is the cron to pop from, e.g. Drive A, or App B</param>
        /// <param name="count">How many items to 'pop' (reserve)</param>
        /// <param name="popStamp">The unique identifier for the items reserved for pop</param>
        /// <returns></returns>
        public List<CronItem> Pop(int count, out byte[] popStamp)
        {
            // TODO, maybe you can also checkout a TYPE. e.g. Give me outbox items.

            lock (_popLock)
            {
                // Make sure we only prep once 
                if (_popCommand == null)
                {
                    _popCommand = _database.CreateCommand();

                    //_popCommand.CommandText =
                    //    "UPDATE cron SET popstamp=$popstamp " +
                    //    "WHERE id IN (SELECT id FROM cron ORDER BY nextrun ASC LIMIT 10); " +
                    //    "SELECT identityid, type, data, runcount, lastrun, nextrun FROM cron WHERE popstamp=$popstamp";

                    _popCommand.CommandText =
                        "UPDATE cron SET popstamp=$popstamp, runcount=runcount+1, nextRun = 1000*(60 * power(2, min(runcount, 10)) + unixepoch()) " +
                        "WHERE (popstamp IS NULL) ORDER BY nextrun ASC LIMIT $count; " +
                        "SELECT identityid, type, data, runcount, lastrun, nextrun FROM cron WHERE popstamp=$popstamp";

                    _pparam1 = _popCommand.CreateParameter();
                    _pparam1.ParameterName = "$popstamp";
                    _popCommand.Parameters.Add(_pparam1);

                    _pparam2 = _popCommand.CreateParameter();
                    _pparam2.ParameterName = "$count";
                    _popCommand.Parameters.Add(_pparam2);

                    _popCommand.Prepare();
                }

                popStamp = SequentialGuid.CreateGuid().ToByteArray();
                _pparam1.Value = popStamp;
                _pparam2.Value = count;

                List<CronItem> result = new List<CronItem>();

                _database.BeginTransaction();

                using (SQLiteDataReader rdr = _popCommand.ExecuteReader(System.Data.CommandBehavior.Default))
                {
                    CronItem item;
                    byte[] _tmpbuf = new byte[MAX_DATA_LENGTH];
                    byte[] _g = new byte[16];

                    while (rdr.Read())
                    {
                        item = new CronItem();

                        // 0: identityId
                        var n = rdr.GetBytes(0, 0, _g, 0, _g.Length);
                        if (n != 16)
                            throw new Exception("Invalid identityid GUID size");
                        item.identityGuid = new Guid(_g);

                        // 1: type
                        item.type = (Int32)rdr.GetInt32(1);

                        // 2: data
                        n = rdr.GetBytes(2, 0, _tmpbuf, 0, MAX_DATA_LENGTH);
                        if (n >= MAX_DATA_LENGTH)
                            throw new Exception("Too much data...");
                        item.data = new byte[n];
                        Buffer.BlockCopy(_tmpbuf, 0, item.data, 0, (int)n);

                        // 3: runcount
                        item.runCount = (UInt32)rdr.GetInt32(3);

                        // 4: lastrun
                        item.lastRun = new UnixTimeUtc((UInt64)rdr.GetInt64(4));

                        // 4: nextrun
                        item.nextRun = new UnixTimeUtc((UInt64)rdr.GetInt64(5));

                        item.popStamp = popStamp;

                        result.Add(item);
                    }
                }

                return result;
            }
        }



        /// <summary>
        /// The List<> of identityIds will be unpopped, i.e. they're back in the CRON table and are active
        /// </summary>
        /// <param name="listIdentityId"></param>
        public void PopCancelList(List<Guid> listIdentityId)
        {
            lock (_popCancelListLock)
            {
                // Make sure we only prep once 
                if (_popCancelListCommand == null)
                {
                    _popCancelListCommand = _database.CreateCommand();
                    _popCancelListCommand.CommandText = "UPDATE cron SET popstamp=NULL WHERE identityid=$identityid";

                    _pcancellistparam1 = _popCancelListCommand.CreateParameter();
                    _pcancellistparam1.ParameterName = "$identityid";
                    _popCancelListCommand.Parameters.Add(_pcancellistparam1);

                    _popCancelListCommand.Prepare();
                }

                _database.BeginTransaction();

                using (_database.CreateCommitUnitOfWork())
                {
                    // I'd rather not do a TEXT statement, this seems safer but slower.
                    for (int i = 0; i < listIdentityId.Count; i++)
                    {
                        _pcancellistparam1.Value = listIdentityId[i];
                        _popCancelListCommand.ExecuteNonQuery();
                    }
                }
            }
        }


        /// <summary>
        /// Finally commits (removes) the items previously popped using Pop()
        /// </summary>
        public void PopCommitList(List<Guid> listIdentityId)
        {
            lock (_popCommitListLock)
            {
                // Make sure we only prep once 
                if (_popCommitListCommand == null)
                {
                    _popCommitListCommand = _database.CreateCommand();
                    _popCommitListCommand.CommandText = "DELETE FROM cron WHERE identityid=$identityid";

                    _pcommitlistparam1 = _popCommitListCommand.CreateParameter();
                    _pcommitlistparam1.ParameterName = "$identityid";
                    _popCommitListCommand.Parameters.Add(_pcommitlistparam1);

                    _popCommitListCommand.Prepare();
                }

                _database.BeginTransaction();

                using (_database.CreateCommitUnitOfWork())
                {
                    // I'd rather not do a TEXT statement, this seems safer but slower.
                    for (int i = 0; i < listIdentityId.Count; i++)
                    {
                        _pcommitlistparam1.Value = listIdentityId[i];
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
        public void PopRecoverDead(UnixTimeUtc t)
        {
            lock (_popLock)
            {
                if (_popRecoverCommand == null)
                {
                    _popRecoverCommand = _database.CreateCommand();
                    _popRecoverCommand.CommandText = "UPDATE cron SET popstamp=NULL WHERE popstamp <= $popstamp";

                    _pcrecoverparam1 = _popRecoverCommand.CreateParameter();

                    _pcrecoverparam1.ParameterName = "$popstamp";
                    _popRecoverCommand.Parameters.Add(_pcrecoverparam1);

                    _popRecoverCommand.Prepare();
                }

                _pcrecoverparam1.Value = SequentialGuid.CreateGuid(t).ToByteArray();

                _database.BeginTransaction();
                _popRecoverCommand.ExecuteNonQuery();
            }
        }
    }
}
