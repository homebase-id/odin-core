using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Identity;
using Org.BouncyCastle.Asn1.Ocsp;
using static Dapper.SqlMapper;

//
// FollowsMe - this class stores all the people that follow me.
// I.e. the people I need to notify when I update some content.
//

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableFollowsMe : TableFollowsMeCRUD
    {   
        public const int GUID_SIZE = 16; // Precisely 16 bytes for the ID key
        private readonly IdentityDatabase _db;

        public TableFollowsMe(IdentityDatabase db, CacheHelper cache) : base(db, cache)
        {
            _db = db;
        }

        ~TableFollowsMe()
        {
        }

        public override void Dispose()
        {
            base.Dispose();
            GC.SuppressFinalize(this);
        }

        public int Delete(OdinId identity, Guid driveId)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.Delete(conn, _db._identityId, identity.DomainName, driveId);
            }
        }

        public int Insert(FollowsMeRecord item)
        {
            item.identityId = _db._identityId;

            using (var conn = _db.CreateDisposableConnection())
            {
                return base.Insert(conn, item);
            }
        }


        /// <summary>
        /// For the given identity, return all drives being followed (and possibly Guid.Empty for everything)
        /// </summary>
        /// <param name="identity">The identity following you</param>
        /// <returns>List of driveIds (possibly includinig Guid.Empty for 'follow all')</returns>
        /// <exception cref="Exception"></exception>
        public List<FollowsMeRecord> Get(OdinId identity)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                var r = base.Get(conn, _db._identityId, identity.DomainName);

                if (r == null)
                    r = new List<FollowsMeRecord>();

                return r;
            }
        }


        public int DeleteByIdentity(OdinId identity)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                int n = 0;
                var r = base.Get(conn, _db._identityId, identity.DomainName);

                if (r == null)
                {
                    return 0;
                }
                
                conn.CreateCommitUnitOfWork(() =>
                {
                    for (int i = 0; i < r.Count; i++)
                    {
                        n += base.Delete(conn, _db._identityId, identity.DomainName, r[i].driveId);
                    }
                });

                return n;
            }
        }

        // Returns # records inserted (1 or 0)
        public int DeleteAndAddFollower(FollowsMeRecord r)
        {
            r.identityId = _db._identityId;
            using (var conn = _db.CreateDisposableConnection())
            {
                int n = 0;
                conn.CreateCommitUnitOfWork(() =>
                {
                    var followerList = base.Get(conn, _db._identityId, r.identity);
                    for (int i = 0; i < followerList.Count; i++)
                    {
                        base.Delete(conn, _db._identityId, followerList[i].identity, followerList[i].driveId);
                    }
                    n = base.Insert(conn, r);
                });
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
        public List<string> GetAllFollowers(int count, string inCursor, out string nextCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");

            if (inCursor == null)
                inCursor = "";

            using (var _select3Command = _database.CreateCommand())
            {
                _select3Command.CommandText =
                    $"SELECT DISTINCT identity FROM followsme WHERE identityId = $identityId AND identity > $cursor ORDER BY identity ASC LIMIT $count;";

                var _s3param1 = _select3Command.CreateParameter();
                var _s3param2 = _select3Command.CreateParameter();
                var _s3param3 = _select3Command.CreateParameter();

                _s3param1.ParameterName = "$cursor";
                _s3param2.ParameterName = "$count";
                _s3param3.ParameterName = "$identityId";

                _select3Command.Parameters.Add(_s3param1);
                _select3Command.Parameters.Add(_s3param2);
                _select3Command.Parameters.Add(_s3param3);

                _s3param1.Value = inCursor;
                _s3param2.Value = count + 1;
                _s3param3.Value = _db._identityId.ToByteArray();

                using (var conn = _db.CreateDisposableConnection())
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_select3Command, System.Data.CommandBehavior.Default))
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
        public List<string> GetFollowers(int count, Guid driveId, string inCursor, out string nextCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");

            if (inCursor == null)
                inCursor = "";

            using (var _select2Command = _database.CreateCommand())
            {
                _select2Command.CommandText =
                    $"SELECT DISTINCT identity FROM followsme WHERE identityId=$identityId AND (driveId=$driveId OR driveId=x'{Convert.ToHexString(Guid.Empty.ToByteArray())}') AND identity > $cursor ORDER BY identity ASC LIMIT $count;";

                var _s2param1 = _select2Command.CreateParameter();
                var _s2param2 = _select2Command.CreateParameter();
                var _s2param3 = _select2Command.CreateParameter();
                var _s2param4 = _select2Command.CreateParameter();

                _s2param1.ParameterName = "$driveId";
                _s2param2.ParameterName = "$cursor";
                _s2param3.ParameterName = "$count";
                _s2param4.ParameterName = "$identityId";

                _select2Command.Parameters.Add(_s2param1);
                _select2Command.Parameters.Add(_s2param2);
                _select2Command.Parameters.Add(_s2param3);
                _select2Command.Parameters.Add(_s2param4);

                _s2param1.Value = driveId.ToByteArray();
                _s2param2.Value = inCursor;
                _s2param3.Value = count + 1;
                _s2param4.Value = _db._identityId.ToByteArray();

                using (var conn = _db.CreateDisposableConnection())
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_select2Command, System.Data.CommandBehavior.Default))
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
