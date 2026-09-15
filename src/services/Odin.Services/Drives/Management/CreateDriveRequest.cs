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
    /// The app that owns the drive; null means an owner drive.
    /// </summary>
    /// <remarks>
    /// Accepted, stored, and read by nothing yet.  Omitting all three leaves the drive addressed by
    /// Guid exactly as before -- which is every drive today.  See <c>docs/drive-addressing.md</c>.
    /// </remarks>
    public Guid? AppId { get; set; }

    /// <summary>
    /// The drive's portable name, unique per owning app.  Validated for format when supplied; not
    /// required, and not derived for you.
    /// </summary>
    public string DriveSlug { get; set; }

    /// <summary>
    /// Readable form of the drive's type, e.g. <c>channel</c>.
    /// </summary>
    public string DriveTypeSlug { get; set; }

    public Dictionary<string, string> Attributes { get; set; }
}