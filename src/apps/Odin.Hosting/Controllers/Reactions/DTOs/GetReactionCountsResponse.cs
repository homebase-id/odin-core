using System.Collections.Generic;
using Odin.Services.Drives.DriveCore.Query.Sqlite;

namespace Odin.Hosting.Controllers.Reactions.DTOs;

public class GetReactionCountsResponse2
{
    public List<ReactionCount> Reactions { get; set; }
        
    public int Total { get; set; }
}