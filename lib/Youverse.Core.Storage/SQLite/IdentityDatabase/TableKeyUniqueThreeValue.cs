namespace Youverse.Core.Storage.Sqlite.IdentityDatabase
{
    public class TableKeyUniqueThreeValue : TableKeyUniqueThreeValueCRUD
    {
        public TableKeyUniqueThreeValue(IdentityDatabase db) : base(db)
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