using Youverse.Core.Services.Drives;

namespace Youverse.Core.Services.Transit.Quarantine;

/// <summary>
/// Minimal set of drive information to be returned to callers who have Circle access
/// </summary>
public class PerimeterDriveData
{
    public TargetDrive TargetDrive { get; set; }
}