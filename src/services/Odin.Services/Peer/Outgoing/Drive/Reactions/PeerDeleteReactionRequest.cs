namespace Odin.Services.Peer.Outgoing.Drive.Reactions;

public class PeerDeleteReactionRequest
{
    public string OdinId { get; set; }

    public DeleteReactionRequestByGlobalTransitId Request { get; set; }
}