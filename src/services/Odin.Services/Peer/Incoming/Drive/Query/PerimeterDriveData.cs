using Odin.Services.Drives;

namespace Odin.Services.Peer.Incoming.Drive.Query;

/// <summary>
/// Minimal set of drive information to be returned to callers who have Circle access
/// </summary>
public class PerimeterDriveData
{
    public TargetDrive TargetDrive { get; set; }
}