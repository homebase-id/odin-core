using System.Collections.Generic;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Drive.Core.Storage;

namespace Youverse.Core.Services.Optimization.Cdn;

public class StaticFile
{
    public SharedSecretEncryptedFileHeader Header { get; set; }

    public IEnumerable<ImageDataContent> AdditionalThumbnails { get; set; }

    /// <summary>
    /// Base64 encoded byte array of the payload
    /// </summary>
    public byte[] Payload { get; set; }
}