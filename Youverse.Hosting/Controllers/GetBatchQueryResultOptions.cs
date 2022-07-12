using System;
using Youverse.Core.Cryptography;
using Youverse.Core.Services.Drive.Query;
using Youverse.Core.Services.Drive.Query.Sqlite.Storage;

namespace Youverse.Hosting.Controllers;

public class GetBatchQueryResultOptions
{
    /// <summary>
    /// Base64 encoded value of the cursor state used when paging/chunking through records
    /// </summary>
    public string CursorState { get; set; }

    /// <summary>
    /// Max number of records to return
    /// </summary>
    public int MaxRecords { get; set; } = 100;

    public bool IncludeMetadataHeader { get; set; }

    public GetBatchResultOptions ToGetBatchResultOptions()
    {
        
        return new GetBatchResultOptions()
        {
            Cursor =  QueryBatchCursor.FromState(this.CursorState),
            MaxRecords = this.MaxRecords,
            IncludeMetadataHeader = this.IncludeMetadataHeader
        };
    }
}