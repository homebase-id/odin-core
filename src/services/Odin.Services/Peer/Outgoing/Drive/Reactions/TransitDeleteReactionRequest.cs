namespace Odin.Services.Peer.Outgoing.Drive.Reactions;

public class TransitDeleteReactionRequest
{
    public string OdinId { get; set; }

    public DeleteReactionRequestByGlobalTransitId Request { get; set; }
}