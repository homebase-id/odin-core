using System;
using Microsoft.Data.Sqlite;

namespace Youverse.Core.Storage.Sqlite.IdentityDatabase
{
    public class TableKeyValue : TableKeyValueCRUD
    {
        public TableKeyValue(IdentityDatabase db) : base(db)
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