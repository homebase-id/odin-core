using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Identity;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableDriveReactions : TableDriveReactionsCRUD
    {
        public TableDriveReactions(IdentityDatabase db, CacheHelper cache) : base(db, cache)
        {
        }

        ~TableDriveReactions()
        {
        }

        public override void Dispose()
        {
            base.Dispose();
            GC.SuppressFinalize(this);
        }


        public int Delete(IdentityDatabase db, Guid driveId, OdinId identity, Guid postId, string singleReaction)
        {
            using (var conn = db.CreateDisposableConnection())
            {
                return base.Delete(conn, db._identityId, driveId, identity, postId, singleReaction);
            }
        }

        public int DeleteAllReactions(IdentityDatabase db, Guid driveId, OdinId identity, Guid postId)
        {
            using (var conn = db.CreateDisposableConnection())
            {
                return base.DeleteAllReactions(conn, db._identityId, driveId, identity, postId);
            }
        }

        public new int Insert(IdentityDatabase db, DriveReactionsRecord item)
        {
            item.identityId = db._identityId;

            using (var conn = db.CreateDisposableConnection())
            {
                return base.Insert(conn, item);
            }
        }

        public (List<string>, int) GetPostReactions(IdentityDatabase db, Guid driveId, Guid postId)
        {
            using (var _selectCommand = _database.CreateCommand())
            {
                _selectCommand.CommandText =
                    $"SELECT singleReaction, COUNT(singleReaction) as reactioncount FROM driveReactions WHERE identityId=$identityId AND driveId=$driveId AND postId=$postId GROUP BY singleReaction ORDER BY reactioncount DESC;";

                var _sparam1 = _selectCommand.CreateParameter();
                var _sparam2 = _selectCommand.CreateParameter();
                var _sparam3 = _selectCommand.CreateParameter();

                _sparam1.ParameterName = "$postId";
                _sparam2.ParameterName = "$driveId";
                _sparam3.ParameterName = "$identityId";

                _selectCommand.Parameters.Add(_sparam1);
                _selectCommand.Parameters.Add(_sparam2);
                _selectCommand.Parameters.Add(_sparam3);

                _sparam1.Value = postId.ToByteArray();
                _sparam2.Value = driveId.ToByteArray();
                _sparam3.Value = db._identityId.ToByteArray();

                using (var conn = db.CreateDisposableConnection())
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_selectCommand, System.Data.CommandBehavior.Default))
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
            }
        }



        /// <summary>
        /// Get the number of reactions  made by the identity on a given post.
        /// </summary>
        /// <param name="identity"></param>
        /// <param name="postId"></param>
        /// <returns></returns>
        public int GetIdentityPostReactions(IdentityDatabase db, OdinId identity, Guid driveId, Guid postId)
        {
            using (var _select2Command = _database.CreateCommand())
            {
                _select2Command.CommandText =
                    $"SELECT COUNT(singleReaction) as reactioncount FROM driveReactions WHERE identityId=$identityId AND identity=$identity AND postId=$postId AND driveId = $driveId;";

                var _s2param1 = _select2Command.CreateParameter();
                var _s2param2 = _select2Command.CreateParameter();
                var _s2param3 = _select2Command.CreateParameter();
                var _s2param4 = _select2Command.CreateParameter();

                _s2param1.ParameterName = "$postId";
                _s2param2.ParameterName = "$identity";
                _s2param3.ParameterName = "$driveId";
                _s2param4.ParameterName = "$identityId";

                _select2Command.Parameters.Add(_s2param1);
                _select2Command.Parameters.Add(_s2param2);
                _select2Command.Parameters.Add(_s2param3);
                _select2Command.Parameters.Add(_s2param4);

                _s2param1.Value = postId.ToByteArray();
                _s2param2.Value = identity.DomainName;
                _s2param3.Value = driveId.ToByteArray();
                _s2param4.Value = db._identityId.ToByteArray();

                using (var conn = db.CreateDisposableConnection())
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_select2Command, System.Data.CommandBehavior.Default))
                    {
                        if (rdr.Read())
                            return rdr.GetInt32(0);
                        else
                            return 0;
                    }

                }
            }
        }


        /// <summary>
        /// Get the number of reactions  made by the identity on a given post.
        /// </summary>
        /// <param name="identity"></param>
        /// <param name="postId"></param>
        /// <returns></returns>
        public List<string> GetIdentityPostReactionDetails(IdentityDatabase db, OdinId identity, Guid driveId, Guid postId)
        {
            using (var _select3Command = _database.CreateCommand())
            {
                _select3Command.CommandText =
                    $"SELECT singleReaction as reactioncount FROM driveReactions WHERE identityId=$identityId AND identity=$identity AND postId=$postId AND driveId = $driveId;";

                var _s3param1 = _select3Command.CreateParameter();
                var _s3param2 = _select3Command.CreateParameter();
                var _s3param3 = _select3Command.CreateParameter();
                var _s3param4 = _select3Command.CreateParameter();

                _s3param1.ParameterName = "$postId";
                _s3param2.ParameterName = "$identity";
                _s3param3.ParameterName = "$driveId";
                _s3param4.ParameterName = "$identityId";

                _select3Command.Parameters.Add(_s3param1);
                _select3Command.Parameters.Add(_s3param2);
                _select3Command.Parameters.Add(_s3param3);
                _select3Command.Parameters.Add(_s3param4);

                _s3param1.Value = postId.ToByteArray();
                _s3param2.Value = identity.DomainName;
                _s3param3.Value = driveId.ToByteArray();
                _s3param4.Value = db._identityId.ToByteArray();

                using (var conn = db.CreateDisposableConnection())
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_select3Command, System.Data.CommandBehavior.Default))
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
        }


        public (List<string>, List<int>, int) GetPostReactionsWithDetails(IdentityDatabase db, Guid driveId, Guid postId)
        {
            using (var _select4Command = _database.CreateCommand())
            {
                _select4Command.CommandText =
                    $"SELECT singleReaction, COUNT(singleReaction) as reactioncount FROM driveReactions WHERE identityId=$identityId AND driveId=$driveId AND postId=$postId GROUP BY singleReaction ORDER BY reactioncount DESC;";

                var _s4param1 = _select4Command.CreateParameter();
                var _s4param2 = _select4Command.CreateParameter();
                var _s4param3 = _select4Command.CreateParameter();

                _s4param1.ParameterName = "$postId";
                _s4param2.ParameterName = "$driveId";
                _s4param3.ParameterName = "$identityId";

                _select4Command.Parameters.Add(_s4param1);
                _select4Command.Parameters.Add(_s4param2);
                _select4Command.Parameters.Add(_s4param3);

                _s4param1.Value = postId.ToByteArray();
                _s4param2.Value = driveId.ToByteArray();
                _s4param3.Value = db._identityId.ToByteArray();

                using (var conn = db.CreateDisposableConnection())
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_select4Command, System.Data.CommandBehavior.Default))
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
            }
        }


        // Copied and modified from CRUD
        public List<DriveReactionsRecord> PagingByRowid(IdentityDatabase db, int count, Int32? inCursor, out Int32? nextCursor, Guid driveId, Guid postIdFilter)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");
            if (inCursor == null)
                inCursor = 0;

            using (var _getPaging0Command = _database.CreateCommand())
            {
                _getPaging0Command.CommandText = "SELECT rowid,identity,postId,singleReaction FROM driveReactions " +
                                             "WHERE identityId=$identityId AND driveId = $driveId AND postId = $postId AND rowid > $rowid ORDER BY rowid ASC LIMIT $_count;";

                var _getPaging0Param1 = _getPaging0Command.CreateParameter();
                var _getPaging0Param2 = _getPaging0Command.CreateParameter();
                var _getPaging0Param3 = _getPaging0Command.CreateParameter();
                var _getPaging0Param4 = _getPaging0Command.CreateParameter();
                var _getPaging0Param5 = _getPaging0Command.CreateParameter();

                _getPaging0Param1.ParameterName = "$rowid";
                _getPaging0Param2.ParameterName = "$_count";
                _getPaging0Param3.ParameterName = "$postId";
                _getPaging0Param4.ParameterName = "$driveId";
                _getPaging0Param5.ParameterName = "$identityId";

                _getPaging0Command.Parameters.Add(_getPaging0Param1);
                _getPaging0Command.Parameters.Add(_getPaging0Param2);
                _getPaging0Command.Parameters.Add(_getPaging0Param3);
                _getPaging0Command.Parameters.Add(_getPaging0Param4);
                _getPaging0Command.Parameters.Add(_getPaging0Param5);

                _getPaging0Param1.Value = inCursor;
                _getPaging0Param2.Value = count + 1;
                _getPaging0Param3.Value = postIdFilter.ToByteArray();
                _getPaging0Param4.Value = driveId.ToByteArray();
                _getPaging0Param5.Value = db._identityId.ToByteArray();

                using (var conn = db.CreateDisposableConnection())
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_getPaging0Command, System.Data.CommandBehavior.Default))
                    {
                        var result = new List<DriveReactionsRecord>();
                        int n = 0;
                        int rowid = 0;
                        while ((n < count) && rdr.Read())
                        {
                            n++;
                            var item = new DriveReactionsRecord();
                            byte[] _tmpbuf = new byte[65535 + 1];
                            long bytesRead;
                            var _guid = new byte[16];

                            item.identityId = ((IdentityDatabase)conn.db)._identityId;

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
            } // using
        }
    }
}
