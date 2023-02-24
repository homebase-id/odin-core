using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SQLite;

namespace Youverse.Core.Storage.SQLite.DriveDatabase
{
    public class TableReactions : TableReactionsCRUD
    {
        const int ID_EQUAL = 16; // Precisely 16 bytes for the ID key

        private SQLiteCommand _deleteCommand = null;
        private SQLiteParameter _delparam1 = null;
        private SQLiteParameter _delparam2 = null;
        private static Object _deleteLock = new Object();

        private SQLiteCommand _selectCommand = null;
        private SQLiteParameter _sparam1 = null;
        private static Object _selectLock = new Object();


        private SQLiteCommand _select2Command = null;
        private SQLiteParameter _s2param1 = null;
        private SQLiteParameter _s2param2 = null;
        private static Object _select2Lock = new Object();

        private SQLiteCommand _getPaging0Command = null;
        private static Object _getPaging0Lock = new Object();
        private SQLiteParameter _getPaging0Param1 = null;
        private SQLiteParameter _getPaging0Param2 = null;
        private SQLiteParameter _getPaging0Param3 = null;


        public TableReactions(DriveDatabase db) : base(db)
        {
        }

        ~TableReactions()
        {
        }

        public override void Dispose()
        {
            _deleteCommand?.Dispose();
            _deleteCommand = null;

            _selectCommand?.Dispose();
            _selectCommand = null;

            base.Dispose();
        }

        /// <summary>
        /// Removes all reactions from the supplied identity
        /// </summary>
        /// <param name="identity"></param>
        /// <param name="postId"></param>
        /// <exception cref="Exception"></exception>
        public void DeleteAllReactions(string identity, Guid postid)
        {
            // Assign to throw exceptions, it's kind of bizarre :-)
            var item = new ReactionsItem()
            {
                identity = identity,
                postid = postid
            };

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

        public int GetIdentityPostReactions(string identity, Guid postId)
        {
            // Assign to throw exceptions, it's kind of bizarre :-)
            var item = new ReactionsItem()
            {
                identity = identity,
                postid = postId
            };

            lock (_select2Lock)
            {
                if (_select2Command == null)
                {
                    _select2Command = _database.CreateCommand();
                    _select2Command.CommandText =
                        $"SELECT COUNT(singlereaction) as reactioncount FROM reactions WHERE identity=$identity AND postid=$postid;";

                    _s2param1 = _select2Command.CreateParameter();
                    _s2param1.ParameterName = "$postid";
                    _select2Command.Parameters.Add(_s2param1);

                    _s2param2 = _select2Command.CreateParameter();
                    _s2param2.ParameterName = "$identity";
                    _select2Command.Parameters.Add(_s2param2);

                    _select2Command.Prepare();
                }

                _s2param1.Value = postId;
                _s2param2.Value = identity;

                using (SQLiteDataReader rdr = _select2Command.ExecuteReader(System.Data.CommandBehavior.Default))
                {
                    if (rdr.Read())
                        return rdr.GetInt32(0);
                    else
                        return 0;
                }
            }
        }

        public (List<string>, int) GetPostReactions(Guid postId)
        {
            // Assign to throw exceptions, it's kind of bizarre :-)
            var item = new ReactionsItem()
            {
                postid = postId
            };

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


        // Copied and modified from CRUD
        public List<ReactionsItem> PagingByRowid(int count, Int32? inCursor, out Int32? nextCursor, Guid PostIdFilter)
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
                    _getPaging0Command.CommandText = "SELECT rowid,identity,postid,singlereaction,created,modified FROM reactions " +
                                                 "WHERE postid == $postid AND rowid > $rowid ORDER BY rowid ASC LIMIT $_count;";
                    _getPaging0Param1 = _getPaging0Command.CreateParameter();
                    _getPaging0Command.Parameters.Add(_getPaging0Param1);
                    _getPaging0Param1.ParameterName = "$rowid";

                    _getPaging0Param2 = _getPaging0Command.CreateParameter();
                    _getPaging0Command.Parameters.Add(_getPaging0Param2);
                    _getPaging0Param2.ParameterName = "$_count";

                    _getPaging0Param3 = _getPaging0Command.CreateParameter();
                    _getPaging0Command.Parameters.Add(_getPaging0Param3);
                    _getPaging0Param3.ParameterName = "$postid";

                    _getPaging0Command.Prepare();
                }
                _getPaging0Param1.Value = inCursor;
                _getPaging0Param2.Value = count + 1;
                _getPaging0Param3.Value = PostIdFilter;

                using (SQLiteDataReader rdr = _getPaging0Command.ExecuteReader(System.Data.CommandBehavior.Default))
                {
                    var result = new List<ReactionsItem>();
                    int n = 0;
                    int rowid = 0;
                    while (rdr.Read() && (n < count))
                    {
                        n++;
                        var item = new ReactionsItem();
                        byte[] _tmpbuf = new byte[65535 + 1];
                        long bytesRead;
                        var _guid = new byte[16];

                        rowid = rdr.GetInt32(0);

                        if (rdr.IsDBNull(1))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            item.identity = rdr.GetString(1);
                        }

                        if (rdr.IsDBNull(2))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            bytesRead = rdr.GetBytes(2, 0, _guid, 0, 16);
                            if (bytesRead != 16)
                                throw new Exception("Not a GUID in postid...");
                            item.postid = new Guid(_guid);
                        }

                        if (rdr.IsDBNull(3))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            item.singlereaction = rdr.GetString(3);
                        }

                        if (rdr.IsDBNull(4))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            item.created = new UnixTimeUtcUnique((UInt64)rdr.GetInt64(4));
                        }

                        if (rdr.IsDBNull(5))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            item.modified = new UnixTimeUtc((UInt64)rdr.GetInt64(5));
                        }
                        result.Add(item);
                    } // while
                    if ((n > 0) && rdr.HasRows)
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
