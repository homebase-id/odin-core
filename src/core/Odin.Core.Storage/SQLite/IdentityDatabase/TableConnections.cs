using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
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

        public List<ConnectionsRecord> PagingByIdentity(DatabaseConnection conn, int count, Int32 statusFilter, string inCursor, out string nextCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");
            if (inCursor == null)
                inCursor = "";

            using (var _getPaging1Command = _database.CreateCommand())
            {
                _getPaging1Command.CommandText = "SELECT identity,displayName,status,accessIsRevoked,data,created,modified FROM connections " +
                                             "WHERE identity > $identity AND status = $status ORDER BY identity ASC LIMIT $_count;";
                var _getPaging1Param1 = _getPaging1Command.CreateParameter();
                _getPaging1Command.Parameters.Add(_getPaging1Param1);
                _getPaging1Param1.ParameterName = "$identity";
                var _getPaging1Param2 = _getPaging1Command.CreateParameter();
                _getPaging1Command.Parameters.Add(_getPaging1Param2);
                _getPaging1Param2.ParameterName = "$_count";

                var _getPaging1Param3 = _getPaging1Command.CreateParameter();
                _getPaging1Command.Parameters.Add(_getPaging1Param3);
                _getPaging1Param3.ParameterName = "$status";

                _getPaging1Param1.Value = inCursor;
                _getPaging1Param2.Value = count + 1;
                _getPaging1Param3.Value = statusFilter;

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
                _getPaging6Command.CommandText = "SELECT identity,displayName,status,accessIsRevoked,data,created,modified FROM connections " +
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

                _getPaging6Param1.Value = inCursor?.uniqueTime;
                _getPaging6Param2.Value = count + 1;
                _getPaging6Param3.Value = statusFilter;

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
    }
}
