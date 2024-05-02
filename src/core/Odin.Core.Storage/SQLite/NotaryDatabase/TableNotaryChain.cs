using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace Odin.Core.Storage.SQLite.NotaryDatabase
{
    public class TableNotaryChain : TableNotaryChainCRUD
    {

        private SqliteCommand _get0Command = null;


        private SqliteCommand _get2Command = null;
        private static Object _get2Lock = new Object();
        private SqliteParameter _get2Param1 = null;

        public TableNotaryChain(NotaryDatabase db, CacheHelper cache) : base(db, cache)
        {
        }

        ~TableNotaryChain()
        {
        }

        /// <summary>
        /// Get the last link in the chain, will return NULL if this is the first link
        /// </summary>
        /// <param name="identity"></param>
        /// <param name="rsakey"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public NotaryChainRecord GetLastLink()
        {
            if (_get0Command == null)
            {
                _get0Command = _database.CreateCommand();
                _get0Command.CommandText = "SELECT previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,notarySignature,recordHash FROM notaryChain ORDER BY rowid DESC LIMIT 1;";
            }

            using (SqliteDataReader rdr = _database.ExecuteReader(_get0Command, System.Data.CommandBehavior.SingleRow))
            {
                if (!rdr.Read())
                {
                    return null;
                }
                var r = ReadRecordFromReaderAll(rdr);
                return r;
            } // using
        }

        public List<NotaryChainRecord> GetIdentity(string identity)
        {
            if (identity == null) throw new Exception("Cannot be null");
            if (identity?.Length < 0) throw new Exception("Too short");
            if (identity?.Length > 65535) throw new Exception("Too long");
            lock (_get2Lock)
            {
                if (_get2Command == null)
                {
                    _get2Command = _database.CreateCommand();
                    _get2Command.CommandText = "SELECT previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,notarySignature,recordHash FROM notaryChain " +
                                                 "WHERE identity = $identity ORDER BY rowid;";
                    _get2Param1 = _get2Command.CreateParameter();
                    _get2Command.Parameters.Add(_get2Param1);
                    _get2Param1.ParameterName = "$identity";
                    _get2Command.Prepare();
                }
                _get2Param1.Value = identity;
                using (SqliteDataReader rdr = _database.ExecuteReader(_get2Command, System.Data.CommandBehavior.Default))
                {
                    if (!rdr.Read())
                    {
                        return null;
                    }
                    var result = new List<NotaryChainRecord>();
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

        public override void Dispose()
        {
            base.Dispose();
        }
    }
}
