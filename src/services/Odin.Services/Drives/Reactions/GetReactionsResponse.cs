using System.Collections.Generic;
using Odin.Services.Drives.DriveCore.Query;

namespace Odin.Services.Drives.Reactions;

public class GetReactionsResponse
{
    public List<Reaction> Reactions { get; set; }
        
    public int? Cursor { get; set; }
}