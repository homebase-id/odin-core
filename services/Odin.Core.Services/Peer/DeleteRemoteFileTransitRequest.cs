using Odin.Core.Services.Drives;
using Odin.Core.Storage;

namespace Odin.Core.Services.Transit;

public class DeleteRemoteFileTransitRequest
{
    public GlobalTransitIdFileIdentifier RemoteGlobalTransitIdFileIdentifier { get; set; }
    public FileSystemType FileSystemType { get; set; }
}