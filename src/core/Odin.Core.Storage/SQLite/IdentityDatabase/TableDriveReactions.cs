using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Odin.Core.Identity;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableDriveReactions : TableDriveReactionsCRUD
    {
        private readonly IdentityDatabase _db;

        public TableDriveReactions(IdentityDatabase db, CacheHelper cache) : base(cache)
        {
            _db = db;
        }

        public async Task<int> DeleteAsync(Guid driveId, OdinId identity, Guid postId, string singleReaction)
        {
            using var conn = _db.CreateDisposableConnection();
            return await base.DeleteAsync(conn, _db._identityId, driveId, identity, postId, singleReaction);
        }

        public async Task<int> DeleteAllReactionsAsync(Guid driveId, OdinId identity, Guid postId)
        {
            using var conn = _db.CreateDisposableConnection();
            return await base.DeleteAllReactionsAsync(conn, _db._identityId, driveId, identity, postId);
        }

        public async Task<int> InsertAsync(DriveReactionsRecord item)
        {
            item.identityId = _db._identityId;
            using var conn = _db.CreateDisposableConnection();
            return await base.InsertAsync(conn, item);
        }

        public async Task<(List<string>, int)> GetPostReactionsAsync(Guid driveId, Guid postId)
        {
            using (var _selectCommand = _db.CreateCommand())
            {
                _selectCommand.CommandText =
                    $"SELECT singleReaction, COUNT(singleReaction) as reactioncount FROM driveReactions WHERE identityId=$identityId AND driveId=$driveId AND postId=$postId GROUP BY singleReaction ORDER BY reactioncount DESC;";

                var sparam1 = _selectCommand.CreateParameter();
                var sparam2 = _selectCommand.CreateParameter();
                var sparam3 = _selectCommand.CreateParameter();

                sparam1.ParameterName = "$postId";
                sparam2.ParameterName = "$driveId";
                sparam3.ParameterName = "$identityId";

                _selectCommand.Parameters.Add(sparam1);
                _selectCommand.Parameters.Add(sparam2);
                _selectCommand.Parameters.Add(sparam3);

                sparam1.Value = postId.ToByteArray();
                sparam2.Value = driveId.ToByteArray();
                sparam3.Value = _db._identityId.ToByteArray();

                using (var conn = _db.CreateDisposableConnection())
                {
                    using (var rdr = await conn.ExecuteReaderAsync(_selectCommand, System.Data.CommandBehavior.Default))
                    {
                        var result = new List<string>();
                        int totalCount = 0;
                        int n = 0;

                        while (await rdr.ReadAsync())
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
        public async Task<int> GetIdentityPostReactionsAsync(OdinId identity, Guid driveId, Guid postId)
        {
            using (var select2Command = _db.CreateCommand())
            {
                select2Command.CommandText =
                    $"SELECT COUNT(singleReaction) as reactioncount FROM driveReactions WHERE identityId=$identityId AND identity=$identity AND postId=$postId AND driveId = $driveId;";

                var s2param1 = select2Command.CreateParameter();
                var s2param2 = select2Command.CreateParameter();
                var s2param3 = select2Command.CreateParameter();
                var s2param4 = select2Command.CreateParameter();

                s2param1.ParameterName = "$postId";
                s2param2.ParameterName = "$identity";
                s2param3.ParameterName = "$driveId";
                s2param4.ParameterName = "$identityId";

                select2Command.Parameters.Add(s2param1);
                select2Command.Parameters.Add(s2param2);
                select2Command.Parameters.Add(s2param3);
                select2Command.Parameters.Add(s2param4);

                s2param1.Value = postId.ToByteArray();
                s2param2.Value = identity.DomainName;
                s2param3.Value = driveId.ToByteArray();
                s2param4.Value = _db._identityId.ToByteArray();

                using (var conn = _db.CreateDisposableConnection())
                {
                    using (var rdr = await conn.ExecuteReaderAsync(select2Command, System.Data.CommandBehavior.Default))
                    {
                        if (await rdr.ReadAsync())
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
        public async Task<List<string>> GetIdentityPostReactionDetailsAsync(OdinId identity, Guid driveId, Guid postId)
        {
            using (var select3Command = _db.CreateCommand())
            {
                select3Command.CommandText =
                    $"SELECT singleReaction as reactioncount FROM driveReactions WHERE identityId=$identityId AND identity=$identity AND postId=$postId AND driveId = $driveId;";

                var s3param1 = select3Command.CreateParameter();
                var s3param2 = select3Command.CreateParameter();
                var s3param3 = select3Command.CreateParameter();
                var s3param4 = select3Command.CreateParameter();

                s3param1.ParameterName = "$postId";
                s3param2.ParameterName = "$identity";
                s3param3.ParameterName = "$driveId";
                s3param4.ParameterName = "$identityId";

                select3Command.Parameters.Add(s3param1);
                select3Command.Parameters.Add(s3param2);
                select3Command.Parameters.Add(s3param3);
                select3Command.Parameters.Add(s3param4);

                s3param1.Value = postId.ToByteArray();
                s3param2.Value = identity.DomainName;
                s3param3.Value = driveId.ToByteArray();
                s3param4.Value = _db._identityId.ToByteArray();

                using (var conn = _db.CreateDisposableConnection())
                {
                    using (var rdr = await conn.ExecuteReaderAsync(select3Command, System.Data.CommandBehavior.Default))
                    {
                        var rs = new List<string>();

                        while (await rdr.ReadAsync())
                        {
                            string s = rdr.GetString(0);
                            rs.Add(s);
                        }

                        return rs;
                    }
                }
            }
        }


        public async Task<(List<string>, List<int>, int)> GetPostReactionsWithDetailsAsync(Guid driveId, Guid postId)
        {
            using (var select4Command = _db.CreateCommand())
            {
                select4Command.CommandText =
                    $"SELECT singleReaction, COUNT(singleReaction) as reactioncount FROM driveReactions WHERE identityId=$identityId AND driveId=$driveId AND postId=$postId GROUP BY singleReaction ORDER BY reactioncount DESC;";

                var s4param1 = select4Command.CreateParameter();
                var s4param2 = select4Command.CreateParameter();
                var s4param3 = select4Command.CreateParameter();

                s4param1.ParameterName = "$postId";
                s4param2.ParameterName = "$driveId";
                s4param3.ParameterName = "$identityId";

                select4Command.Parameters.Add(s4param1);
                select4Command.Parameters.Add(s4param2);
                select4Command.Parameters.Add(s4param3);

                s4param1.Value = postId.ToByteArray();
                s4param2.Value = driveId.ToByteArray();
                s4param3.Value = _db._identityId.ToByteArray();

                using (var conn = _db.CreateDisposableConnection())
                {
                    using (var rdr = await conn.ExecuteReaderAsync(select4Command, System.Data.CommandBehavior.Default))
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
        public async Task<(List<DriveReactionsRecord>, Int32? nextCursor)> PagingByRowidAsync(IdentityDatabase db, int count, Int32? inCursor, Guid driveId, Guid postIdFilter)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");
            if (inCursor == null)
                inCursor = 0;

            using (var paging0Command = db.CreateCommand())
            {
                paging0Command.CommandText = "SELECT rowid,identity,postId,singleReaction FROM driveReactions " +
                                             "WHERE identityId=$identityId AND driveId = $driveId AND postId = $postId AND rowid > $rowid ORDER BY rowid ASC LIMIT $_count;";

                var getPaging0Param1 = paging0Command.CreateParameter();
                var getPaging0Param2 = paging0Command.CreateParameter();
                var getPaging0Param3 = paging0Command.CreateParameter();
                var getPaging0Param4 = paging0Command.CreateParameter();
                var getPaging0Param5 = paging0Command.CreateParameter();

                getPaging0Param1.ParameterName = "$rowid";
                getPaging0Param2.ParameterName = "$_count";
                getPaging0Param3.ParameterName = "$postId";
                getPaging0Param4.ParameterName = "$driveId";
                getPaging0Param5.ParameterName = "$identityId";

                paging0Command.Parameters.Add(getPaging0Param1);
                paging0Command.Parameters.Add(getPaging0Param2);
                paging0Command.Parameters.Add(getPaging0Param3);
                paging0Command.Parameters.Add(getPaging0Param4);
                paging0Command.Parameters.Add(getPaging0Param5);

                getPaging0Param1.Value = inCursor;
                getPaging0Param2.Value = count + 1;
                getPaging0Param3.Value = postIdFilter.ToByteArray();
                getPaging0Param4.Value = driveId.ToByteArray();
                getPaging0Param5.Value = db._identityId.ToByteArray();

                using (var conn = db.CreateDisposableConnection())
                {
                    using (var rdr = await conn.ExecuteReaderAsync(paging0Command, System.Data.CommandBehavior.Default))
                    {
                        var result = new List<DriveReactionsRecord>();
                        Int32? nextCursor;
                        
                        int n = 0;
                        int rowid = 0;
                        while ((n < count) && await rdr.ReadAsync())
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
                        if ((n > 0) && await rdr.ReadAsync())
                        {
                            nextCursor = rowid;
                        }
                        else
                        {
                            nextCursor = null;
                        }

                        return (result, nextCursor);
                    } // using
                }
            } // using
        }
    }
}
