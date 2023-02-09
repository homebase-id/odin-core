using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace Youverse.Core.Storage.SQLite.DriveDatabase
{
    public class ReactionItem
    {
        public string identity;
        public byte[] postId;
        public string singleReaction;
    }

    public class TableReactions : TableBase
    {
        const int ID_EQUAL = 16; // Precisely 16 bytes for the ID key
        public const int MAX_MEMBER_LENGTH = 257;  // Maximum 512 bytes for the member value (domain 256)

        private SQLiteCommand _insertCommand = null;
        private SQLiteParameter _iparam1 = null;
        private SQLiteParameter _iparam2 = null;
        private SQLiteParameter _iparam3 = null;
        private static Object _insertLock = new Object();

        private SQLiteCommand _deleteCommand = null;
        private SQLiteParameter _delparam1 = null;
        private SQLiteParameter _delparam2 = null;
        private static Object _deleteLock = new Object();

        private SQLiteCommand _singleDeleteCommand = null;
        private SQLiteParameter _singleDelparam1 = null;
        private SQLiteParameter _singleDelparam2 = null;
        private SQLiteParameter _singleDelparam3 = null;
        private static Object _singleDeleteLock = new Object();

        private SQLiteCommand _selectCommand = null;
        private SQLiteParameter _sparam1 = null;
        private static Object _selectLock = new Object();

        public TableReactions(DriveDatabase db) : base(db)
        {
        }

        ~TableReactions()
        {
        }

        public override void Dispose()
        {
            _insertCommand?.Dispose();
            _insertCommand = null;

            _deleteCommand?.Dispose();
            _deleteCommand = null;

            _singleDeleteCommand?.Dispose();
            _singleDeleteCommand= null;

            _selectCommand?.Dispose();
            _selectCommand = null;
        }

        public override void EnsureTableExists(bool dropExisting = false)
        {
            using (var cmd = _database.CreateCommand())
            {
                if (dropExisting)
                {
                    cmd.CommandText = "DROP TABLE IF EXISTS reactions;";
                    cmd.ExecuteNonQuery();
                }

                cmd.CommandText =
                    @"CREATE TABLE IF NOT EXISTS reactions(
                     identity STRING NOT NULL, 
                     postid BLOB NOT NULL,
                     singlereaction STRING NOT NULL,
                     UNIQUE(identity, postid, singlereaction)); "
                    + "CREATE INDEX if not exists reactionidx ON reactions(identity, postid);";

                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// I'd really like to change identity string to the identity class which is validated
        /// </summary>
        /// <param name="identity"></param>
        /// <param name="postId"></param>
        /// <param name="singleReaction">Be sure your string is trimmed, lowercase, etc.</param>
        /// <exception cref="Exception">If duplicate, will throw error</exception>
        public void InsertReaction(string identity, Guid postId, string singleReaction)
        {
            if ((identity == null) || (identity.Length >= MAX_MEMBER_LENGTH))
                throw new Exception("invalid identity.");

            if ((singleReaction == null) || (singleReaction.Length < 3)) // :x:
                throw new Exception("singlereaction is not a reaction");

            lock (_insertLock)
            {
                // Make sure we only prep once 
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand();
                    _insertCommand.CommandText = @"INSERT INTO reactions (identity, postid, singlereaction) " +
                                                  "VALUES ($identity, $postid, $singlereaction)";

                    _iparam1 = _insertCommand.CreateParameter();
                    _iparam2 = _insertCommand.CreateParameter();
                    _iparam3 = _insertCommand.CreateParameter();

                    _insertCommand.Parameters.Add(_iparam1);
                    _insertCommand.Parameters.Add(_iparam2);
                    _insertCommand.Parameters.Add(_iparam3);

                    _iparam1.ParameterName = "$identity";
                    _iparam2.ParameterName = "$postid";
                    _iparam3.ParameterName = "$singlereaction";

                    _insertCommand.Prepare();
                }

                _iparam1.Value = identity;
                _iparam2.Value = postId.ToByteArray();
                _iparam3.Value = singleReaction;

                _database.BeginTransaction();
                _insertCommand.ExecuteNonQuery();
            }
        }


        /// <summary>
        /// Removes all reactions from the supplied identity
        /// </summary>
        /// <param name="identity"></param>
        /// <param name="postId"></param>
        /// <exception cref="Exception"></exception>
        public void DeleteAllReactions(string identity, Guid postid)
        {
            if ((identity == null) || (identity.Length >= MAX_MEMBER_LENGTH))
                throw new Exception("invalid identity.");

            lock (_deleteLock)
            {
                // Make sure we only prep once 
                if (_deleteCommand == null)
                {
                    _deleteCommand = _database.CreateCommand();
                    _deleteCommand.CommandText = @"DELETE FROM reactions WHERE identity=$identity AND postid=$postid;";

                    _delparam1 = _deleteCommand.CreateParameter();
                    _delparam2 = _deleteCommand.CreateParameter();
                    _deleteCommand.Parameters.Add(_delparam1);
                    _deleteCommand.Parameters.Add(_delparam2);
                    _delparam1.ParameterName = "$identity";
                    _delparam2.ParameterName = "$postid";

                    _deleteCommand.Prepare();
                }

                _delparam1.Value = identity;
                _delparam2.Value = postid.ToByteArray();

                _database.BeginTransaction();
                _deleteCommand.ExecuteNonQuery();
            }
        }


        public void DeleteReaction(string identity, Guid postid, string singleReaction)
        {
            if ((identity == null) || (identity.Length >= MAX_MEMBER_LENGTH))
                throw new Exception("invalid identity.");

            if ((singleReaction == null) || (singleReaction.Length < 3)) // :x:
                throw new Exception("singlereaction is not a reaction");

            lock (_singleDeleteLock)
            {
                // Make sure we only prep once 
                if (_singleDeleteCommand == null)
                {
                    _singleDeleteCommand = _database.CreateCommand();
                    _singleDeleteCommand.CommandText = @"DELETE FROM reactions WHERE identity=$identity AND postid=$postid AND singlereaction=$singlereaction;";

                    _singleDelparam1 = _singleDeleteCommand.CreateParameter();
                    _singleDelparam2 = _singleDeleteCommand.CreateParameter();
                    _singleDelparam3 = _singleDeleteCommand.CreateParameter();

                    _singleDeleteCommand.Parameters.Add(_singleDelparam1);
                    _singleDeleteCommand.Parameters.Add(_singleDelparam2);
                    _singleDeleteCommand.Parameters.Add(_singleDelparam3);

                    _singleDelparam1.ParameterName = "$identity";
                    _singleDelparam2.ParameterName = "$postid";
                    _singleDelparam3.ParameterName = "$singlereaction";

                    _singleDeleteCommand.Prepare();
                }

                _singleDelparam1.Value = identity;
                _singleDelparam2.Value = postid.ToByteArray();
                _singleDelparam3.Value = singleReaction;

                _database.BeginTransaction();
                _singleDeleteCommand.ExecuteNonQuery();
            }
        }


        public (List<string>, int) GetPostReactions(Guid postId)
        {
            lock (_selectLock)
            {
                if (_selectCommand == null)
                {
                    _selectCommand = _database.CreateCommand();
                    _selectCommand.CommandText =
                        $"SELECT singlereaction, COUNT(singlereaction) as reactioncount FROM reactions WHERE postid=$postid GROUP BY singlereaction ORDER BY reactioncount DESC;";

                    _sparam1 = _selectCommand.CreateParameter();
                    _sparam1.ParameterName = "$postid";
                    _selectCommand.Parameters.Add(_sparam1);

                    _selectCommand.Prepare();
                }

                _sparam1.Value = postId;

                using (SQLiteDataReader rdr = _selectCommand.ExecuteReader(System.Data.CommandBehavior.Default))
                {
                    var result = new List<string>();
                    int totalCount = 0;
                    int n = 0;

                    while (rdr.Read())
                    {
                        // Only return the first five reactions (?)
                        if (n < 5)
                        {
                            string s = rdr.GetString(0);
                            if (s.Length >= MAX_MEMBER_LENGTH)
                                throw new Exception("Too much data...");
                            result.Add(s);
                        }

                        int count = rdr.GetInt32(1);
                        totalCount += count;
                        n++;
                    }

                    return (result, totalCount);
                }
            }
        }
    }
}
