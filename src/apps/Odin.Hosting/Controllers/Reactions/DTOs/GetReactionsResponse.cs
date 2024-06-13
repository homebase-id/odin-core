using System.Collections.Generic;
using Odin.Services.Drives.DriveCore.Query.Sqlite;

namespace Odin.Hosting.Controllers.Reactions.DTOs;

public class GetReactionsResponse2
{
    public List<Reaction> Reactions { get; set; }
        
    public int? Cursor { get; set; }
}