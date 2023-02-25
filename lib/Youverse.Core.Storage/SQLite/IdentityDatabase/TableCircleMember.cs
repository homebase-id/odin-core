using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace Youverse.Core.Storage.SQLite.IdentityDatabase
{
    public class TableCircleMember : TableCirclememberCRUD
    {
        public const int MAX_DATA_LENGTH = 65000;  // Some max value for the data

        private SQLiteCommand _insertCommand = null;
        private SQLiteParameter _iparam1 = null;
        private SQLiteParameter _iparam2 = null;
        private SQLiteParameter _iparam3 = null;
        private static Object _insertLock = new Object();

        private SQLiteCommand _removeCommand = null;
        private SQLiteParameter _remparam1 = null;
        private SQLiteParameter _remparam2 = null;
        private static Object _removeLock = new Object();

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
            _insertCommand?.Dispose();
            _insertCommand = null;

            _removeCommand?.Dispose();
            _removeCommand = null;

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
        public List<CirclememberItem> GetCircleMembers(Guid circleId)
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
                    var result = new List<CirclememberItem>();

                    byte[] _tmpbuf = new byte[MAX_DATA_LENGTH];
                    byte[] g = new byte[16];

                    while (rdr.Read())
                    {
                        var item = new CirclememberItem();

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
        public List<CirclememberItem> GetMemberCirclesAndData(Guid memberId)
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
                    var result = new List<CirclememberItem>();

                    byte[] _tmpbuf = new byte[MAX_DATA_LENGTH];
                    byte[] g = new byte[16];

                    while (rdr.Read())
                    {
                        var item = new CirclememberItem();
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
        /// Adds each CirclememberItem in the supplied list.
        /// </summary>
        /// <param name="CirclememberItemList"></param>
        /// <exception cref="Exception"></exception>
        public void AddCircleMembers(List<CirclememberItem> CirclememberItemList)
        {
            if ((CirclememberItemList == null) || (CirclememberItemList.Count < 1))
                throw new Exception("No members supplied (null or empty)");

            lock (_insertLock)
            {
                // Make sure we only prep once 
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand();
                    _insertCommand.CommandText = @"INSERT INTO circlemember (circleid, memberid, data) "+
                                                  "VALUES ($circleid, $memberid, $data)";

                    _iparam1 = _insertCommand.CreateParameter();
                    _iparam2 = _insertCommand.CreateParameter();
                    _iparam3 = _insertCommand.CreateParameter();

                    _iparam1.ParameterName = "$circleid";
                    _iparam2.ParameterName = "$memberid";
                    _iparam3.ParameterName = "$data";

                    _insertCommand.Parameters.Add(_iparam1);
                    _insertCommand.Parameters.Add(_iparam2);
                    _insertCommand.Parameters.Add(_iparam3);

                    _insertCommand.Prepare();
                }

                _database.BeginTransaction();

                using (_database.CreateCommitUnitOfWork())
                {
                    // Possibly do a Commit() here. But I need to think about Commits, Semaphores and multiple threads.
                    for (int i = 0; i < CirclememberItemList.Count; i++)
                    {
                        _iparam1.Value = CirclememberItemList[i].circleId;
                        _iparam2.Value = CirclememberItemList[i].memberId;
                        _iparam3.Value = CirclememberItemList[i].data;

                        _insertCommand.ExecuteNonQuery();
                    }
                }
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
            if ((members == null) || (members.Count < 1))
                throw new Exception("No members supplied (null or empty)");

            lock (_removeLock)
            {
                // Make sure we only prep once 
                if (_removeCommand == null)
                {
                    _removeCommand = _database.CreateCommand();
                    _removeCommand.CommandText = "DELETE FROM circlemember WHERE circleid=$circleid AND memberid=$memberid;";

                    _remparam1 = _removeCommand.CreateParameter();
                    _remparam1.ParameterName = "$circleid";
                    _removeCommand.Parameters.Add(_remparam1);

                    _remparam2 = _removeCommand.CreateParameter();
                    _remparam2.ParameterName = "$memberid";
                    _removeCommand.Parameters.Add(_remparam2);

                    _removeCommand.Prepare();
                }

                _remparam1.Value = circleId;

                _database.BeginTransaction();

                using (_database.CreateCommitUnitOfWork())
                {
                    for (int i = 0; i < members.Count; i++)
                    {
                        _remparam2.Value = members[i].ToByteArray();
                        _removeCommand.ExecuteNonQuery();
                    }
                }
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
