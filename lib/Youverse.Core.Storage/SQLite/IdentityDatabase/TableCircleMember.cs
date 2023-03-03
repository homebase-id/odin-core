using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace Youverse.Core.Storage.SQLite.IdentityDatabase
{
    public class TableCircleMember : TableCircleMemberCRUD
    {
        public const int MAX_DATA_LENGTH = 65000;  // Some max value for the data

        public TableCircleMember(IdentityDatabase db) : base(db)
        {
        }

        ~TableCircleMember()
        {
        }

        public override void Dispose()
        {
            base.Dispose();
        }


        public new virtual List<CircleMemberItem> GetCircleMembers(Guid circleId)
        {
            var r = base.GetCircleMembers(circleId);

            // The services code doesn't handle null, so I've made this override
            if (r == null)
                r = new List<CircleMemberItem>();

            return r;
        }


        /// <summary>
        /// Returns all members of the given circle (the data, aka exchange grants not returned)
        /// </summary>
        /// <param name="circleId"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public new virtual List<CircleMemberItem> GetMemberCirclesAndData(Guid memberId)
        {
            var r = base.GetMemberCirclesAndData(memberId);

            // The services code doesn't handle null, so I've made this override
            if (r == null)
                r = new List<CircleMemberItem>();

            return r;
        }


        /// <summary>
        /// Adds each CircleMemberItem in the supplied list.
        /// </summary>
        /// <param name="CircleMemberItemList"></param>
        /// <exception cref="Exception"></exception>
        public void AddCircleMembers(List<CircleMemberItem> CircleMemberItemList)
        {
            if ((CircleMemberItemList == null) || (CircleMemberItemList.Count < 1))
                throw new Exception("No members supplied (null or empty)");

            using (_database.CreateCommitUnitOfWork())
                for (int i = 0; i < CircleMemberItemList.Count; i++)
                    Insert(CircleMemberItemList[i]);
        }


        /// <summary>
        /// Removes the list of members from the given circle.
        /// </summary>
        /// <param name="circleId"></param>
        /// <param name="members"></param>
        /// <exception cref="Exception"></exception>
        public void RemoveCircleMembers(Guid circleId, List<Guid> members)
        {
            if ((members == null) || (members.Count < 1))
                throw new Exception("No members supplied (null or empty)");

            using (_database.CreateCommitUnitOfWork())
                for (int i = 0; i < members.Count; i++)
                    Delete(circleId, members[i]);
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

            using (_database.CreateCommitUnitOfWork())
                for (int i = 0; i < members.Count; i++)
                    DeleteByCircleMember(members[i]);

        }
    }
}
