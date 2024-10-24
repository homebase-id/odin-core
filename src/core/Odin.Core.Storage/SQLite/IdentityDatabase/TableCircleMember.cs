using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableCircleMember : TableCircleMemberCRUD
    {
        private readonly IdentityDatabase _db;

        public TableCircleMember(IdentityDatabase db, CacheHelper cache) : base(cache)
        {
            _db = db;
        }

        public async Task<int> DeleteAsync(Guid circleId, Guid memberId)
        {
            using var conn = _db.CreateDisposableConnection();
            return await base.DeleteAsync(conn, _db._identityId, circleId, memberId);
        }

        public async Task<int> InsertAsync(CircleMemberRecord item)
        {
            item.identityId = _db._identityId;
            using var conn = _db.CreateDisposableConnection();
            return await base.InsertAsync(conn, item);
        }

        public async Task<int> UpsertAsync(CircleMemberRecord item)
        {
            item.identityId = _db._identityId;
            using var conn = _db.CreateDisposableConnection();
            return await base.UpsertAsync(conn, item);
        }


        public async Task<List<CircleMemberRecord>> GetCircleMembersAsync(Guid circleId)
        {
            using var conn = _db.CreateDisposableConnection();
            var r = await base.GetCircleMembersAsync(conn, _db._identityId, circleId);

            // The services code doesn't handle null, so I've made this override
            if (r == null)
                r = new List<CircleMemberRecord>();

            return r;
        }


        /// <summary>
        /// Returns all members of the given circle (the data, aka exchange grants not returned)
        /// </summary>
        /// <param name="circleId"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public async Task<List<CircleMemberRecord>> GetMemberCirclesAndDataAsync(Guid memberId)
        {
            using var conn = _db.CreateDisposableConnection();
            var r = await base.GetMemberCirclesAndDataAsync(conn, _db._identityId, memberId);

            // The services code doesn't handle null, so I've made this override
            if (r == null)
                r = new List<CircleMemberRecord>();

            return r;
        }


        /// <summary>
        /// Adds each CircleMemberRecord in the supplied list.
        /// </summary>
        /// <param name="circleMemberRecordList"></param>
        /// <exception cref="Exception"></exception>
        public async Task UpsertCircleMembersAsync(List<CircleMemberRecord> circleMemberRecordList)
        {
            if (circleMemberRecordList == null || circleMemberRecordList.Count < 1)
                throw new Exception("No members supplied (null or empty)");

            using (var conn = _db.CreateDisposableConnection())
            {
                await conn.CreateCommitUnitOfWorkAsync(async () =>
                {
                    for (int i = 0; i < circleMemberRecordList.Count; i++)
                    {
                        circleMemberRecordList[i].identityId = _db._identityId;
                        await base.UpsertAsync(conn, circleMemberRecordList[i]);
                    }
                });
            }
        }


        /// <summary>
        /// Removes the list of members from the given circle.
        /// </summary>
        /// <param name="circleId"></param>
        /// <param name="members"></param>
        /// <exception cref="Exception"></exception>
        public async Task RemoveCircleMembersAsync(Guid circleId, List<Guid> members)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                if ((members == null) || (members.Count < 1))
                    throw new Exception("No members supplied (null or empty)");

                await conn.CreateCommitUnitOfWorkAsync(async () =>
                {
                    for (int i = 0; i < members.Count; i++)
                        await base.DeleteAsync(conn, _db._identityId, circleId, members[i]);
                });
            }
        }


        /// <summary>
        /// Removes the supplied list of members from all circles.
        /// </summary>
        /// <param name="members"></param>
        /// <exception cref="Exception"></exception>
        public async Task DeleteMembersFromAllCirclesAsync(List<Guid> members)
        {
            if ((members == null) || (members.Count < 1))
                throw new Exception("No members supplied (null or empty)");

            using (var conn = _db.CreateDisposableConnection())
            {
                await conn.CreateCommitUnitOfWorkAsync(async () =>
                {
                    for (int i = 0; i < members.Count; i++)
                    {
                        var circles = await base.GetMemberCirclesAndDataAsync(conn, _db._identityId, members[i]);

                        for (int j = 0; j < circles.Count; j++)
                            await base.DeleteAsync(conn, _db._identityId, circles[j].circleId, members[i]);
                    }
                });
            }
        }
    }
}