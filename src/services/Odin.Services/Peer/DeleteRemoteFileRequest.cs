using Odin.Core.Storage;
using Odin.Services.Drives;

namespace Odin.Services.Peer;

public class DeleteRemoteFileRequest
{
    public GlobalTransitIdFileIdentifier RemoteGlobalTransitIdFileIdentifier { get; set; }
    public FileSystemType FileSystemType { get; set; }
}

public class MarkFileAsReadRequest
{
    public GlobalTransitIdFileIdentifier GlobalTransitIdFileIdentifier { get; set; }
    public FileSystemType FileSystemType { get; set; }
}