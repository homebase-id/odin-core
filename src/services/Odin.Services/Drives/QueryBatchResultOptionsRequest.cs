using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Services.Drives.DriveCore.Query;

namespace Odin.Services.Drives;

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
    /// Specifies if the result set includes the metadata header (assuming the file has one).  Superseded by
    /// <see cref="ResultShape"/>, and ignored when that is set.
    /// </summary>
    public bool IncludeMetadataHeader { get; set; }

    /// <summary>
    /// What each result row carries.  When null, <see cref="IncludeMetadataHeader"/> decides between
    /// <see cref="FileResultShape.Full"/> and <see cref="FileResultShape.NoContent"/>.
    /// </summary>
    public FileResultShape? ResultShape { get; set; }

    /// <summary>
    /// If true, the transfer history with-in the server metadata will be including (assuming you have set ExcludeServerMetaData = false)
    /// </summary>
    public bool IncludeTransferHistory { get; set; }

    public QueryBatchSortOrder Ordering { get; set; }
    
    public QueryBatchSortField Sorting { get; set; }
    
    public QueryBatchResultOptions ToQueryBatchResultOptions()
    {
        return new QueryBatchResultOptions()
        {
            Cursor = string.IsNullOrEmpty(this.CursorState) ? new QueryBatchCursor() : new QueryBatchCursor(this.CursorState),
            MaxRecords = this.MaxRecords,
            ResultShape = FileResultShapes.Resolve(this.ResultShape, this.IncludeMetadataHeader),
            IncludeTransferHistory = this.IncludeTransferHistory,
            Ordering = this.Ordering,
            Sorting = this.Sorting
        };
    }

    public static QueryBatchResultOptionsRequest Default => new QueryBatchResultOptionsRequest() { MaxRecords = 10 };
}