﻿using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace Youverse.Core.Storage.Sqlite.IdentityDatabase
{
    public class TableCircleMember : TableCircleMemberCRUD
    {
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


        public new virtual List<CircleMemberRecord> GetCircleMembers(Guid circleId)
        {
            var r = base.GetCircleMembers(circleId);

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
        public new virtual List<CircleMemberRecord> GetMemberCirclesAndData(Guid memberId)
        {
            var r = base.GetMemberCirclesAndData(memberId);

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
        public void AddCircleMembers(List<CircleMemberRecord> CircleMemberRecordList)
        {
            if ((CircleMemberRecordList == null) || (CircleMemberRecordList.Count < 1))
                throw new Exception("No members supplied (null or empty)");

            using (_database.CreateCommitUnitOfWork())
                for (int i = 0; i < CircleMemberRecordList.Count; i++)
                    Insert(CircleMemberRecordList[i]);
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
            {
                for (int i = 0; i < members.Count; i++)
                {
                    DeleteByCircleMember(members[i]);
                }
            }
        }
    }
}
