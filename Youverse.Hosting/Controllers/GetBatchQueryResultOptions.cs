using Youverse.Core.Services.Drive.Query;

namespace Youverse.Hosting.Controllers;

public class GetRecentQueryResultOptions
{
    public ulong MaxDate { get; set; }
    
    public ulong Cursor { get; set; }
    
    public int MaxRecords { get; set; } = 100;
    
    public bool IncludeMetadataHeader { get; set; }
    
    public ResultOptions ToResultOptions()
    {
        return new ResultOptions()
        {
            MaxRecords = this.MaxRecords,
            IncludeMetadataHeader = this.IncludeMetadataHeader
        };
    }
}

public class GetBatchQueryResultOptions
{
    /// <summary>
    /// Base64 encoded value of the cursor 
    /// </summary>
    public string Cursor64 { get; set; }

    /// <summary>
    /// Max number of records to return
    /// </summary>
    public int MaxRecords { get; set; } = 100;

    public bool IncludeMetadataHeader { get; set; }

    public ResultOptions ToResultOptions()
    {
        return new ResultOptions()
        {
            MaxRecords = this.MaxRecords,
            IncludeMetadataHeader = this.IncludeMetadataHeader
        };
    }
}