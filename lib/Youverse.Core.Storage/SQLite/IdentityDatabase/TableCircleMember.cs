using System;
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

        private SQLiteCommand _selectCommand = null;
        private SQLiteParameter _sparam1 = null;
        private static Object _selectLock = new Object();

        private SQLiteCommand _select2Command = null;
        private SQLiteParameter _s2param1 = null;
        private static Object _select2Lock = new Object();

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

            _selectCommand?.Dispose();
            _selectCommand = null;

            _select2Command?.Dispose();
            _select2Command = null;

            base.Dispose();
        }

        /// <summary>
        /// Returns all members of the given circle (the data, aka exchange grants not returned)
        /// </summary>
        /// <param name="circleId"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public List<CircleMemberItem> GetCircleMembers(Guid circleId)
        {
            lock (_selectLock)
            {
                if (_selectCommand == null)
                {
                    _selectCommand = _database.CreateCommand();
                    _selectCommand.CommandText =
                        $"SELECT memberid, data FROM circlemember WHERE circleid=$circleid";

                    _sparam1 = _selectCommand.CreateParameter();
                    _sparam1.ParameterName = "$circleid";
                    _selectCommand.Parameters.Add(_sparam1);

                    _selectCommand.Prepare();
                }

                _sparam1.Value = circleId;

                using (SQLiteDataReader rdr = _selectCommand.ExecuteReader(System.Data.CommandBehavior.Default))
                {
                    var result = new List<CircleMemberItem>();

                    byte[] _tmpbuf = new byte[MAX_DATA_LENGTH];
                    byte[] g = new byte[16];

                    while (rdr.Read())
                    {
                        var item = new CircleMemberItem();

                        item.circleId = circleId;
                        long n = rdr.GetBytes(0, 0, g, 0, 16);
                        if (n != 16)
                            throw new Exception("Not a GUID");
                        item.memberId = new Guid(g);

                        n = rdr.GetBytes(1, 0, _tmpbuf, 0, MAX_DATA_LENGTH);
                        if (n > 0)
                        {
                            item.data = new byte[n];
                            Buffer.BlockCopy(_tmpbuf, 0, item.data, 0, (int)n);
                        }
                        else
                        {
                            item.data = null;
                        }

                        result.Add(item);
                    }

                    return result;
                }
            }
        }


        /// <summary>
        /// Returns all members of the given circle (the data, aka exchange grants not returned)
        /// </summary>
        /// <param name="circleId"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public List<CircleMemberItem> GetMemberCirclesAndData(Guid memberId)
        {
            lock (_select2Lock)
            {
                if (_select2Command == null)
                {
                    _select2Command = _database.CreateCommand();
                    _select2Command.CommandText =
                        $"SELECT circleid, data FROM circlemember WHERE memberid=$memberid";

                    _s2param1 = _select2Command.CreateParameter();
                    _s2param1.ParameterName = "$memberid";
                    _select2Command.Parameters.Add(_s2param1);

                    _select2Command.Prepare();
                }

                _s2param1.Value = memberId;

                using (SQLiteDataReader rdr = _select2Command.ExecuteReader(System.Data.CommandBehavior.Default))
                {
                    var result = new List<CircleMemberItem>();

                    byte[] _tmpbuf = new byte[MAX_DATA_LENGTH];
                    byte[] g = new byte[16];

                    while (rdr.Read())
                    {
                        var item = new CircleMemberItem();
                        item.memberId = memberId;

                        long n = rdr.GetBytes(0, 0, g, 0, 16);
                        if (n != 16)
                            throw new Exception("Not a GUID");
                        item.circleId = new Guid(g);

                        n = rdr.GetBytes(1, 0, _tmpbuf, 0, MAX_DATA_LENGTH);
                        if (n > 0)
                        {
                            item.data = new byte[n];
                            Buffer.BlockCopy(_tmpbuf, 0, item.data, 0, (int)n);
                        }
                        else
                        {
                            item.data = null;
                        }

                        result.Add(item);
                    }

                    return result;
                }
            }
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

            _database.BeginTransaction();

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

            _database.BeginTransaction();

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
