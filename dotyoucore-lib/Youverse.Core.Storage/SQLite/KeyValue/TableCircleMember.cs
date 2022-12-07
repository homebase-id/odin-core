using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace Youverse.Core.Storage.SQLite.KeyValue
{
    public class CircleMemberItem
    {
        public byte[] circleId;
        public byte[] member;
    }

    public class TableCircleMember : TableKeyValueBase  // Make it IDisposable??
    {
        const int ID_EQUAL = 16; // Precisely 16 bytes for the ID key
        public const int MAX_MEMBER_LENGTH = 257;  // Maximum 512 bytes for the member value (domain 256)

        private SQLiteCommand _insertCommand = null;
        private SQLiteParameter _iparam1 = null;
        private SQLiteParameter _iparam2 = null;
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

        public TableCircleMember(KeyValueDatabase db) : base(db)
        {
        }

        ~TableCircleMember()
        {
            if (_insertCommand != null)
            {
                _insertCommand.Dispose();
                _insertCommand = null;
            }

            if (_removeCommand != null)
            {
                _removeCommand.Dispose();
                _removeCommand = null;
            }

            if (_deleteCommand != null)
            {
                _deleteCommand.Dispose();
                _deleteCommand = null;
            }

            if (_selectCommand != null)
            {
                _selectCommand.Dispose();
                _selectCommand = null;
            }
        }

        public override void EnsureTableExists(bool dropExisting = false)
        {
            using (var cmd = _keyValueDatabase.CreateCommand())
            {
                if (dropExisting)
                {
                    cmd.CommandText = "DROP TABLE IF EXISTS circlemember;";
                    cmd.ExecuteNonQuery();
                }

                cmd.CommandText =
                    @"CREATE TABLE IF NOT EXISTS circlemember(
                     circleid BLOB NOT NULL, 
                     member BLOB NOT NULL,
                     UNIQUE(circleid, member)); "
                    + "CREATE INDEX if not exists circlenameidx ON circlemember(member, circleid);";

                cmd.ExecuteNonQuery();
            }
        }


        /// <summary>
        /// Returns all members of the given circle
        /// </summary>
        /// <param name="circleId"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public List<byte[]> GetMembers(byte[] circleId)
        {
            if ((circleId == null) || (circleId.Length != ID_EQUAL))
                throw new Exception("circleID must be 16 bytes.");

            lock (_selectLock)
            {
                if (_selectCommand == null)
                {
                    _selectCommand = _keyValueDatabase.CreateCommand();
                    _selectCommand.CommandText =
                        $"SELECT member FROM circlemember WHERE circleid=$circleid";

                    _sparam1 = _selectCommand.CreateParameter();
                    _sparam1.ParameterName = "$circleid";
                    _selectCommand.Parameters.Add(_sparam1);

                    _selectCommand.Prepare();
                }

                _sparam1.Value = circleId;

                using (SQLiteDataReader rdr = _selectCommand.ExecuteReader(System.Data.CommandBehavior.Default))
                {
                    var result = new List<byte[]>();

                    while (rdr.Read())
                    {
                        byte[] _tmpbuf = new byte[MAX_MEMBER_LENGTH];
                        long n = rdr.GetBytes(0, 0, _tmpbuf, 0, MAX_MEMBER_LENGTH);
                        if (n >= MAX_MEMBER_LENGTH)
                            throw new Exception("Too much data...");

                        var ba = new byte[n];
                        Buffer.BlockCopy(_tmpbuf, 0, ba, 0, (int)n);
                        result.Add(ba);
                    }

                    return result;
                }
            }
        }



        public void AddMembers(byte[] circleId, List<byte[]> members)
        {
            if ((circleId == null) || (circleId.Length != ID_EQUAL))
                throw new Exception("circleID must be 16 bytes.");

            if ((members == null) || (members.Count < 1))
                throw new Exception("No members supplied (null or empty)");

            lock (_insertLock)
            {
                // Make sure we only prep once 
                if (_insertCommand == null)
                {
                    _insertCommand = _keyValueDatabase.CreateCommand();
                    _insertCommand.CommandText = @"INSERT INTO circlemember (circleid, member) "+
                                                  "VALUES ($circleid, $member)";

                    _iparam1 = _insertCommand.CreateParameter();
                    _iparam1.ParameterName = "$circleid";
                    _insertCommand.Parameters.Add(_iparam1);

                    _iparam2 = _insertCommand.CreateParameter();
                    _iparam2.ParameterName = "$member";
                    _insertCommand.Parameters.Add(_iparam2);

                    _insertCommand.Prepare();
                }

                _iparam1.Value = circleId;

                // Possibly do a Commit() here. But I need to think about Commits, Semaphores and multiple threads.
                for (int i=0; i < members.Count; i++)
                {
                    if ((members[i] == null) || (members[i].Length > MAX_MEMBER_LENGTH))
                        throw new Exception("circleID must be 16 bytes.");

                    _iparam2.Value = members[i];
                    _insertCommand.ExecuteNonQuery();
                }
            }
        }


        public void RemoveMembers(byte[] circleId, List<byte[]> members)
        {
            if ((circleId == null) || (circleId.Length != ID_EQUAL))
                throw new Exception("circleID must be 16 bytes.");

            if ((members == null) || (members.Count < 1))
                throw new Exception("No members supplied (null or empty)");

            lock (_removeLock)
            {
                // Make sure we only prep once 
                if (_removeCommand == null)
                {
                    _removeCommand = _keyValueDatabase.CreateCommand();
                    _removeCommand.CommandText = "DELETE FROM circlemember WHERE circleid=$circleid AND member=$member;";

                    _remparam1 = _removeCommand.CreateParameter();
                    _remparam1.ParameterName = "$circleid";
                    _removeCommand.Parameters.Add(_remparam1);

                    _remparam2 = _removeCommand.CreateParameter();
                    _remparam2.ParameterName = "$member";
                    _removeCommand.Parameters.Add(_remparam2);

                    _removeCommand.Prepare();
                }

                _remparam1.Value = circleId;

                for (int i = 0; i < members.Count; i++)
                {
                    _remparam2.Value = members[i];
                    _removeCommand.ExecuteNonQuery();
                }
            }
        }



        public void DeleteMembers(List<byte[]> members)
        {
            if ((members == null) || (members.Count < 1))
                throw new Exception("No members supplied (null or empty)");

            lock (_deleteLock)
            {
                // Make sure we only prep once 
                if (_deleteCommand == null)
                {
                    _deleteCommand = _keyValueDatabase.CreateCommand();
                    _deleteCommand.CommandText = "DELETE FROM circlemember WHERE member=$member;";

                    _delparam1 = _deleteCommand.CreateParameter();
                    _delparam1.ParameterName = "$member";
                    _deleteCommand.Parameters.Add(_delparam1);

                    _deleteCommand.Prepare();
                }

                for (int i = 0; i < members.Count; i++)
                {
                    _delparam1.Value = members[i];
                    _deleteCommand.ExecuteNonQuery();
                }
            }
        }
    }
}
