using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace Youverse.Core.Storage.Sqlite.IdentityDatabase
{
    public class TableAppGrants : TableAppGrantsCRUD
    {
        public TableAppGrants(IdentityDatabase db) : base(db)
        {
        }

        ~TableAppGrants()
        {
        }

        public override void Dispose()
        {
            base.Dispose();
        }
    }
}
