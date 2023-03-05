using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using System.Security.Principal;
using System.Xml;

namespace Youverse.Core.Storage.Sqlite.IdentityDatabase
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
