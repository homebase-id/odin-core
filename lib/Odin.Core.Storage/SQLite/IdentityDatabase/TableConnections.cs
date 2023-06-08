using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using System.Security.Principal;
using System.Xml;

namespace Youverse.Core.Storage.Sqlite.IdentityDatabase
{
    public class TableConnections : TableConnectionsCRUD
    {
        private SqliteCommand _getPaging1Command = null;
        private static Object _getPaging1Lock = new Object();
        private SqliteParameter _getPaging1Param1 = null;
        private SqliteParameter _getPaging1Param2 = null;
        private SqliteParameter _getPaging1Param3 = null;

        private SqliteCommand _getPaging6Command = null;
        private static Object _getPaging6Lock = new Object();
        private SqliteParameter _getPaging6Param1 = null;
        private SqliteParameter _getPaging6Param2 = null;
        private SqliteParameter _getPaging6Param3 = null;

        public TableConnections(IdentityDatabase db, CacheHelper cache) : base(db, cache)
        {
        }

        ~TableConnections()
        {
        }

        public override void Dispose()
        {
            base.Dispose();
        }

        public List<ConnectionsRecord> PagingByIdentity(int count, Int32 statusFilter, string inCursor, out string nextCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");
            if (inCursor == null)
                inCursor = "";

            lock (_getPaging1Lock)
            {
                if (_getPaging1Command == null)
                {
                    _getPaging1Command = _database.CreateCommand();
                    _getPaging1Command.CommandText = "SELECT identity,displayName,status,accessIsRevoked,data,created,modified FROM connections " +
                                                 "WHERE identity > $identity AND status = $status ORDER BY identity ASC LIMIT $_count;";
                    _getPaging1Param1 = _getPaging1Command.CreateParameter();
                    _getPaging1Command.Parameters.Add(_getPaging1Param1);
                    _getPaging1Param1.ParameterName = "$identity";
                    _getPaging1Param2 = _getPaging1Command.CreateParameter();
                    _getPaging1Command.Parameters.Add(_getPaging1Param2);
                    _getPaging1Param2.ParameterName = "$_count";

                    _getPaging1Param3 = _getPaging1Command.CreateParameter();
                    _getPaging1Command.Parameters.Add(_getPaging1Param3);
                    _getPaging1Param3.ParameterName = "$status";
                    _getPaging1Command.Prepare();
                }
                _getPaging1Param1.Value = inCursor;
                _getPaging1Param2.Value = count + 1;
                _getPaging1Param3.Value = statusFilter;

                using (SqliteDataReader rdr = _database.ExecuteReader(_getPaging1Command, System.Data.CommandBehavior.Default))
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
        } // PagingGet


        public List<ConnectionsRecord> PagingByCreated(int count, Int32 statusFilter, UnixTimeUtcUnique? inCursor, out UnixTimeUtcUnique? nextCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");
            if (inCursor == null)
                inCursor = new UnixTimeUtcUnique(long.MaxValue);

            lock (_getPaging6Lock)
            {
                if (_getPaging6Command == null)
                {
                    _getPaging6Command = _database.CreateCommand();
                    _getPaging6Command.CommandText = "SELECT identity,displayName,status,accessIsRevoked,data,created,modified FROM connections " +
                                                 "WHERE created < $created AND status=$status ORDER BY created DESC LIMIT $_count;";
                    _getPaging6Param1 = _getPaging6Command.CreateParameter();
                    _getPaging6Command.Parameters.Add(_getPaging6Param1);
                    _getPaging6Param1.ParameterName = "$created";
                    _getPaging6Param2 = _getPaging6Command.CreateParameter();
                    _getPaging6Command.Parameters.Add(_getPaging6Param2);
                    _getPaging6Param2.ParameterName = "$_count";
                    _getPaging6Param3 = _getPaging6Command.CreateParameter();
                    _getPaging6Command.Parameters.Add(_getPaging6Param3);
                    _getPaging6Param3.ParameterName = "$status";
                    _getPaging6Command.Prepare();
                }
                _getPaging6Param1.Value = inCursor?.uniqueTime;
                _getPaging6Param2.Value = count + 1;
                _getPaging6Param3.Value = statusFilter;

                using (SqliteDataReader rdr = _database.ExecuteReader(_getPaging6Command, System.Data.CommandBehavior.Default))
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
        } // PagingGet
    }
}
