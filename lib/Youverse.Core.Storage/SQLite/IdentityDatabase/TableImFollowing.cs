using System;
using System.Collections.Generic;
using System.Data.SQLite;

//
// ImFollowing - this class stores all the people that follow me.
// I.e. the people I need to notify when I update some content.
//

namespace Youverse.Core.Storage.SQLite.IdentityDatabase
{
    public class TableImFollowing : TableImFollowingCRUD
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
        private SQLiteParameter _s2param3 = null;
        private static object _select2Lock = new object();

        private SQLiteCommand _select3Command = null;
        private SQLiteParameter _s3param1 = null;
        private SQLiteParameter _s3param2 = null;
        private static object _select3Lock = new object();

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

            _select3Command?.Dispose();
            _select3Command = null;

            base.Dispose();
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
                        $"SELECT driveid, created FROM imfollowing WHERE identity=$identity";
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
                        var f = new ImFollowingItem();

                        if (rdr.IsDBNull(0))
                            f.driveid = Guid.Empty;
                        else
                        {
                            var n = rdr.GetBytes(0, 0, _tmpbuf, 0, 16);
                            if (n != GUID_SIZE)
                                throw new Exception("Not a GUID");
                            f.driveid = new Guid(_tmpbuf);
                        }
                        var d = rdr.GetInt64(1);
                        f.identity = identity;
                        f.created = new UnixTimeUtcUnique((ulong) d);
                        result.Add(f);
                    }

                    return result;
                }
            }
        }

        /// <summary>
        /// Return all followers, paged.
        /// </summary>
        /// <param name="count">Maximum number of identities per page</param>
        /// <param name="inCursor">If supplied then pick the next page after the supplied identity.</param>
        /// <returns>A sorted list of identities. If list size is smaller than count then you're finished</returns>
        /// <exception cref="Exception"></exception>
        public List<string> GetAllFollowers(int count, string inCursor, out string nextCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");

            if (inCursor == null)
                inCursor = "";

            lock (_select3Lock)
            {
                // Make sure we only prep once 
                if (_select3Command == null)
                {
                    _select3Command = _database.CreateCommand();
                    _select3Command.CommandText =
                        $"SELECT DISTINCT identity FROM imfollowing WHERE identity > $cursor ORDER BY identity ASC LIMIT $count;";

                    _s3param1 = _select3Command.CreateParameter();
                    _s3param1.ParameterName = "$cursor";
                    _select3Command.Parameters.Add(_s3param1);

                    _s3param2 = _select3Command.CreateParameter();
                    _s3param2.ParameterName = "$count";
                    _select3Command.Parameters.Add(_s3param2);

                    _select3Command.Prepare();
                }

                _s3param1.Value = inCursor;
                _s3param2.Value = count + 1; // +1 because we want to see if there are more records to set the nextCursor correctly

                using (SQLiteDataReader rdr = _select3Command.ExecuteReader(System.Data.CommandBehavior.Default))
                {
                    var result = new List<string>();

                    int n = 0;

                    while ((n < count) && rdr.Read())
                    {
                        n++;
                        var s = rdr.GetString(0);
                        if (s.Length < 1)
                            throw new Exception("Empty string");
                        result.Add(s);
                    }

                    if ((n > 0) && rdr.HasRows)
                    {
                        nextCursor = result[n - 1];
                    }
                    else
                    {
                        nextCursor = null; 
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
        /// <param name="inCursor">If supplied then pick the next page after the supplied identity.</param>
        /// <returns>A sorted list of identities. If list size is smaller than count then you're finished</returns>
        /// <exception cref="Exception"></exception>
        public List<string> GetFollowers(int count, Guid driveId, string inCursor, out string nextCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");

            if (inCursor == null)
                inCursor = "";

            lock (_select2Lock)
            {
                // Make sure we only prep once 
                if (_select2Command == null)
                {
                    _select2Command = _database.CreateCommand();
                    _select2Command.CommandText =
                        $"SELECT DISTINCT identity FROM imfollowing WHERE (driveid=$driveid OR driveid=x'{Convert.ToHexString(Guid.Empty.ToByteArray())}') AND identity > $cursor ORDER BY identity ASC LIMIT $count;";

                    _s2param1 = _select2Command.CreateParameter();
                    _s2param1.ParameterName = "$driveid";
                    _select2Command.Parameters.Add(_s2param1);

                    _s2param2 = _select2Command.CreateParameter();
                    _s2param2.ParameterName = "$cursor";
                    _select2Command.Parameters.Add(_s2param2);

                    _s2param3 = _select2Command.CreateParameter();
                    _s2param3.ParameterName = "$count";
                    _select2Command.Parameters.Add(_s2param3);

                    _select2Command.Prepare();
                }

                _s2param1.Value = driveId;
                _s2param2.Value = inCursor;
                _s2param3.Value = count + 1;                    // +1 to check for EOD on nextCursor

                using (SQLiteDataReader rdr = _select2Command.ExecuteReader(System.Data.CommandBehavior.Default))
                {
                    var result = new List<string>();

                    int n = 0;

                    while ((n < count) && rdr.Read())
                    {
                        n++;
                        var s = rdr.GetString(0);
                        if (s.Length < 1)
                            throw new Exception("Empty string");
                        result.Add(s);
                    }

                    if ((n > 0) && rdr.HasRows)
                    {
                        nextCursor = result[n - 1];
                    }
                    else
                    {
                        nextCursor = null;
                    }

                    return result;
                }
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
    }
}
