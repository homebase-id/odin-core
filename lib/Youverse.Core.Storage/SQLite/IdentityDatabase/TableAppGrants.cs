using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace Youverse.Core.Storage.SQLite.IdentityDatabase
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
