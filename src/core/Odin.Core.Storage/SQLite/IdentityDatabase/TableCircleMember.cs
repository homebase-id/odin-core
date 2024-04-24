using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableCircleMember : TableCircleMemberCRUD
    {
        public TableCircleMember(IdentityDatabase db, CacheHelper cache) : base(db, cache)
        {
        }

        ~TableCircleMember()
        {
        }

        public new virtual List<CircleMemberRecord> GetCircleMembers(DatabaseBase.DatabaseConnection conn, Guid circleId)
        {
            var r = base.GetCircleMembers(conn, circleId);

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
        public new virtual List<CircleMemberRecord> GetMemberCirclesAndData(DatabaseBase.DatabaseConnection conn, Guid memberId)
        {
            var r = base.GetMemberCirclesAndData(conn, memberId);

            // The services code doesn't handle null, so I've made this override
            if (r == null)
                r = new List<CircleMemberRecord>();

            return r;
        }


        /// <summary>
        /// Adds each CircleMemberRecord in the supplied list.
        /// </summary>
        /// <param name="CircleMemberRecordList"></param>
        /// <exception cref="Exception"></exception>
        public void UpsertCircleMembers(DatabaseBase.DatabaseConnection conn, List<CircleMemberRecord> CircleMemberRecordList)
        {
            if ((CircleMemberRecordList == null) || (CircleMemberRecordList.Count < 1))
                throw new Exception("No members supplied (null or empty)");

            using (conn.CreateCommitUnitOfWork())
                for (int i = 0; i < CircleMemberRecordList.Count; i++)
                    Upsert(conn, CircleMemberRecordList[i]);
        }


        /// <summary>
        /// Removes the list of members from the given circle.
        /// </summary>
        /// <param name="circleId"></param>
        /// <param name="members"></param>
        /// <exception cref="Exception"></exception>
        public void RemoveCircleMembers(DatabaseBase.DatabaseConnection conn, Guid circleId, List<Guid> members)
        {
            if ((members == null) || (members.Count < 1))
                throw new Exception("No members supplied (null or empty)");

            using (conn.CreateCommitUnitOfWork())
                for (int i = 0; i < members.Count; i++)
                    Delete(conn, circleId, members[i]);
        }


        /// <summary>
        /// Removes the supplied list of members from all circles.
        /// </summary>
        /// <param name="members"></param>
        /// <exception cref="Exception"></exception>
        public void DeleteMembersFromAllCircles(DatabaseBase.DatabaseConnection conn, List<Guid> members)
        {
            if ((members == null) || (members.Count < 1))
                throw new Exception("No members supplied (null or empty)");

            using (conn.CreateCommitUnitOfWork())
            {
                for (int i = 0; i < members.Count; i++)
                {
                    var circles = GetMemberCirclesAndData(conn, members[i]);

                    for (int j = 0; j < circles.Count; j++)
                        Delete(conn, circles[j].circleId, members[i]);
                }
            }
        }
    }
}
