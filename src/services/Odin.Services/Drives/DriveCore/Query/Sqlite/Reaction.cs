using Odin.Core.Identity;
using Odin.Core.Time;

namespace Odin.Services.Drives.DriveCore.Query.Sqlite;

public class Reaction
{
    public OdinId OdinId { get; set; }

    public InternalDriveFileId FileId { get; set; }

    public UnixTimeUtcUnique Created { get; set; }

    public string ReactionContent { get; set; }
}