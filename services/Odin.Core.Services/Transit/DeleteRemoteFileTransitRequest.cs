using Youverse.Core.Services.Drives;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Transit;

public class DeleteRemoteFileTransitRequest
{
    public GlobalTransitIdFileIdentifier RemoteGlobalTransitIdFileIdentifier { get; set; }
    public FileSystemType FileSystemType { get; set; }
}