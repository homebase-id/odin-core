using System;
using Microsoft.Data.Sqlite;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.KeyChainDatabase;

namespace Odin.Core.Storage.SQLite.KeyChainDatabase
{
    public class TableKeyChain : TableKeyChainCRUD
    {

        private SqliteCommand _get0Command = null;
        private SqliteCommand _get1Command = null;
        private SqliteParameter _get1Param1 = null;

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
        public KeyChainRecord GetLastLink()
        {
            if (_get0Command == null)
            {
                _get0Command = _database.CreateCommand();
                _get0Command.CommandText = "SELECT previousHash,identity,timestamp,nonce,signedNonce,algorithm,publicKey,recordHash FROM blockChain ORDER BY rowid DESC LIMIT 1;";
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

        // Get oldest 
        public KeyChainRecord Get(string identity)
        {
            if (identity == null) throw new Exception("Cannot be null");
            if (identity?.Length < 0) throw new Exception("Too short");
            if (identity?.Length > 65535) throw new Exception("Too long");

            if (_get1Command == null)
            {
                _get1Command = _database.CreateCommand();
                _get1Command.CommandText = "SELECT previousHash,identity,timestamp,nonce,signedNonce,algorithm,publicKey,recordHash FROM blockChain " +
                                                "WHERE identity = $identity ORDER BY rowid ASC LIMIT 1;";
                _get1Param1 = _get1Command.CreateParameter();
                _get1Command.Parameters.Add(_get1Param1);
                _get1Param1.ParameterName = "$identity";
                _get1Command.Prepare();
            }
            _get1Param1.Value = identity;

            using (SqliteDataReader rdr = _database.ExecuteReader(_get1Command, System.Data.CommandBehavior.SingleRow))
            {
                if (!rdr.Read())
                {
                    return null;
                }
                var r = ReadRecordFromReaderAll(rdr);
                return r;
            } // using
        }

        public override void Dispose()
        {
            base.Dispose();
        }
    }
}
