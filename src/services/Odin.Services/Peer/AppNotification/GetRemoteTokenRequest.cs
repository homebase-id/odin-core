using Odin.Core.Identity;

namespace Odin.Services.Peer.AppNotification;

public class GetRemoteTokenRequest
{
    public OdinId Identity { get; set; }
}