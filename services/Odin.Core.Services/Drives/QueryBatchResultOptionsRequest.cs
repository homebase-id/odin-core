using Odin.Core.Services.Drives.DriveCore.Query;
using Odin.Core.Storage.SQLite.IdentityDatabase;

namespace Odin.Core.Services.Drives;

public class QueryBatchResultOptionsRequest
{
    /// <summary>
    /// Base64 encoded value of the cursor state used when paging/chunking through records
    /// </summary>
    public string CursorState { get; set; }

    /// <summary>
    /// Max number of records to return
    /// </summary>
    public int MaxRecords { get; set; } = 100;

    /// <summary>
    /// Specifies if the result set includes the metadata header (assuming the file has one)
    /// </summary>
    public bool IncludeMetadataHeader { get; set; }

    public Ordering Ordering { get; set; }
    
    public Sorting Sorting { get; set; }
    
    public QueryBatchResultOptions ToQueryBatchResultOptions()
    {
        return new QueryBatchResultOptions()
        {
            Cursor = string.IsNullOrEmpty(this.CursorState) ? new QueryBatchCursor() : new QueryBatchCursor(this.CursorState),
            MaxRecords = this.MaxRecords,
            IncludeHeaderContent = this.IncludeMetadataHeader,
            Ordering = this.Ordering,
            Sorting = this.Sorting
        };
    }

    public static QueryBatchResultOptionsRequest Default => new QueryBatchResultOptionsRequest() { MaxRecords = 10 };
}