﻿using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Youverse.Core.Identity;

//
// ImFollowing - this class stores all the people that follow me.
// I.e. the people I need to notify when I update some content.
//

namespace Youverse.Core.Storage.Sqlite.IdentityDatabase
{
    public class TableImFollowing : TableImFollowingCRUD
    {
        public const int GUID_SIZE = 16; // Precisely 16 bytes for the ID key

        private SqliteCommand _select2Command = null;
        private SqliteParameter _s2param1 = null;
        private SqliteParameter _s2param2 = null;
        private SqliteParameter _s2param3 = null;
        private static object _select2Lock = new object();

        private SqliteCommand _select3Command = null;
        private SqliteParameter _s3param1 = null;
        private SqliteParameter _s3param2 = null;
        private static object _select3Lock = new object();

        public TableImFollowing(IdentityDatabase db) : base(db)
        {
        }

        ~TableImFollowing()
        {
        }

        public override void Dispose()
        {
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
        public new virtual List<ImFollowingRecord> Get(OdinId identity)
        {
            var r = base.Get(identity);

            if (r == null)
                r = new List<ImFollowingRecord>();

            return r;
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

                using (SqliteDataReader rdr = _select3Command.ExecuteReader(System.Data.CommandBehavior.Default, _database))
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

                    if ((n > 0) && rdr.Read())
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
                        $"SELECT DISTINCT identity FROM imfollowing WHERE (driveId=$driveId OR driveId=x'{Convert.ToHexString(Guid.Empty.ToByteArray())}') AND identity > $cursor ORDER BY identity ASC LIMIT $count;";

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

                _s2param1.Value = driveId.ToByteArray();
                _s2param2.Value = inCursor;
                _s2param3.Value = count + 1;                    // +1 to check for EOD on nextCursor

                using (SqliteDataReader rdr = _select2Command.ExecuteReader(System.Data.CommandBehavior.Default, _database))
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

                    if ((n > 0) && rdr.Read())
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
    }
}
