using Youverse.Core.Storage;
using Youverse.Core.Storage.SQLite;

namespace Youverse.Hosting.Controllers;

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

    public Core.Services.Drive.Query.QueryBatchResultOptions ToQueryBatchResultOptions()
    {
        
        return new Core.Services.Drive.Query.QueryBatchResultOptions()
        {
            Cursor =  new QueryBatchCursor(this.CursorState),
            MaxRecords = this.MaxRecords,
            IncludeJsonContent = this.IncludeMetadataHeader
        };
    }
}