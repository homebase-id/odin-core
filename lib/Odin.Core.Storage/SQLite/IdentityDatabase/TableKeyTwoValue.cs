using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.Sqlite;

namespace Youverse.Core.Storage.Sqlite.IdentityDatabase
{
    public class TableKeyTwoValue : TableKeyTwoValueCRUD
    {
        public TableKeyTwoValue(IdentityDatabase db, CacheHelper cache) : base(db, cache)
        {
        }

        ~TableKeyTwoValue()
        {
        }

        public override void Dispose()
        {
            base.Dispose();
        }
    }
}
