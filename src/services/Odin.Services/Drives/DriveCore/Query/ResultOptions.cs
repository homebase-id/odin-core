using System.Text.Json.Serialization;

namespace Odin.Services.Drives.DriveCore.Query;

public class ResultOptions
{
    /// <summary>
    /// Max number of records to return
    /// </summary>
    public int MaxRecords { get; set; } = 100;

    /// <summary>
    /// What each result row carries.  Not serialized: these options travel to peers (e.g. in a query-modified
    /// request), whose wire format knows only <see cref="IncludeHeaderContent"/>.
    /// </summary>
    [JsonIgnore]
    public QueryBatchResultShape ResultShape { get; set; } = QueryBatchResultShape.NoContent;

    /// <summary>
    /// <see cref="ResultShape"/> as the legacy flag, so the two can never disagree
    /// </summary>
    public bool IncludeHeaderContent
    {
        get => ResultShape == QueryBatchResultShape.Full;
        set => ResultShape = value ? QueryBatchResultShape.Full : QueryBatchResultShape.NoContent;
    }

    /// <summary>
    /// Indicates if we should not include the payload ofr the preview thumbnail.  This is good for saving
    /// bandwidth in high load scenarios (i.e. every byte counts)
    /// </summary>
    public bool ExcludePreviewThumbnail { get; set; }

    /// <summary>
    /// If true, the ServerMetaData will be excluded from the result, even if the caller is owner or the force flag is true
    /// </summary>
    public bool ExcludeServerMetaData { get; set; }

    /// <summary>
    /// If true, the transfer history with-in the server metadata will be including (assuming you have set ExcludeServerMetaData = false)
    /// </summary>
    public bool IncludeTransferHistory { get; set; }
}
