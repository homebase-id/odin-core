using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity.Abstractions;

namespace Odin.Services.Drives.DriveCore.Query;

public class QueryBatchResultOptions : ResultOptions
{
    public QueryBatchCursor Cursor { get; set; }

    public QueryBatchSortOrder Ordering { get; set; } = QueryBatchSortOrder.Default;

    public QueryBatchSortField Sorting { get; set; } = QueryBatchSortField.CreatedDate;
}
