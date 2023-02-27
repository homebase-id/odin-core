using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace Youverse.Core.Storage.SQLite.IdentityDatabase
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
