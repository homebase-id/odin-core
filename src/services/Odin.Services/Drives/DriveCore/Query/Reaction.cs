using Odin.Core.Time;

namespace Odin.Services.Drives.DriveCore.Query;

public class Reaction
{
    public string OdinId { get; set; }

    public InternalDriveFileId FileId { get; set; }

    public UnixTimeUtc Created { get; set; }

    public string ReactionContent { get; set; }
}