using System;
using System.Collections.Generic;

namespace Odin.Services.Drives.DriveCore.Storage;

/// <summary>
/// Metadata about the file which is never sent over peer
/// </summary>
public class LocalAppMetadata
{
    public Guid VersionTag { get; set; }
        
    public string Content { get; init; }

    public List<Guid> Tags { get; init; }
}