using System;

namespace Youverse.Core.Services.Drives.DriveCore.Query.Sqlite;

public class Reaction
{
    public Guid FileId { get; set; }
    public string OdinId { get; set; }

    public UnixTimeUtcUnique Created { get; set; }
    public string ReactionText { get; set; }
}