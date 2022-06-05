namespace Youverse.Core.Services.Drive.Query;

public class ResultOptions
{
    public int MaxRecords { get; set; }
    public bool IncludeMetadataHeader { get; set; }

    public bool IncludePayload { get; set; }
}