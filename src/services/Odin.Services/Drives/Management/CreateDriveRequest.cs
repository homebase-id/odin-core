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

    public Dictionary<string, string> Attributes { get; set; }
}