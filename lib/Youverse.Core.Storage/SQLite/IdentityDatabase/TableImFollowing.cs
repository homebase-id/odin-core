using System;
using System.Collections.Generic;
using System.Data.SQLite;

//
// ImFollowing - this class stores all the people that follow me.
// I.e. the people I need to notify when I update some content.
//

namespace Youverse.Core.Storage.SQLite.IdentityDatabase
{
    public class ImFollowingItem
    {
        public string identity;
        public UnixTimeUtc timeStamp;
        public Guid driveId;  // I suppose this is the infamous 'driveAlias' :-) 
    }

    public class TableImFollowing : TableBase
    {
        public const int GUID_SIZE = 16; // Precisely 16 bytes for the ID key

        private SQLiteCommand _insertCommand = null;
        private SQLiteParameter _iparam1 = null;
        private SQLiteParameter _iparam2 = null;
        private SQLiteParameter _iparam3 = null;
        private static object _insertLock = new object();

        private SQLiteCommand _deleteCommand = null;
        private SQLiteParameter _dparam1 = null;
        private static object _deleteLock = new object();
        private SQLiteCommand _deleteCommand2 = null;
        private SQLiteParameter _dparam2_1 = null;
        private SQLiteParameter _dparam2_2 = null;

        private SQLiteCommand _selectCommand = null;
        private SQLiteParameter _sparam1 = null;
        private static object _selectLock = new object();

        private SQLiteCommand _select2Command = null;
        private SQLiteParameter _s2param1 = null;
        private SQLiteParameter _s2param2 = null;
        private static object _select2Lock = new object();

        public TableImFollowing(IdentityDatabase db) : base(db)
        {
        }

        ~TableImFollowing()
        {
        }

        public override void Dispose()
        {
            _insertCommand?.Dispose();
            _insertCommand = null;

            _deleteCommand?.Dispose();
            _deleteCommand = null;

            _deleteCommand2?.Dispose();
            _deleteCommand2 = null;

            _selectCommand?.Dispose();
            _selectCommand = null;

            _select2Command?.Dispose();
            _select2Command = null;
        }

        /// <summary>
        /// 
        /// </summary>
        public override void EnsureTableExists(bool dropExisting = false)
        {
            using (var cmd = _database.CreateCommand())
            {
                if (dropExisting)
                {
                    cmd.CommandText = "DROP TABLE IF EXISTS imfollowing;";
                    cmd.ExecuteNonQuery();
                }

                cmd.CommandText =
                    @"CREATE TABLE IF NOT EXISTS imfollowing(
                     identity STRING NOT NULL,
                     timestamp INT NOT NULL,
                     driveid BLOB NOT NULL,
                     UNIQUE(identity,driveid)); "
                    + "CREATE INDEX if not exists imfollowingidentityidx ON imfollowing(identity);";

                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// For the given identity, return all drives being followed (and possibly Guid.Empty for everything)
        /// </summary>
        /// <param name="identity">The identity following you</param>
        /// <returns>List of driveIds (possibly includinig Guid.Empty for 'follow all')</returns>
        /// <exception cref="Exception"></exception>
        public List<ImFollowingItem> Get(string identity)
        {
            if (identity == null || identity.Length < 1)
                throw new Exception("identity cannot be NULL or empty.");

            lock (_selectLock)
            {
                // Make sure we only prep once 
                if (_selectCommand == null)
                {
                    _selectCommand = _database.CreateCommand();
                    _selectCommand.CommandText =
                        $"SELECT driveid, timestamp FROM imfollowing WHERE identity=$identity";
                    _sparam1 = _selectCommand.CreateParameter();
                    _sparam1.ParameterName = "$identity";
                    _selectCommand.Parameters.Add(_sparam1);
                    _selectCommand.Prepare();
                }

                _sparam1.Value = identity;

                using (SQLiteDataReader rdr = _selectCommand.ExecuteReader(System.Data.CommandBehavior.Default))
                {
                    var result = new List<ImFollowingItem>();
                    byte[] _tmpbuf = new byte[16];
                    var fi = new ImFollowingItem();

                    while (rdr.Read())
                    {
                        var n = rdr.GetBytes(0, 0, _tmpbuf, 0, 16);
                        if (n != GUID_SIZE)
                            throw new Exception("Not a GUID");
                        var d = rdr.GetInt64(1);
                        var f = new ImFollowingItem();
                        f.identity = identity;
                        f.timeStamp = new UnixTimeUtc((ulong) d);
                        f.driveId = new Guid(_tmpbuf);
                        result.Add(f);
                    }

                    return result;
                }
            }
        }

        /// <summary>
        /// Return pages of identities, following driveId, up to count size.
        /// Optionally supply a cursor to indicate the last identity processed (sorted ascending)
        /// </summary>
        /// <param name="count">Maximum number of identities per page</param>
        /// <param name="driveId">The drive they're following that you want to get a list for</param>
        /// <param name="cursor">If supplied then pick the next page after the supplied identity.</param>
        /// <returns>A sorted list of identities. If list size is smaller than count then you're finished</returns>
        /// <exception cref="Exception"></exception>
        public List<string> GetFollowers(int count, Guid driveId, string cursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");

            if (cursor == null)
                cursor = "";

            lock (_select2Lock)
            {
                // Make sure we only prep once 
                if (_select2Command == null)
                {
                    _select2Command = _database.CreateCommand();
                    _select2Command.CommandText =
                        $"SELECT DISTINCT identity FROM imfollowing WHERE (driveid=$driveid OR driveid=x'{Convert.ToHexString(Guid.Empty.ToByteArray())}') AND identity > $cursor ORDER BY identity ASC LIMIT {count}";
                    _s2param1 = _select2Command.CreateParameter();
                    _s2param1.ParameterName = "$driveid";
                    _select2Command.Parameters.Add(_s2param1);

                    _s2param2 = _select2Command.CreateParameter();
                    _s2param2.ParameterName = "$cursor";
                    _select2Command.Parameters.Add(_s2param2);

                    _select2Command.Prepare();
                }

                _s2param1.Value = driveId;
                _s2param2.Value = cursor;

                using (SQLiteDataReader rdr = _select2Command.ExecuteReader(System.Data.CommandBehavior.Default))
                {
                    var result = new List<string>();

                    int n = 0;

                    while (rdr.Read() && n < count)
                    {
                        n++;
                        var s = rdr.GetString(0);
                        if (s.Length < 1)
                            throw new Exception("Empty string");
                        result.Add(s);
                    }

                    return result;
                }
            }
        }



        /// <summary>
        /// Add a new follower that's following you, for the specified drive. Use Guid.Empty as a "full follow of everything".
        /// </summary>
        /// <param name="identity"></param>
        /// <param name="driveId">if Guid.Empty will follow 'everything' otherwise driveid bring followed</param>
        /// <exception cref="Exception"></exception>
        public void InsertFollower(string identity, Guid? driveId)
        {
            if (driveId == null)
                driveId = Guid.Empty;
            else
            {
                if (driveId == Guid.Empty)
                    throw new Exception("Guid.Empty is reserved for 'all'.");
            }

            if (identity == null || identity.Length < 1)
                throw new Exception("identity can't be NULL or empty.");

            lock (_insertLock)
            {
                // Make sure we only prep once 
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand();
                    _insertCommand.CommandText = @"INSERT INTO imfollowing(identity, timestamp, driveid) " +
                                                  "VALUES ($identity, $timestamp, $driveid)";

                    _iparam1 = _insertCommand.CreateParameter();
                    _iparam2 = _insertCommand.CreateParameter();
                    _iparam3 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_iparam1);
                    _insertCommand.Parameters.Add(_iparam2);
                    _insertCommand.Parameters.Add(_iparam3);
                    _iparam1.ParameterName = "$identity";
                    _iparam2.ParameterName = "$timestamp";
                    _iparam3.ParameterName = "$driveid";

                    _insertCommand.Prepare();
                }

                _iparam1.Value = identity;
                _iparam2.Value = UnixTimeUtc.Now().milliseconds;
                _iparam3.Value = driveId;

                _database.BeginTransaction();
                _insertCommand.ExecuteNonQuery();
            }
        }


