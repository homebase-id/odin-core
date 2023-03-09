using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.Sqlite;

namespace Youverse.Core.Storage.Sqlite.IdentityDatabase
{
    public class TableKeyThreeValue : TableKeyThreeValueCRUD
    {
        public TableKeyThreeValue(IdentityDatabase db) : base(db)
        {
        }

        ~TableKeyThreeValue()
        {
        }

        public override void Dispose()
        {
            base.Dispose();
        }
    }
}