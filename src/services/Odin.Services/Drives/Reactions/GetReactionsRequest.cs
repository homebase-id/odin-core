namespace Odin.Services.Drives.Reactions;

public class GetReactionsRequest
{
    public ExternalFileIdentifier File { get; set; }
    public string Cursor { get; set; }
    
    public int MaxRecords { get; set; }
}