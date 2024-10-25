namespace WaitingListApi.Data.Database
{
    public class WaitingListTable : WaitingListTableCrud
    {

        public WaitingListTable(WaitingListDatabase db) : base(db)
        {
        }

        public override int Insert(WaitingListRecord item)
        {
            return base.Insert(item);
        }

    }
}