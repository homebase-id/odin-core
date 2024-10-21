using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Odin.Core.Identity;

//
// ImFollowing - this class stores all the people that follow me.
// I.e. the people I need to notify when I update some content.
//

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableImFollowing : TableImFollowingCRUD
    {
        private readonly IdentityDatabase _db;

        public TableImFollowing(IdentityDatabase db, CacheHelper cache) : base(cache)
        {
            this._db = db;
        }

        public override void Dispose()
        {
            base.Dispose();
            GC.SuppressFinalize(this);
        }

        public async Task<int> InsertAsync(ImFollowingRecord item)
        {
            item.identityId = _db._identityId;
            using var conn = _db.CreateDisposableConnection();
            return await base.InsertAsync(conn, item);
        }

        public async Task<int> DeleteAsync(OdinId identity, Guid driveId)
        {
            using var conn = _db.CreateDisposableConnection();
            return await base.DeleteAsync(conn, _db._identityId, identity, driveId);
        }

        /// <summary>
        /// For the given identity, return all drives being followed (and possibly Guid.Empty for everything)
        /// </summary>
        /// <param name="identity">The identity following you</param>
        /// <returns>List of driveIds (possibly includinig Guid.Empty for 'follow all')</returns>
        /// <exception cref="Exception"></exception>
        public async Task<List<ImFollowingRecord>> GetAsync(OdinId identity)
        {
            using var conn = _db.CreateDisposableConnection();
            var r = await base.GetAsync(conn, _db._identityId, identity);

            if (r == null)
                r = new List<ImFollowingRecord>();

            return r;
        }

        public async Task<int> DeleteByIdentityAsync(OdinId identity)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                int n = 0;
                var r = await base.GetAsync(conn, _db._identityId, identity);

                if (r == null)
                {
                    return 0;
                }

                await conn.CreateCommitUnitOfWorkAsync(async () =>
                {
                    for (int i = 0; i < r.Count; i++)
                    {
                        n += await DeleteAsync(conn, _db._identityId, identity, r[i].driveId);
                    }
                });

                return n;
            }
        }


        /// <summary>
        /// Return all followers, paged.
        /// </summary>
        /// <param name="count">Maximum number of identities per page</param>
        /// <param name="inCursor">If supplied then pick the next page after the supplied identity.</param>
        /// <returns>A sorted list of identities. If list size is smaller than count then you're finished</returns>
        /// <exception cref="Exception"></exception>
        public async Task<(List<string> followers, string nextCursor)>  GetAllFollowersAsync(int count, string inCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");

            if (inCursor == null)
                inCursor = "";

            using (var _select3Command = _db.CreateCommand())
            {
                _select3Command.CommandText =
                    $"SELECT DISTINCT identity FROM imfollowing WHERE identityId = $identityId AND identity > $cursor ORDER BY identity ASC LIMIT $count;";

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
                _s3param2.Value = count + 1; // +1 because we want to see if there are more records to set the nextCursor correctly
                _s3param3.Value = _db._identityId.ToByteArray();

                using (var conn = _db.CreateDisposableConnection())
                {
                    using (var rdr = await conn.ExecuteReaderAsync(_select3Command, System.Data.CommandBehavior.Default))
                    {
                        var result = new List<string>();
                        string nextCursor;

                        int n = 0;

                        while ((n < count) && await rdr.ReadAsync())
                        {
                            n++;
                            var s = rdr.GetString(0);
                            if (s.Length < 1)
                                throw new Exception("Empty string");
                            result.Add(s);
                        }

                        if ((n > 0) && await rdr.ReadAsync())
                        {
                            nextCursor = result[n - 1];
                        }
                        else
                        {
                            nextCursor = null;
                        }

                        return (result, nextCursor);
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
        public async Task<(List<string> followers, string nextCursor)>  GetFollowersAsync(int count, Guid driveId, string inCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");

            if (inCursor == null)
                inCursor = "";

            using (var _select2Command = _db.CreateCommand())
            {
                _select2Command.CommandText =
                    $"SELECT DISTINCT identity FROM imfollowing WHERE identityId = $identityId AND (driveId=$driveId OR driveId=x'{Convert.ToHexString(Guid.Empty.ToByteArray())}') AND identity > $cursor ORDER BY identity ASC LIMIT $count;";

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
                _s2param3.Value = count + 1; // +1 to check for EOD on nextCursor
                _s2param4.Value = _db._identityId.ToByteArray();

                using (var conn = _db.CreateDisposableConnection())
                {
                    using (var rdr = await conn.ExecuteReaderAsync(_select2Command, System.Data.CommandBehavior.Default))
                    {
                        var result = new List<string>();
                        string nextCursor;

                        int n = 0;

                        while ((n < count) && await rdr.ReadAsync())
                        {
                            n++;
                            var s = rdr.GetString(0);
                            if (s.Length < 1)
                                throw new Exception("Empty string");
                            result.Add(s);
                        }

                        if ((n > 0) && await rdr.ReadAsync())
                        {
                            nextCursor = result[n - 1];
                        }
                        else
                        {
                            nextCursor = null;
                        }

                        return (result, nextCursor);
                    }
                }
            }
        }
    }
}