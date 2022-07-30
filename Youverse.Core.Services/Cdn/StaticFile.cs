using System.Collections.Generic;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Drive.Storage;

namespace Youverse.Core.Services.Cdn;

public class StaticFile
{
    public ClientFileHeader Header { get; set; }

    public IEnumerable<ThumbnailContent> AdditionalThumbnails { get; set; }

    /// <summary>
    /// Base64 encoded byte array of the payload
    /// </summary>
    public byte[] Payload { get; set; }
}