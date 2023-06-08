namespace Youverse.Core.Storage.Sqlite.IdentityDatabase
{
    public class TableKeyUniqueThreeValue : TableKeyUniqueThreeValueCRUD
    {
        public TableKeyUniqueThreeValue(IdentityDatabase db, CacheHelper cache) : base(db, cache)
        {
        }

        ~TableKeyUniqueThreeValue()
        {
        }

        public override void Dispose()
        {
            base.Dispose();
        }
    }
}