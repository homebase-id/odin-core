namespace Odin.Core.Services.Drives.DriveCore.Query;

public class ResultOptions
{
    /// <summary>
    /// Max number of records to return
    /// </summary>
    public int MaxRecords { get; set; } = 100;

    public bool IncludeHeaderContent { get; set; }
    
    /// <summary>
    /// Indicates if we should not include the payload ofr the preview thumbnail.  This is good for saving
    /// bandwidth in high load scenarios (i.e. every byte counts)
    /// </summary>
    public bool ExcludePreviewThumbnail { get; set; }
    
    /// <summary>
    /// If true, the ServerMetaData will be excluded from the result, even if the caller is owner or the force flag is true
    /// </summary>
    public bool ExcludeServerMetaData { get; set; }
}