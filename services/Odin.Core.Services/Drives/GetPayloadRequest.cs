using Odin.Core.Services.Drives.FileSystem.Base;

namespace Odin.Core.Services.Drives;

public class GetPayloadRequest
{
    public ExternalFileIdentifier File { get; set; }
    public FileChunk Chunk { get; set; }
}