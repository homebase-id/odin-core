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
    public bool OwnerOnly { get; set; }

    public Dictionary<string, string> Attributes { get; set; }
    public Guid Type => this.TargetDrive.Type;
}