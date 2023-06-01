using System;
using Microsoft.Data.Sqlite;
using Youverse.Core.Storage.SQLite;

namespace Youverse.Core.Storage.Sqlite.IdentityDatabase
{
    public class TableKeyValue : TableKeyValueCRUD
    {
        public TableKeyValue(IdentityDatabase db, CacheHelper cache) : base(db, cache)
        {
        }

        ~TableKeyValue()
        {
        }

        public override void Dispose()
        {
            base.Dispose();
        }
    }
}