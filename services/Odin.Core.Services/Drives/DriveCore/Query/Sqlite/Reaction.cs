namespace Youverse.Core.Services.Drives.DriveCore.Query.Sqlite;

public class Reaction
{
    public string OdinId { get; set; }

    public InternalDriveFileId FileId { get; set; }

    public UnixTimeUtcUnique Created { get; set; }

    public string ReactionContent { get; set; }
}