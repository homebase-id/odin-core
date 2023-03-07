using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace Youverse.Core.Storage.Sqlite.IdentityDatabase
{
    public class TableCircle : TableCircleCRUD
    {
        public TableCircle(IdentityDatabase db) : base(db)
        {
        }

        ~TableCircle()
        {
        }

        public override void Dispose()
        {
            base.Dispose();
        }
    }
}
