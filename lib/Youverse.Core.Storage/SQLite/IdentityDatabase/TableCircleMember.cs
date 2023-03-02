﻿using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace Youverse.Core.Storage.SQLite.IdentityDatabase
{
    public class TableCircleMember : TableCircleMemberCRUD
    {
        public const int MAX_DATA_LENGTH = 65000;  // Some max value for the data

        private SQLiteCommand _deleteCommand = null;
        private SQLiteParameter _delparam1 = null;
        private static Object _deleteLock = new Object();

        public TableCircleMember(IdentityDatabase db) : base(db)
        {
        }

        ~TableCircleMember()
        {
        }

        public override void Dispose()
        {
            _deleteCommand?.Dispose();
            _deleteCommand = null;

            base.Dispose();
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

            lock (_deleteLock)
            {
                // Make sure we only prep once 
                if (_deleteCommand == null)
                {
                    _deleteCommand = _database.CreateCommand();
                    _deleteCommand.CommandText = "DELETE FROM circlemember WHERE memberid=$memberid;";

                    _delparam1 = _deleteCommand.CreateParameter();
                    _delparam1.ParameterName = "$memberid";
                    _deleteCommand.Parameters.Add(_delparam1);

                    _deleteCommand.Prepare();
                }

                _database.BeginTransaction();

                using (_database.CreateCommitUnitOfWork())
                {
                    for (int i = 0; i < members.Count; i++)
                    {
                        _delparam1.Value = members[i].ToByteArray();
                        _deleteCommand.ExecuteNonQuery();
                    }
                }
            }
        }
    }
}
