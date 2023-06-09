namespace Odin.Core.Services.Drives.Reactions;

public class GetReactionsRequest
{
    public ExternalFileIdentifier File { get; set; }
    public int Cursor { get; set; }
    
    public int MaxRecords { get; set; }
}