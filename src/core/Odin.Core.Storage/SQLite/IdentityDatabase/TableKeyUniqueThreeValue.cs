namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableKeyUniqueThreeValue : TableKeyUniqueThreeValueCRUD
    {
        public TableKeyUniqueThreeValue(IdentityDatabase db, CacheHelper cache) : base(cache)
        {
        }

        ~TableKeyUniqueThreeValue()
        {
        }
    }
}