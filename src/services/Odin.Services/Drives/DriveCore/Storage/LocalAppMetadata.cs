using System;
using System.Collections.Generic;

namespace Odin.Services.Drives.DriveCore.Storage;

/// <summary>
/// Metadata about the file which is never sent over transit
/// </summary>
public class LocalAppMetadata
{
    public Guid VersionTag { get; set; }
        
    public string Content { get; set; }

    public List<Guid> Tags { get; set; }
}