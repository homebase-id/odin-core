using System;

namespace Youverse.Core.Services.Drive;

/// <summary>
///  A drive specifier for incoming requests to perform actions on a drive.  (essentially, this hides the internal DriveId).
/// </summary>
public class TargetDrive
{
    public Guid Alias { get; set; }
    public Guid Type { get; set; }

    public bool IsValid()
    {
        return this.Alias != Guid.Empty && this.Type != Guid.Empty;
    }

    public static TargetDrive NewTargetDrive()
    {
        return new TargetDrive()
        {
            Alias = Guid.NewGuid(),
            Type = Guid.NewGuid()
        };
    }
}