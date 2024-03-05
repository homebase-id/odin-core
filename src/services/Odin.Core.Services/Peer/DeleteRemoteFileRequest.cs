using Odin.Core.Services.Drives;
using Odin.Core.Storage;

namespace Odin.Core.Services.Peer;

public class DeleteRemoteFileRequest
{
    public GlobalTransitIdFileIdentifier RemoteGlobalTransitIdFileIdentifier { get; set; }
    public FileSystemType FileSystemType { get; set; }
}