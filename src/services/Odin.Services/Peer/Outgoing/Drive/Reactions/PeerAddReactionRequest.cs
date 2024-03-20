namespace Odin.Services.Peer.Outgoing.Drive.Reactions;

public class PeerAddReactionRequest
{
    public string OdinId { get; set; }

    public AddRemoteReactionRequest Request { get; set; }
}