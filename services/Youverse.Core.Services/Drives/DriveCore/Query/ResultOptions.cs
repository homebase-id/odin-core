namespace Youverse.Core.Services.Drive.Core.Query;

public class ResultOptions
{
    /// <summary>
    /// Max number of records to return
    /// </summary>
    public int MaxRecords { get; set; } = 100;

    public bool IncludeJsonContent { get; set; }
    
    /// <summary>
    /// Indicates if we should not include the payload ofr the preview thumbnail.  This is good for saving
    /// bandwidth in high load scenarios (i.e. every byte counts)
    /// </summary>
    public bool ExcludePreviewThumbnail { get; set; }
}