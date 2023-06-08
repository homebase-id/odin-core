using System.Collections.Generic;
using Youverse.Core.Services.Drives.DriveCore.Query.Sqlite;

namespace Youverse.Core.Services.Drives.Reactions;

public class GetReactionsResponse
{
    public List<Reaction> Reactions { get; set; }
        
    public int? Cursor { get; set; }
}