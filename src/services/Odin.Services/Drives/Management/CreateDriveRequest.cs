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
    /// Specifies if the CDN may read this drive's payloads.
    ///
    /// Defaults to true to match the behaviour this flag replaced: before it existed a new drive
    /// was CDN-eligible unless someone set the blockcdn attribute on it. Callers that omit the
    /// field - which is every existing client - therefore get what they got before.
    /// </summary>
    public bool AllowCdn { get; set; } = true;

    public bool OwnerOnly { get; set; }

    public Dictionary<string, string> Attributes { get; set; }
}