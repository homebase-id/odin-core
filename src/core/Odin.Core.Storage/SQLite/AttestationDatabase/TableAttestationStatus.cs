using System;
using Microsoft.Data.Sqlite;
using Odin.Core.Storage.SQLite;

namespace Odin.Core.Storage.SQLite.AttestationDatabase
{
    public class TableAttestationStatus : TableAttestationStatusCRUD
    {
        public TableAttestationStatus(CacheHelper cache) : base(cache)
        {
        }
    }
}
