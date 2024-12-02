using System.Collections.Generic;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Query.Sqlite;

namespace Odin.Services.Drives.Reactions;

public class GetReactionCountsResponse
{
    public List<ReactionCount> Reactions { get; set; }
        
    public int Total { get; set; }
}