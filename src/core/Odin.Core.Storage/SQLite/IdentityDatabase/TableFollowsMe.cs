﻿using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

//
// FollowsMe - this class stores all the people that follow me.
// I.e. the people I need to notify when I update some content.
//

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableFollowsMe : TableFollowsMeCRUD
    {
        public const int GUID_SIZE = 16; // Precisely 16 bytes for the ID key


        public TableFollowsMe(IdentityDatabase db, CacheHelper cache) : base(db, cache)
        {
        }

        ~TableFollowsMe()
        {
        }

        public override void Dispose()
        {
            base.Dispose();
            GC.SuppressFinalize(this);
        }


        /// <summary>
        /// For the given identity, return all drives being followed (and possibly Guid.Empty for everything)
        /// </summary>
        /// <param name="identity">The identity following you</param>
        /// <returns>List of driveIds (possibly includinig Guid.Empty for 'follow all')</returns>
        /// <exception cref="Exception"></exception>
        public new virtual List<FollowsMeRecord> Get(DatabaseBase.DatabaseConnection conn, string identity)
        {
            var r = base.Get(conn, identity);

            if (r == null)
                r = new List<FollowsMeRecord>();

            return r;
        }


        public int DeleteByIdentity(DatabaseBase.DatabaseConnection conn, string identity)
        {
            if (identity == null)
                return 0;

            int n = 0;
            var r = Get(conn, identity);

            lock (conn._lock)
            {
                using (conn.CreateCommitUnitOfWork())
                {
                    for (int i = 0; i < r.Count; i++)
                    {
                        n += Delete(conn, identity, r[i].driveId);
                    }
                }

                return n;
            }
        }

        /// <summary>
        /// Return pages of identities that follow me; up to count size.
        /// Optionally supply a cursor to indicate the last identity processed (sorted ascending)
        /// </summary>
        /// <param name="count">Maximum number of identities per page</param>
        /// <param name="inCursor">If supplied then pick the next page after the supplied identity.</param>
        /// <returns>A sorted list of identities. If list size is smaller than count then you're finished</returns>
        /// <exception cref="Exception"></exception>
        public List<string> GetAllFollowers(DatabaseBase.DatabaseConnection conn, int count, string inCursor, out string nextCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");

            if (inCursor == null)
                inCursor = "";

            using (var _select3Command = _database.CreateCommand())
            {
                _select3Command.CommandText =
                    $"SELECT DISTINCT identity FROM followsme WHERE identity > $cursor ORDER BY identity ASC LIMIT $count;";

                var _s3param1 = _select3Command.CreateParameter();
                _s3param1.ParameterName = "$cursor";
                _select3Command.Parameters.Add(_s3param1);

                var _s3param2 = _select3Command.CreateParameter();
                _s3param2.ParameterName = "$count";
                _select3Command.Parameters.Add(_s3param2);

                _s3param1.Value = inCursor;
                _s3param2.Value = count + 1;

                lock (conn._lock)
                {
                    using (SqliteDataReader rdr = _database.ExecuteReader(conn, _select3Command, System.Data.CommandBehavior.Default))
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
        public List<string> GetFollowers(DatabaseBase.DatabaseConnection conn, int count, Guid driveId, string inCursor, out string nextCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");

            if (inCursor == null)
                inCursor = "";

            using (var _select2Command = _database.CreateCommand())
            {
                _select2Command.CommandText =
                    $"SELECT DISTINCT identity FROM followsme WHERE (driveId=$driveId OR driveId=x'{Convert.ToHexString(Guid.Empty.ToByteArray())}') AND identity > $cursor ORDER BY identity ASC LIMIT $count;";

                var _s2param1 = _select2Command.CreateParameter();
                _s2param1.ParameterName = "$driveId";
                _select2Command.Parameters.Add(_s2param1);

                var _s2param2 = _select2Command.CreateParameter();
                _s2param2.ParameterName = "$cursor";
                _select2Command.Parameters.Add(_s2param2);

                var _s2param3 = _select2Command.CreateParameter();
                _s2param3.ParameterName = "$count";
                _select2Command.Parameters.Add(_s2param3);

                _s2param1.Value = driveId.ToByteArray();
                _s2param2.Value = inCursor;
                _s2param3.Value = count + 1;

                lock (conn._lock)
                {
                    using (SqliteDataReader rdr = _database.ExecuteReader(conn, _select2Command, System.Data.CommandBehavior.Default))
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
}
