using System;
using System.Collections.Generic;

namespace Odin.Services.Drives.DriveCore.Storage;

/// <summary>
/// Metadata about the file which is never sent over peer
/// </summary>
public class LocalAppMetadata
{
    public Guid VersionTag { get; set; }

    /// <summary>
    /// Initialization vector used when the target file is encrypted.  Note the AesKey will be that of the file's KeyHeader
    /// </summary>
    public byte[] Iv { get; init; }

    public string Content { get; init; }

    public List<Guid> Tags { get; init; }
}