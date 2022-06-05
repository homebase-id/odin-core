namespace Youverse.Core.Services.Drive.Query;

public class ResultOptions
{
    /// <summary>
    /// Max number of records to return
    /// </summary>
    public int MaxRecords { get; set; } = 100;
    public bool IncludeMetadataHeader { get; set; }

    public bool IncludePayload { get; set; }
}