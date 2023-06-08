using System.Collections.Generic;
using Odin.Core.Services.Drives.DriveCore.Query.Sqlite;

namespace Odin.Core.Services.Drives.Reactions;

public class GetReactionsResponse
{
    public List<Reaction> Reactions { get; set; }
        
    public int? Cursor { get; set; }
}