        /// <summary>
        /// For the identity following you, delete all follows.
        /// </summary>
        /// <param name="identity">The identity following you</param>
        /// <exception cref="Exception"></exception>
        public void DeleteFollower(string identity)
        {
            if (identity == null || identity.Length < 1)
                throw new Exception("identity cannot be NULL or empty");

            lock (_deleteLock)
            {
                // Make sure we only prep once 
                if (_deleteCommand == null)
                {
                    _deleteCommand = _database.CreateCommand();
                    _deleteCommand.CommandText = @"DELETE FROM imfollowing WHERE identity=$identity;";
                    _dparam1 = _deleteCommand.CreateParameter();
                    _deleteCommand.Parameters.Add(_dparam1);
                    _dparam1.ParameterName = "$identity";

                    _deleteCommand.Prepare();
                }

                _dparam1.Value = identity;

                _database.BeginTransaction();
                _deleteCommand.ExecuteNonQuery();
            }
        }


        /// <summary>
        /// Delete following for the specified drive.
        /// </summary>
        /// <param name="identity">The identity following you</param>
        /// <param name="driveId">The drive you want to unfollow</param>
        /// <exception cref="Exception"></exception>
        public void DeleteFollower(string identity, Guid driveId)
        {
            if (identity == null || identity.Length < 1)
                throw new Exception("identity cannot be NULL or empty");

            lock (_deleteLock)
            {
                // Make sure we only prep once 
                if (_deleteCommand2 == null)
                {
                    _deleteCommand2 = _database.CreateCommand();
                    _deleteCommand2.CommandText = @"DELETE FROM imfollowing WHERE identity=$identity AND driveid=$driveid;";

                    _dparam2_1 = _deleteCommand2.CreateParameter();
                    _deleteCommand2.Parameters.Add(_dparam2_1);
                    _dparam2_1.ParameterName = "$identity";

                    _dparam2_2 = _deleteCommand2.CreateParameter();
                    _deleteCommand2.Parameters.Add(_dparam2_2);
                    _dparam2_2.ParameterName = "$driveid";

                    _deleteCommand2.Prepare();
                }

                _dparam2_1.Value = identity;
                _dparam2_2.Value = driveId;

                _database.BeginTransaction();
                _deleteCommand2.ExecuteNonQuery();
            }
        }
    }
}
