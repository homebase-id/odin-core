using System;
using System.Data.SQLite;

namespace Youverse.Core.Storage.SQLite.IdentityDatabase
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