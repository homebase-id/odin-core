using System.Collections.Generic;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Drives.DriveCore.Storage;

namespace Odin.Core.Services.Optimization.Cdn;

public class StaticFile
{
    public SharedSecretEncryptedFileHeader Header { get; set; }

    public IEnumerable<ImageDataContent> AdditionalThumbnails { get; set; }

    /// <summary>
    /// Base64 encoded byte array of the payload
    /// </summary>
    public byte[] Payload { get; set; }
}