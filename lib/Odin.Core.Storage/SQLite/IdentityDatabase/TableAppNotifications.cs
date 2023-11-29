namespace Odin.Core.Storage.SQLite.IdentityDatabase;

public class TableAppNotifications: TableAppNotificationsCRUD
{
    public TableAppNotifications(IdentityDatabase db, CacheHelper cache) : base(db, cache)
    {
    }
}