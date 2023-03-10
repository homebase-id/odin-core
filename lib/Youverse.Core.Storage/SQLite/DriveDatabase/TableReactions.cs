using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Youverse.Core.Identity;

namespace Youverse.Core.Storage.Sqlite.DriveDatabase
{
    public class TableReactions : TableReactionsCRUD
    {
        private SqliteCommand _selectCommand = null;
        private SqliteParameter _sparam1 = null;
        private static Object _selectLock = new Object();


        private SqliteCommand _select2Command = null;
        private SqliteParameter _s2param1 = null;
        private SqliteParameter _s2param2 = null;
        private static Object _select2Lock = new Object();

        private SqliteCommand _select3Command = null;
        private SqliteParameter _s3param1 = null;
        private SqliteParameter _s3param2 = null;
        private static Object _select3Lock = new Object();

        private SqliteCommand _select4Command = null;
        private SqliteParameter _s4param1 = null;
        private static Object _select4Lock = new Object();

        private SqliteCommand _getPaging0Command = null;
        private static Object _getPaging0Lock = new Object();
        private SqliteParameter _getPaging0Param1 = null;
        private SqliteParameter _getPaging0Param2 = null;
        private SqliteParameter _getPaging0Param3 = null;


        public TableReactions(DriveDatabase db) : base(db)
        {
        }

        ~TableReactions()
        {
        }

        public override void Dispose()
        {
            _selectCommand?.Dispose();
            _selectCommand = null;

            _select2Command?.Dispose();
            _select2Command = null;

            _select3Command?.Dispose();
            _select3Command = null;

            _getPaging0Command?.Dispose();
            _getPaging0Command = null;

            base.Dispose();
        }


        public (List<string>, int) GetPostReactions(Guid postId)
        {
            lock (_selectLock)
            {
                if (_selectCommand == null)
                {
                    _selectCommand = _database.CreateCommand();
                    _selectCommand.CommandText =
                        $"SELECT singleReaction, COUNT(singleReaction) as reactioncount FROM reactions WHERE postId=$postId GROUP BY singleReaction ORDER BY reactioncount DESC;";

                    _sparam1 = _selectCommand.CreateParameter();
                    _sparam1.ParameterName = "$postId";
                    _selectCommand.Parameters.Add(_sparam1);

                    _selectCommand.Prepare();
                }

                _sparam1.Value = postId.ToByteArray();

                using (SqliteDataReader rdr = _database.ExecuteReader(_selectCommand, System.Data.CommandBehavior.Default))
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
                            result.Add(s);
                        }

                        int count = rdr.GetInt32(1);
                        totalCount += count;
                        n++;
                    }

