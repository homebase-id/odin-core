using System;
using System.Collections.Generic;

namespace Odin.Services.Drives.Management;

public class CreateDriveRequest
{
    public string Name { get; set; }
    public TargetDrive TargetDrive { get; set; }
    public string Metadata { get; set; }
    public bool AllowAnonymousReads { get; set; }

    public bool AllowSubscriptions { get; set; }

    /// <summary>
    /// Specifies if the CDN may read this drive's payloads. Opt-in: a caller that omits this
    /// gets a drive the CDN cannot read. The Public Posts system drive is seeded with it set
    /// (see <see cref="SystemDriveConstants"/>); everything else is the owner's choice.
    /// </summary>
    public bool AllowCdn { get; set; }

    public bool OwnerOnly { get; set; }

    /// <summary>
    /// The app that owns the drive.  Omitting it means the owner acting as themselves, which is the
    /// owner-console app -- never nobody.
    /// </summary>
    /// <remarks>
    /// The app must exist: one the platform ships, or one registered on this identity.  Ownership is
    /// half the drive's wire address (<c>/apps/{appSlug}/drives/{driveSlug}</c>) and is not changeable
    /// afterwards except by adoption, so it is checked when the drive is written.
    /// </remarks>
    public Guid? AppId { get; set; }

    /// <summary>
    /// The drive's portable name, unique per owning app.  Validated for format when supplied, and
    /// derived from <see cref="Name"/> when it is not -- every drive ends up with one.
    /// </summary>
    public string DriveSlug { get; set; }

    /// <summary>
    /// Readable form of the drive's type, e.g. <c>channel</c>.  Derived from the drive's type when
    /// omitted, falling back to <c>drive</c> for a type nothing recognises.
    /// </summary>
    public string DriveTypeSlug { get; set; }

    public Dictionary<string, string> Attributes { get; set; }
}