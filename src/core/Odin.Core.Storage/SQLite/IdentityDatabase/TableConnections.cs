using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Identity;
using Odin.Core.Time;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableConnections : TableConnectionsCRUD
    {
        public TableConnections(IdentityDatabase db, CacheHelper cache) : base(db, cache)
        {
        }

        ~TableConnections()
        {
        }

        public ConnectionsRecord Get(DatabaseConnection conn, OdinId identity)
        {
            return base.Get(conn, ((IdentityDatabase)conn.db)._identityId, identity);
        }

        public new int Insert(DatabaseConnection conn, ConnectionsRecord item)
        {
            item.identityId = ((IdentityDatabase)conn.db)._identityId;
            return base.Insert(conn, item);
        }

        public new int Upsert(DatabaseConnection conn, ConnectionsRecord item)
        {
            item.identityId = ((IdentityDatabase)conn.db)._identityId;
            return base.Upsert(conn, item);
        }

        public new int Update(DatabaseConnection conn, ConnectionsRecord item)
        {
            item.identityId = ((IdentityDatabase)conn.db)._identityId;
            return base.Update(conn, item);
        }

        public int Delete(DatabaseConnection conn, OdinId identity)
        {
            return base.Delete(conn, ((IdentityDatabase) conn.db)._identityId, identity);
        }

        public List<ConnectionsRecord> PagingByIdentity(DatabaseConnection conn, int count, string inCursor, out string nextCursor)
        {
            return base.PagingByIdentity(conn, count, ((IdentityDatabase)conn.db)._identityId, inCursor, out nextCursor);
        }

        public List<ConnectionsRecord> PagingByIdentity(DatabaseConnection conn, int count, Int32 status, string inCursor, out string nextCursor)
        {
            return base.PagingByIdentity(conn, count, ((IdentityDatabase)conn.db)._identityId, status, inCursor, out nextCursor);
        }


        public List<ConnectionsRecord> PagingByCreated(DatabaseConnection conn, int count, Int32 status, UnixTimeUtcUnique? inCursor, out UnixTimeUtcUnique? nextCursor)
        {
            return base.PagingByCreated(conn, count, ((IdentityDatabase)conn.db)._identityId, status, inCursor, out nextCursor);
        }

        public List<ConnectionsRecord> PagingByCreated(DatabaseConnection conn, int count, UnixTimeUtcUnique? inCursor, out UnixTimeUtcUnique? nextCursor)
        {
            return base.PagingByCreated(conn, count, ((IdentityDatabase)conn.db)._identityId, inCursor, out nextCursor);
        }

        /*
        public List<ConnectionsRecord> ObsoletePagingByIdentity(DatabaseConnection conn, int count, Int32 statusFilter, string inCursor, out string nextCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");
            if (inCursor == null)
                inCursor = "";

            using (var _getPaging1Command = _database.CreateCommand())
            {
                _getPaging1Command.CommandText = "SELECT identityId,identity,displayName,status,accessIsRevoked,data,created,modified FROM connections " +
                                             "WHERE identityId=$identityId AND identity > $identity AND status = $status ORDER BY identity ASC LIMIT $_count;";
                var _getPaging1Param1 = _getPaging1Command.CreateParameter();
                _getPaging1Command.Parameters.Add(_getPaging1Param1);
                _getPaging1Param1.ParameterName = "$identity";
                var _getPaging1Param2 = _getPaging1Command.CreateParameter();
                _getPaging1Command.Parameters.Add(_getPaging1Param2);
                _getPaging1Param2.ParameterName = "$_count";

                var _getPaging1Param3 = _getPaging1Command.CreateParameter();
                _getPaging1Command.Parameters.Add(_getPaging1Param3);
                _getPaging1Param3.ParameterName = "$status";

                var _getPaging1Param4 = _getPaging1Command.CreateParameter();
                _getPaging1Command.Parameters.Add(_getPaging1Param4);
                _getPaging1Param4.ParameterName = "$identityId";

                _getPaging1Param1.Value = inCursor;
                _getPaging1Param2.Value = count + 1;
                _getPaging1Param3.Value = statusFilter;
                _getPaging1Param4.Value = ((IdentityDatabase)conn.db)._identityId;

                lock (conn._lock)
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_getPaging1Command, System.Data.CommandBehavior.Default))
                    {
                        var result = new List<ConnectionsRecord>();
                        int n = 0;
                        while ((n < count) && rdr.Read())
                        {
                            n++;
                            result.Add(ReadRecordFromReaderAll(rdr));
                        } // while
                        if ((n > 0) && rdr.Read())
                        {
                            nextCursor = result[n - 1].identity;
                        }
                        else
                        {
                            nextCursor = null;
                        }

                        return result;
                    } // using
                } // lock
            } // using 
        } // PagingGet


        public List<ConnectionsRecord> PagingByCreated(DatabaseConnection conn, int count, Int32 statusFilter, UnixTimeUtcUnique? inCursor, out UnixTimeUtcUnique? nextCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");
            if (inCursor == null)
                inCursor = new UnixTimeUtcUnique(long.MaxValue);

            using (var _getPaging6Command = _database.CreateCommand())
            {
                _getPaging6Command.CommandText = "SELECT identityId, identity,displayName,status,accessIsRevoked,data,created,modified FROM connections " +
                                             "WHERE created < $created AND status=$status ORDER BY created DESC LIMIT $_count;";
                var _getPaging6Param1 = _getPaging6Command.CreateParameter();
                _getPaging6Command.Parameters.Add(_getPaging6Param1);
                _getPaging6Param1.ParameterName = "$created";
                var _getPaging6Param2 = _getPaging6Command.CreateParameter();
                _getPaging6Command.Parameters.Add(_getPaging6Param2);
                _getPaging6Param2.ParameterName = "$_count";
                var _getPaging6Param3 = _getPaging6Command.CreateParameter();
                _getPaging6Command.Parameters.Add(_getPaging6Param3);
                _getPaging6Param3.ParameterName = "$status";

                var _getPaging6Param4 = _getPaging6Command.CreateParameter();
                _getPaging6Command.Parameters.Add(_getPaging6Param4);
                _getPaging6Param4.ParameterName = "$identityId";

                _getPaging6Param1.Value = inCursor?.uniqueTime;
                _getPaging6Param2.Value = count + 1;
                _getPaging6Param3.Value = statusFilter;
                _getPaging6Param4.Value = ((IdentityDatabase)conn.db)._identityId;

                lock (conn._lock)
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_getPaging6Command, System.Data.CommandBehavior.Default))
                    {
                        var result = new List<ConnectionsRecord>();
                        int n = 0;
                        while ((n < count) && rdr.Read())
                        {
                            n++;
                            result.Add(ReadRecordFromReaderAll(rdr));
                        } // while
                        if ((n > 0) && rdr.Read())
                        {
                            nextCursor = result[n - 1].created;
                        }
                        else
                        {
                            nextCursor = null;
                        }

                        return result;
                    } // using
                } // lock
            }
        } // PagingGet
        */
    }
}
