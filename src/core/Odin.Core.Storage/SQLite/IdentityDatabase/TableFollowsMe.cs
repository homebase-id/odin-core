using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;

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

        public TableFollowsMe(IdentityDatabase db, CacheHelper cache) : base(cache)
        {
            _db = db;
        }

        public override void Dispose()
        {
            base.Dispose();
            GC.SuppressFinalize(this);
        }

        public async Task<int> DeleteAsync(OdinId identity, Guid driveId)
        {
            using var conn = _db.CreateDisposableConnection();
            return await base.DeleteAsync (conn, _db._identityId, identity.DomainName, driveId);
        }

        public async Task<int> DeleteAndInsertManyAsync(OdinId identity, List<FollowsMeRecord> items)
        {
            int recordsInserted = 0;

            await DeleteByIdentityAsync(identity);

            using (var conn = _db.CreateDisposableConnection())
            {
                await conn.CreateCommitUnitOfWorkAsync(async () =>
                {
                    for (int i= 0; i < items.Count; i++)
                    {
                        items[i].identityId = _db._identityId;
                        recordsInserted += await base.InsertAsync(conn, items[i]);
                    }
                });
            }

            return recordsInserted;
        }


        public async Task<int> InsertAsync(FollowsMeRecord item)
        {
            item.identityId = _db._identityId;
            using var conn = _db.CreateDisposableConnection();
            return await base.InsertAsync(conn, item);
        }


        /// <summary>
        /// For the given identity, return all drives being followed (and possibly Guid.Empty for everything)
        /// </summary>
        /// <param name="identity">The identity following you</param>
        /// <returns>List of driveIds (possibly includinig Guid.Empty for 'follow all')</returns>
        /// <exception cref="Exception"></exception>
        public async Task<List<FollowsMeRecord>> GetAsync(OdinId identity)
        {
            using var conn = _db.CreateDisposableConnection();
            var r = await base.GetAsync(conn, _db._identityId, identity.DomainName);

            if (r == null)
                r = new List<FollowsMeRecord>();

            return r;
        }


        public async Task<int> DeleteByIdentityAsync(OdinId identity)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                int n = 0;
                var r = await base.GetAsync(conn, _db._identityId, identity.DomainName);

                if (r == null)
                {
                    return 0;
                }
                
                await conn.CreateCommitUnitOfWorkAsync(async () =>
                {
                    for (int i = 0; i < r.Count; i++)
                    {
                        n += await base.DeleteAsync(conn, _db._identityId, identity.DomainName, r[i].driveId);
                    }
                });

                return n;
            }
        }

        // Returns # records inserted (1 or 0)
        public async Task<int> DeleteAndAddFollowerAsync(FollowsMeRecord r)
        {
            r.identityId = _db._identityId;
            using (var conn = _db.CreateDisposableConnection())
            {
                int n = 0;
                await conn.CreateCommitUnitOfWorkAsync(async () =>
                {
                    var followerList = await base.GetAsync(conn, _db._identityId, r.identity);
                    for (int i = 0; i < followerList.Count; i++)
                    {
                        await base.DeleteAsync(conn, _db._identityId, followerList[i].identity, followerList[i].driveId);
                    }
                    n = await base.InsertAsync(conn, r);
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

            using (var select3Command = _db.CreateCommand())
            {
                select3Command.CommandText =
                    $"SELECT DISTINCT identity FROM followsme WHERE identityId = $identityId AND identity > $cursor ORDER BY identity ASC LIMIT $count;";

                var s3param1 = select3Command.CreateParameter();
                var s3param2 = select3Command.CreateParameter();
                var s3param3 = select3Command.CreateParameter();

                s3param1.ParameterName = "$cursor";
                s3param2.ParameterName = "$count";
                s3param3.ParameterName = "$identityId";

                select3Command.Parameters.Add(s3param1);
                select3Command.Parameters.Add(s3param2);
                select3Command.Parameters.Add(s3param3);

                s3param1.Value = inCursor;
                s3param2.Value = count + 1;
                s3param3.Value = _db._identityId.ToByteArray();

                using (var conn = _db.CreateDisposableConnection())
                {
                    // SEB:TODO make async
                    using (var rdr = conn.ExecuteReaderAsync(select3Command, System.Data.CommandBehavior.Default).Result)
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

            using (var _select2Command = _db.CreateCommand())
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
                    // SEB:TODO make async
                    using (var rdr = conn.ExecuteReaderAsync(_select2Command, System.Data.CommandBehavior.Default).Result)
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
