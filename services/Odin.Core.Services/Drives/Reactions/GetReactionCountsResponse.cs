using System.Collections.Generic;
using Odin.Core.Services.Drives.DriveCore.Query.Sqlite;

namespace Odin.Core.Services.Drives.Reactions;

public class GetReactionCountsResponse
{
    public List<ReactionCount> Reactions { get; set; }
        
    public int Total { get; set; }
}