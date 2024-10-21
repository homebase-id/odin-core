using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableCircleMember : TableCircleMemberCRUD
    {
        private readonly IdentityDatabase _db;

        public TableCircleMember(IdentityDatabase db, CacheHelper cache) : base(cache)
        {
            _db = db;
        }

        ~TableCircleMember()
        {
        }

        public int Delete(Guid circleId, Guid memberId, DatabaseConnection connection)
        {
            if (null == connection)
            {
                using (var conn = _db.CreateDisposableConnection())
                {
                    return base.Delete(conn, _db._identityId, circleId, memberId);
                }
            }

            return base.Delete(connection, _db._identityId, circleId, memberId);
        }

        public int Insert(CircleMemberRecord item)
        {
            item.identityId = _db._identityId;
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.Insert(conn, item);
            }
        }

        public int Upsert(CircleMemberRecord item, DatabaseConnection connection)
        {
            item.identityId = _db._identityId;
            if (null == connection)
            {
                using (var conn = _db.CreateDisposableConnection())
                {
                    return base.Upsert(conn, item);
                }
            }

            return base.Upsert(connection, item);
        }


        public List<CircleMemberRecord> GetCircleMembers(Guid circleId, DatabaseConnection connection = null)
        {
            List<CircleMemberRecord> GetData(DatabaseConnection conn)
            {
                var r = base.GetCircleMembers(conn, _db._identityId, circleId);

                // The services code doesn't handle null, so I've made this override
                if (r == null)
                    r = new List<CircleMemberRecord>();

                return r;
            }

            if (null == connection)
            {
                using (var conn = _db.CreateDisposableConnection())
                {
                    return GetData(conn);
                }
            }

            return GetData(connection);
        }


        /// <summary>
        /// Returns all members of the given circle (the data, aka exchange grants not returned)
        /// </summary>
        /// <param name="circleId"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public List<CircleMemberRecord> GetMemberCirclesAndData(Guid memberId, DatabaseConnection connection = null)
        {
            List<CircleMemberRecord> GetData(DatabaseConnection conn)
            {
                var r = base.GetMemberCirclesAndData(conn, _db._identityId, memberId);

                // The services code doesn't handle null, so I've made this override
                if (r == null)
                    r = new List<CircleMemberRecord>();

                return r;
            }

            if (null == connection)
            {
                using (var conn = _db.CreateDisposableConnection())
                {
                    return GetData(conn);
                }
            }

            return GetData(connection);
        }


        /// <summary>
        /// Adds each CircleMemberRecord in the supplied list.
        /// </summary>
        /// <param name="CircleMemberRecordList"></param>
        /// <exception cref="Exception"></exception>
        public void UpsertCircleMembers(List<CircleMemberRecord> CircleMemberRecordList)
        {
            if ((CircleMemberRecordList == null) || (CircleMemberRecordList.Count < 1))
                throw new Exception("No members supplied (null or empty)");

            using (var conn = _db.CreateDisposableConnection())
            {
                conn.CreateCommitUnitOfWork(() =>
                {
                    for (int i = 0; i < CircleMemberRecordList.Count; i++)
                    {
                        CircleMemberRecordList[i].identityId = _db._identityId;
                        base.Upsert(conn, CircleMemberRecordList[i]);
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
        public void RemoveCircleMembers(Guid circleId, List<Guid> members)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                if ((members == null) || (members.Count < 1))
                    throw new Exception("No members supplied (null or empty)");

                conn.CreateCommitUnitOfWork(() =>
                {
                    for (int i = 0; i < members.Count; i++)
                        base.Delete(conn, _db._identityId, circleId, members[i]);
                });
            }
        }


        /// <summary>
        /// Removes the supplied list of members from all circles.
        /// </summary>
        /// <param name="members"></param>
        /// <exception cref="Exception"></exception>
        public void DeleteMembersFromAllCircles(List<Guid> members)
        {
            if ((members == null) || (members.Count < 1))
                throw new Exception("No members supplied (null or empty)");

            using (var conn = _db.CreateDisposableConnection())
            {
                conn.CreateCommitUnitOfWork(() =>
                {
                    for (int i = 0; i < members.Count; i++)
                    {
                        var circles = base.GetMemberCirclesAndData(conn, _db._identityId, members[i]);

                        for (int j = 0; j < circles.Count; j++)
                            base.Delete(conn, _db._identityId, circles[j].circleId, members[i]);
                    }
                });
            }
        }
    }
}