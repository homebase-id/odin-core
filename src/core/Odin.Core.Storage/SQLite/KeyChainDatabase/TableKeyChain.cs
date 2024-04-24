using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace Odin.Core.Storage.SQLite.KeyChainDatabase
{
    public class TableKeyChain : TableKeyChainCRUD
    {

        private SqliteCommand _get0Command = null;
        private SqliteCommand _get1Command = null;
        private SqliteParameter _get1Param1 = null;


        private SqliteCommand _get2Command = null;
        private static Object _get2Lock = new Object();
        private SqliteParameter _get2Param1 = null;

        public TableKeyChain(KeyChainDatabase db, CacheHelper cache) : base(db, cache)
        {
        }

        ~TableKeyChain()
        {
        }

        /// <summary>
        /// Get the last link in the chain, will return NULL if this is the first link
        /// </summary>
        /// <param name="identity"></param>
        /// <param name="rsakey"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public KeyChainRecord GetLastLink(DatabaseBase.DatabaseConnection conn)
        {
            if (_get0Command == null)
            {
                _get0Command = _database.CreateCommand(conn);
                _get0Command.CommandText = "SELECT previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,recordHash FROM keyChain ORDER BY rowid DESC LIMIT 1;";
            }

            using (SqliteDataReader rdr = _database.ExecuteReader(conn, _get0Command, System.Data.CommandBehavior.SingleRow))
            {
                if (!rdr.Read())
                {
                    return null;
                }
                var r = ReadRecordFromReaderAll(rdr);
                return r;
            } // using
        }

        // Get oldest 
        public KeyChainRecord GetOldest(DatabaseBase.DatabaseConnection conn, string identity)
        {
            if (identity == null) throw new Exception("Cannot be null");
            if (identity?.Length < 0) throw new Exception("Too short");
            if (identity?.Length > 65535) throw new Exception("Too long");

            if (_get1Command == null)
            {
                _get1Command = _database.CreateCommand(conn);
                _get1Command.CommandText = "SELECT previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,recordHash FROM keyChain " +
                                                "WHERE identity = $identity ORDER BY rowid ASC LIMIT 1;";
                _get1Param1 = _get1Command.CreateParameter();
                _get1Command.Parameters.Add(_get1Param1);
                _get1Param1.ParameterName = "$identity";
                _get1Command.Prepare();
            }
            _get1Param1.Value = identity;

            using (SqliteDataReader rdr = _database.ExecuteReader(conn, _get1Command, System.Data.CommandBehavior.SingleRow))
            {
                if (!rdr.Read())
                {
                    return null;
                }
                var r = ReadRecordFromReaderAll(rdr);
                return r;
            } // using
        }

        public List<KeyChainRecord> GetIdentity(DatabaseBase.DatabaseConnection conn, string identity)
        {
            if (identity == null) throw new Exception("Cannot be null");
            if (identity?.Length < 0) throw new Exception("Too short");
            if (identity?.Length > 65535) throw new Exception("Too long");
            lock (_get2Lock)
            {
                if (_get2Command == null)
                {
                    _get2Command = _database.CreateCommand(conn);
                    _get2Command.CommandText = "SELECT previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,recordHash FROM keyChain " +
                                                 "WHERE identity = $identity ORDER BY rowid;";
                    _get2Param1 = _get2Command.CreateParameter();
                    _get2Command.Parameters.Add(_get2Param1);
                    _get2Param1.ParameterName = "$identity";
                    _get2Command.Prepare();
                }
                _get2Param1.Value = identity;
                using (SqliteDataReader rdr = _database.ExecuteReader(conn, _get2Command, System.Data.CommandBehavior.Default))
                {
                    if (!rdr.Read())
                    {
                        return null;
                    }
                    var result = new List<KeyChainRecord>();
                    while (true)
                    {
                        result.Add(ReadRecordFromReaderAll(rdr));
                        if (!rdr.Read())
                            break;
                    }
                    return result;
                } // using
            } // lock
        }
    }
}
