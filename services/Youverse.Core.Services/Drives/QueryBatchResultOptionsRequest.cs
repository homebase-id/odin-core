using Youverse.Core.Services.Drive.Core.Query;
using Youverse.Core.Storage.SQLite;
using Youverse.Core.Storage.SQLite.DriveDatabase;

namespace Youverse.Core.Services.Drive;

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

    public QueryBatchResultOptions ToQueryBatchResultOptions()
    {
        return new QueryBatchResultOptions()
        {
            Cursor = string.IsNullOrEmpty(this.CursorState) ? new QueryBatchCursor() : new QueryBatchCursor(this.CursorState),
            MaxRecords = this.MaxRecords,
            IncludeJsonContent = this.IncludeMetadataHeader
        };
    }

    public static QueryBatchResultOptionsRequest Default => new QueryBatchResultOptionsRequest() { MaxRecords = 10 };
}