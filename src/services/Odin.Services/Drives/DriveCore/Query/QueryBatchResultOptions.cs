using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity.Abstractions;

namespace Odin.Services.Drives.DriveCore.Query;

public class QueryBatchResultOptions : ResultOptions
{
    public QueryBatchCursor Cursor { get; set; }

    public QueryBatchOrdering Ordering { get; set; } = QueryBatchOrdering.Default;

    public QueryBatchType Sorting { get; set; } = QueryBatchType.CreatedDate;
}

/*
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
}*/