                    return (result, totalCount);
                }
            }
        } //



        /// <summary>
        /// Get the number of reactions  made by the identity on a given post.
        /// </summary>
        /// <param name="identity"></param>
        /// <param name="postId"></param>
        /// <returns></returns>
        public int GetIdentityPostReactions(OdinId identity, Guid postId)
        {
            lock (_select2Lock)
            {
                if (_select2Command == null)
                {
                    _select2Command = _database.CreateCommand();
                    _select2Command.CommandText =
                        $"SELECT COUNT(singleReaction) as reactioncount FROM reactions WHERE identity=$identity AND postId=$postId;";

                    _s2param1 = _select2Command.CreateParameter();
                    _s2param1.ParameterName = "$postId";
                    _select2Command.Parameters.Add(_s2param1);

                    _s2param2 = _select2Command.CreateParameter();
                    _s2param2.ParameterName = "$identity";
                    _select2Command.Parameters.Add(_s2param2);

                    _select2Command.Prepare();
                }

                _s2param1.Value = postId.ToByteArray();
                _s2param2.Value = identity.DomainName;

                using (SqliteDataReader rdr = _database.ExecuteReader(_select2Command, System.Data.CommandBehavior.Default))
                {
                    if (rdr.Read())
                        return rdr.GetInt32(0);
                    else
                        return 0;
                }
            }
        }


        /// <summary>
        /// Get the number of reactions  made by the identity on a given post.
        /// </summary>
        /// <param name="identity"></param>
        /// <param name="postId"></param>
        /// <returns></returns>
        public List<string> GetIdentityPostReactionDetails(OdinId identity, Guid postId)
        {
            lock (_select3Lock)
            {
                if (_select3Command == null)
                {
                    _select3Command = _database.CreateCommand();
                    _select3Command.CommandText =
                        $"SELECT singleReaction as reactioncount FROM reactions WHERE identity=$identity AND postId=$postId;";

                    _s3param1 = _select3Command.CreateParameter();
                    _s3param1.ParameterName = "$postId";
                    _select3Command.Parameters.Add(_s3param1);

                    _s3param2 = _select3Command.CreateParameter();
                    _s3param2.ParameterName = "$identity";
                    _select3Command.Parameters.Add(_s3param2);

                    _select3Command.Prepare();
                }

                _s3param1.Value = postId.ToByteArray();
                _s3param2.Value = identity.DomainName;

                using (SqliteDataReader rdr = _database.ExecuteReader(_select3Command, System.Data.CommandBehavior.Default))
                {
                    var rs = new List<string>();

                    while (rdr.Read())
                    {
                        string s = rdr.GetString(0);
                        rs.Add(s);
                    }

                    return rs;
                }
            }
        }


        public (List<string>, List<int>, int) GetPostReactionsWithDetails(Guid postId)
        {
            lock (_select4Lock)
            {
                if (_select4Command == null)
                {
                    _select4Command = _database.CreateCommand();
                    _select4Command.CommandText =
                        $"SELECT singleReaction, COUNT(singleReaction) as reactioncount FROM reactions WHERE postId=$postId GROUP BY singleReaction ORDER BY reactioncount DESC;";

                    _s4param1 = _select4Command.CreateParameter();
                    _s4param1.ParameterName = "$postId";
                    _select4Command.Parameters.Add(_s4param1);

                    _select4Command.Prepare();
                }

                _s4param1.Value = postId;

                using (SqliteDataReader rdr = _database.ExecuteReader(_select4Command, System.Data.CommandBehavior.Default))
                {
                    var result = new List<string>();
                    var iresult = new List<int>();
                    int totalCount = 0;

                    while (rdr.Read())
                    {
                        string s = rdr.GetString(0);
                        result.Add(s);

                        int count = rdr.GetInt32(1);
                        iresult.Add(count);

                        totalCount += count;
                    }

                    return (result, iresult, totalCount);
                }
            }
        } //


        // Copied and modified from CRUD
        public List<ReactionsRecord> PagingByRowid(int count, Int32? inCursor, out Int32? nextCursor, Guid postIdFilter)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");
            if (inCursor == null)
                inCursor = 0;

            lock (_getPaging0Lock)
            {
                if (_getPaging0Command == null)
                {
                    _getPaging0Command = _database.CreateCommand();
                    _getPaging0Command.CommandText = "SELECT rowid,identity,postId,singleReaction FROM reactions " +
                                                 "WHERE postId == $postId AND rowid > $rowid ORDER BY rowid ASC LIMIT $_count;";
                    _getPaging0Param1 = _getPaging0Command.CreateParameter();
                    _getPaging0Command.Parameters.Add(_getPaging0Param1);
                    _getPaging0Param1.ParameterName = "$rowid";

                    _getPaging0Param2 = _getPaging0Command.CreateParameter();
                    _getPaging0Command.Parameters.Add(_getPaging0Param2);
                    _getPaging0Param2.ParameterName = "$_count";

                    _getPaging0Param3 = _getPaging0Command.CreateParameter();
                    _getPaging0Command.Parameters.Add(_getPaging0Param3);
                    _getPaging0Param3.ParameterName = "$postId";

                    _getPaging0Command.Prepare();
                }
                _getPaging0Param1.Value = inCursor;
                _getPaging0Param2.Value = count + 1;
                _getPaging0Param3.Value = postIdFilter.ToByteArray();

                using (SqliteDataReader rdr = _database.ExecuteReader(_getPaging0Command, System.Data.CommandBehavior.Default))
                {
                    var result = new List<ReactionsRecord>();
                    int n = 0;
                    int rowid = 0;
                    while ((n < count) && rdr.Read())
                    {
                        n++;
                        var item = new ReactionsRecord();
                        byte[] _tmpbuf = new byte[65535 + 1];
                        long bytesRead;
                        var _guid = new byte[16];

                        rowid = rdr.GetInt32(0);

                        if (rdr.IsDBNull(1))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            var s = rdr.GetString(1);
                            item.identity = new OdinId(s);
                        }

                        if (rdr.IsDBNull(2))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            bytesRead = rdr.GetBytes(2, 0, _guid, 0, 16);
                            if (bytesRead != 16)
                                throw new Exception("Not a GUID in postId...");
                            item.postId = new Guid(_guid);
                        }

                        if (rdr.IsDBNull(3))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            item.singleReaction = rdr.GetString(3);
                        }

                        result.Add(item);
                    } // while
                    if ((n > 0) && rdr.Read())
                    {
                        nextCursor = rowid;
                    }
                    else
                    {
                        nextCursor = null;
                    }

                    return result;
                } // using
            } // lock
        } // PagingGet
    }
}
