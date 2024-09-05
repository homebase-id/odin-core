using Odin.Core.Identity;

namespace Odin.Services.Peer.Outgoing.Drive.Reactions;

public class PeerAddReactionRequest
{
    public OdinId OdinId { get; set; }

    public AddRemoteReactionRequest Request { get; set; }
}