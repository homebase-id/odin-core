using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Security.Principal;
using System.Xml;

namespace Youverse.Core.Storage.SQLite.IdentityDatabase
{
    public class TableConnections : TableConnectionsCRUD
    {
        public TableConnections(IdentityDatabase db) : base(db)
        {
        }

        ~TableConnections()
        {
        }

        public override void Dispose()
        {
            base.Dispose();
        }
    }
}
