using System;
using System.Collections.Generic;
using System.Data.SQLite;

//
// FollowsMe - this class stores all the people that follow me.
// I.e. the people I need to notify when I update some content.
//

namespace Youverse.Core.Storage.SQLite.IdentityDatabase
{
    public class TableFollowsMe : TableFollowsMeCRUD
    {
        public const int GUID_SIZE = 16; // Precisely 16 bytes for the ID key

        private SQLiteCommand _deleteCommand = null;
        private SQLiteParameter _dparam1 = null;
        private static object _deleteLock = new object();

        private SQLiteCommand _select2Command = null;
        private SQLiteParameter _s2param1 = null;
        private SQLiteParameter _s2param2 = null;
        private SQLiteParameter _s2param3 = null;
        private static object _select2Lock = new object();

        public TableFollowsMe(IdentityDatabase db) : base(db)
        {
        }

        ~TableFollowsMe()
        {
        }

        public override void Dispose()
        {
            _deleteCommand?.Dispose();
            _deleteCommand = null;

            _select2Command?.Dispose();
            _select2Command = null;

            base.Dispose();
        }


        /// <summary>
        /// For the given identity, return all drives being followed (and possibly Guid.Empty for everything)
        /// </summary>
        /// <param name="identity">The identity following you</param>
        /// <returns>List of driveIds (possibly includinig Guid.Empty for 'follow all')</returns>
        /// <exception cref="Exception"></exception>
        public new virtual List<FollowsMeItem> Get(string identity)
        {
            var r = base.Get(identity);

            if (r == null)
                r = new List<FollowsMeItem>();

            return r;
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
                        $"SELECT DISTINCT identity FROM followsme WHERE (driveId=$driveId OR driveId=x'{Convert.ToHexString(Guid.Empty.ToByteArray())}') AND identity > $cursor ORDER BY identity ASC LIMIT $count;";

                    _s2param1 = _select2Command.CreateParameter();
                    _s2param1.ParameterName = "$driveId";
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
                _s2param3.Value = count + 1;

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
                        nextCursor = result[n-1];
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
                    _deleteCommand.CommandText = @"DELETE FROM followsme WHERE identity=$identity;";
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
