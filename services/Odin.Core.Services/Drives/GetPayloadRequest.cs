using Youverse.Core.Services.Drives.FileSystem.Base;

namespace Youverse.Core.Services.Drives;

public class GetPayloadRequest
{
    public ExternalFileIdentifier File { get; set; }
    public FileChunk Chunk { get; set; }
}