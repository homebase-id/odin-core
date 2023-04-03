using Youverse.Core.Storage.Sqlite.DriveDatabase;

namespace Youverse.Core.Services.Drives.DriveCore.Query;

public class QueryBatchResultOptions : ResultOptions
{
    public QueryBatchCursor Cursor { get; set; }

    public Ordering Ordering { get; set; }
}

public enum Ordering
{
    Default = 0,
    NewestFirst = 1,
    OldestFirst = 2
}