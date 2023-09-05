using Odin.Core.Services.Drives;

namespace Odin.Core.Services.Peer.ReceivingHost.Quarantine;

/// <summary>
/// Minimal set of drive information to be returned to callers who have Circle access
/// </summary>
public class PerimeterDriveData
{
    public TargetDrive TargetDrive { get; set; }
}