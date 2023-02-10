using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace Youverse.Core.Storage.SQLite.IdentityDatabase
{
    public class CircleItem
    {
        public Guid circleId;
        public byte[] data;
    }

    public class TableCircle : TableBase
    {
        public const int MAX_DATA_LENGTH = 65000;  // Some max value for the data

        private SQLiteCommand _insertCommand = null;
        private SQLiteParameter _iparam1 = null;
        private SQLiteParameter _iparam2 = null;
        private static Object _insertLock = new Object();

        private SQLiteCommand _deleteCommand = null;
        private SQLiteParameter _dparam1 = null;
        private static Object _deleteLock = new Object();

        private SQLiteCommand _selectCommand = null;
        private SQLiteParameter _sparam1 = null;
        private static Object _selectLock = new Object();

        private SQLiteCommand _select2Command = null;
        private static Object _select2Lock = new Object();

        public TableCircle(IdentityDatabase db) : base(db)
        {
        }

        ~TableCircle()
        {
        }

        public override void Dispose()
        {
            _insertCommand?.Dispose();
            _insertCommand = null;
            _deleteCommand?.Dispose();
            _deleteCommand = null;
            _selectCommand?.Dispose();
            _select2Command = null;
            _select2Command?.Dispose();
            _select2Command = null;
        }

        /// <summary>
        /// Table description:
        /// fileId is a SequentialGuid.CreateGuid() because it's unique & contains a timestamp
        /// priority not currently used, but an integer to indicate priority (lower is higher? or higher is higher? :)
        /// timestamp is the UnixTime in seconds for when this item was inserted into the DB (kind of not needed since we have the fileId)
        /// popstamp is a SequentialGuid.CreateGuid() used to handle multi-threaded popping of items in the inbox.
        ///    An item first needs to be popped (but isn't removed from the table yet)
        ///    Once the item is safely handled, the pop can be committed and the item is removed from the inbox.
        ///    There'll be a function to recover 'hanging' pops for threads that died.
        /// </summary>
        public override void EnsureTableExists(bool dropExisting = false)
        {
            using (var cmd = _database.CreateCommand())
            {
                if (dropExisting)
                {
                    cmd.CommandText = "DROP TABLE IF EXISTS circle;";
                    cmd.ExecuteNonQuery();
                }

                cmd.CommandText =
                    @"CREATE TABLE IF NOT EXISTS circle(
                     circleid BLOB UNIQUE NOT NULL, 
                     data BLOB,
                     UNIQUE(circleid)); "
                    + "CREATE INDEX if not exists circleididx ON circle(circleid);";

                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Only used for testing? Returns data
        /// </summary>
        public CircleItem Get(Guid circleId)
        {
            lock (_selectLock)
            {
                // Make sure we only prep once 
                if (_selectCommand == null)
                {
                    _selectCommand = _database.CreateCommand();
                    _selectCommand.CommandText =
                        $"SELECT data FROM circle WHERE circleid=$circleid";
                    _sparam1 = _selectCommand.CreateParameter();
                    _sparam1.ParameterName = "$circleid";
                    _selectCommand.Parameters.Add(_sparam1);
                    _selectCommand.Prepare();
                }

                _sparam1.Value = circleId;

                using (SQLiteDataReader rdr = _selectCommand.ExecuteReader(System.Data.CommandBehavior.SingleRow))
                {
                    if (!rdr.Read())
                        return null;

                    if (rdr.IsDBNull(0))
                        return null;

                    var item = new CircleItem();

                    item.circleId = circleId; // Should I duplicate it? :-/ Hm...

                    byte[] _tmpbuf = new byte[MAX_DATA_LENGTH+1];
                    long n = rdr.GetBytes(0, 0, _tmpbuf, 0, MAX_DATA_LENGTH+1);
                    if (n >= MAX_DATA_LENGTH+1)
                        throw new Exception("Too much data...");

                    item.data = new byte[n];
                    Buffer.BlockCopy(_tmpbuf, 0, item.data, 0, (int)n);

                    return item;
                }
            }
        }


        public List<CircleItem> GetAllCircles()
        {
            lock (_select2Lock)
            {
                // Make sure we only prep once 
                if (_select2Command == null)
                {
                    _select2Command = _database.CreateCommand();
                    _select2Command.CommandText = $"SELECT circleid,data FROM circle ORDER BY circleid";
                    _select2Command.Prepare();
                }

                var result = new List<CircleItem>();

                using (SQLiteDataReader rdr = _select2Command.ExecuteReader(System.Data.CommandBehavior.Default))
                {
                    byte[] _tmpbuf = new byte[16];

                    while (rdr.Read())
                    {
                        var item = new CircleItem();

                        // Get circleId
                        long n = rdr.GetBytes(0, 0, _tmpbuf, 0, 16);
                        if (n != 16)
                            throw new Exception("circleId invalid data size");
                        item.circleId = new Guid(_tmpbuf);

                        // Get data
                        n = rdr.GetBytes(1, 0, _tmpbuf, 0, MAX_DATA_LENGTH+1);
                        if (n >= MAX_DATA_LENGTH+1)
                            throw new Exception("Too much data...");
                        item.data = new byte[n];
                        Buffer.BlockCopy(_tmpbuf, 0, item.data, 0, (int)n);

                        result.Add(item);
                    }
                }

                return result;
            }
        }

        public void InsertCircle(Guid circleId, byte[] data)
        {
            if (data?.Length > MAX_DATA_LENGTH)
                throw new Exception("data too large.");

            lock (_insertLock)
            {
                // Make sure we only prep once 
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand();
                    _insertCommand.CommandText = @"INSERT INTO circle(circleid, data) "+
                                                  "VALUES ($circleid, $data)";

                    _iparam1 = _insertCommand.CreateParameter();
                    _iparam2 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_iparam1);
                    _insertCommand.Parameters.Add(_iparam2);
                    _iparam1.ParameterName = "$circleid";
                    _iparam2.ParameterName = "$data";

                    _insertCommand.Prepare();
                }

                _iparam1.Value = circleId.ToByteArray();
                _iparam2.Value = data;

                _database.BeginTransaction();
                _insertCommand.ExecuteNonQuery();
            }
        }


        public void DeleteCircle(Guid circleId)
        {
            lock (_deleteLock)
            {
                // Make sure we only prep once 
                if (_deleteCommand == null)
                {
                    _deleteCommand = _database.CreateCommand();
                    _deleteCommand.CommandText = @"DELETE FROM circlemember WHERE circleid=$circleid;"+
                                                  "DELETE FROM circle WHERE circleid=$circleid;";

                    _dparam1 = _deleteCommand.CreateParameter();
                    _deleteCommand.Parameters.Add(_dparam1);
                    _dparam1.ParameterName = "$circleid";

                    _deleteCommand.Prepare();
                }

                _dparam1.Value = circleId.ToByteArray();

                _database.BeginTransaction();
                _deleteCommand.ExecuteNonQuery();
            }
        }
    }
}
