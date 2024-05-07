namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableCircle : TableCircleCRUD
    {
        public TableCircle(IdentityDatabase db, CacheHelper cache) : base(db, cache)
        {
        }

        ~TableCircle()
        {
        }
    }
}
