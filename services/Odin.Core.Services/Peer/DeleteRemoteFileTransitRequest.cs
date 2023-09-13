using Odin.Core.Services.Drives;
using Odin.Core.Storage;

namespace Odin.Core.Services.Peer;

public class DeleteRemoteFileTransitRequest
{
    public GlobalTransitIdFileIdentifier RemoteGlobalTransitIdFileIdentifier { get; set; }
    public FileSystemType FileSystemType { get; set; }
}