using System.Collections.Generic;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drives.DriveCore.Query.Sqlite;

namespace Youverse.Core.Services.Drives.Reactions;

public class GetReactionsResponse
{
    public List<string> Reactions { get; set; }
        
    public int TotalCount { get; set; }
}

public class GetReactionsResponse2
{
    public List<Reaction> Reactions { get; set; }
        
    public int? Cursor { get; set; }
}

public class GetReactionsRequest
{
    public ExternalFileIdentifier File { get; set; }
    public int Cursor { get; set; }
    
    public int MaxRecords { get; set; }
}