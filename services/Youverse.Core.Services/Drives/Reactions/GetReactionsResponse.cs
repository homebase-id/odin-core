using System.Collections.Generic;
using Youverse.Core.Services.Drives.DriveCore.Query.Sqlite;

namespace Youverse.Core.Services.Drives.Reactions;

public class GetReactionCountsResponse
{
    public List<ReactionCount> Reactions { get; set; }
        
    public int Total { get; set; }
}


public class GetReactionsResponse
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