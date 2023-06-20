using System;
using Microsoft.Data.Sqlite;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.BlockChainDatabase;

namespace Odin.Core.Storage.SQLite.BlockChainDatabase
{
    public class TableBlockChain : TableBlockChainCRUD
    {

        private SqliteCommand _get0Command = null;

        public TableBlockChain(BlockChainDatabase db, CacheHelper cache) : base(db, cache)
        {
        }

        ~TableBlockChain()
        {
        }

        /// <summary>
        /// Get the last link in the chain, will return NULL if this is the first link
        /// </summary>
        /// <param name="identity"></param>
        /// <param name="rsakey"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public BlockChainRecord GetLastLink()
        {
            if (_get0Command == null)
            {
                _get0Command = _database.CreateCommand();
                _get0Command.CommandText = "SELECT hash,identity,rsakey,signature,created,modified FROM blockChain ORDER BY rowid DESC LIMIT 1;";
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

        public override void Dispose()
        {
            base.Dispose();
        }
    }
}
