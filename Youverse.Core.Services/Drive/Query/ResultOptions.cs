namespace Youverse.Core.Services.Drive.Query;

public class ResultOptions
{
    public byte[] StartCursor { get; set; }
    
    public byte[] StopCursor { get; set; }
    
    /// <summary>
    /// Max number of records to return
    /// </summary>
    public int MaxRecords { get; set; } = 100;
    
    public bool IncludeMetadataHeader { get; set; }

}