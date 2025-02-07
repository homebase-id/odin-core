using Odin.Core.Storage;

namespace Odin.Services.Drives.DriveCore.Query;

public class QueryBatchResultOptions : ResultOptions
{
    public QueryBatchCursor Cursor { get; set; }

    public Ordering Ordering { get; set; } = Ordering.Default;

    public Sorting Sorting { get; set; } = Sorting.FileId;
}

public enum Sorting
{
    FileId = 0,
    UserDate = 1
}

public enum Ordering
{
    Default = 0,
    NewestFirst = 1,
    OldestFirst = 2
}