using Odin.Core.Identity;

namespace Odin.Services.Peer.Outgoing.Drive.Reactions;

public class PeerDeleteReactionRequest
{
    public OdinId OdinId { get; set; }

    public DeleteReactionRequestByGlobalTransitId Request { get; set; }
}