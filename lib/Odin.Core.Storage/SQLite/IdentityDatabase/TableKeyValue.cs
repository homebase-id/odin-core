namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableKeyValue : TableKeyValueCRUD
    {
        public TableKeyValue(IdentityDatabase db, CacheHelper cache) : base(db, cache)
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