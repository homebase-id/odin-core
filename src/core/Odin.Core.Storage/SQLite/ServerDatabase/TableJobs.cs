namespace Odin.Core.Storage.SQLite.ServerDatabase;

#nullable enable

public class TableJobs(ServerDatabase db, CacheHelper? cache) : TableJobsCRUD(db, cache)
{
}