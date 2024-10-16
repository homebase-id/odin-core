using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace Odin.Core.Storage.SQLite.KeyChainDatabase
{
    public class TableKeyChain : TableKeyChainCRUD
    {
        public TableKeyChain(KeyChainDatabase db, CacheHelper cache) : base(cache)
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
        public KeyChainRecord GetLastLink(DatabaseConnection conn)
        {
            using (var _get0Command = conn.db.CreateCommand())
            {
                _get0Command.CommandText = "SELECT previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,recordHash FROM keyChain ORDER BY rowid DESC LIMIT 1;";

                lock (conn._lock)
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_get0Command, System.Data.CommandBehavior.SingleRow))
                    {
                        if (!rdr.Read())
                        {
                            return null;
                        }
                        var r = ReadRecordFromReaderAll(rdr);
                        return r;
                    } // using
                }
            }
        }

        // Get oldest 
        public KeyChainRecord GetOldest(DatabaseConnection conn, string identity)
        {
            if (identity == null) throw new Exception("Cannot be null");
            if (identity?.Length < 0) throw new Exception("Too short");
            if (identity?.Length > 65535) throw new Exception("Too long");

            using (var _get1Command = conn.db.CreateCommand())
            {
                _get1Command.CommandText = "SELECT previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,recordHash FROM keyChain " +
                                                "WHERE identity = $identity ORDER BY rowid ASC LIMIT 1;";
                var _get1Param1 = _get1Command.CreateParameter();
                _get1Command.Parameters.Add(_get1Param1);
                _get1Param1.ParameterName = "$identity";

                _get1Param1.Value = identity;

                lock (conn._lock)
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_get1Command, System.Data.CommandBehavior.SingleRow))
                    {
                        if (!rdr.Read())
                        {
                            return null;
                        }
                        var r = ReadRecordFromReaderAll(rdr);
                        return r;
                    } // using
                } // lock
            } // using
        }

        public List<KeyChainRecord> GetIdentity(DatabaseConnection conn, string identity)
        {
            if (identity == null) throw new Exception("Cannot be null");
            if (identity?.Length < 0) throw new Exception("Too short");
            if (identity?.Length > 65535) throw new Exception("Too long");

            using (var _get2Command = conn.db.CreateCommand())
            {
                _get2Command.CommandText = "SELECT previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,recordHash FROM keyChain " +
                                             "WHERE identity = $identity ORDER BY rowid;";
                var _get2Param1 = _get2Command.CreateParameter();
                _get2Command.Parameters.Add(_get2Param1);
                _get2Param1.ParameterName = "$identity";

                _get2Param1.Value = identity;

                lock (conn._lock)
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_get2Command, System.Data.CommandBehavior.Default))
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
                } // Lock
            } // using
        }
    }
}
