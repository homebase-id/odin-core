using System;
using Microsoft.Data.Sqlite;
using Odin.Core.Storage.SQLite;

namespace Odin.Core.Storage.SQLite.AttestationDatabase
{
    public class TableAttestationChain : TableAttestationChainCRUD
    {
        public TableAttestationChain(AttestationDatabase db, CacheHelper cache) : base(db, cache)
        {
        }

        ~TableAttestationChain()
        {
        }

        public override void Dispose()
        {
            base.Dispose();
        }
    }
}
