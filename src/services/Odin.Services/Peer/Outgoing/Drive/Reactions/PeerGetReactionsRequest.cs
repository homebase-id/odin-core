namespace Odin.Services.Peer.Outgoing.Drive.Reactions;

public class PeerGetReactionsRequest
{
    public string OdinId { get; set; }

    public GetRemoteReactionsRequest Request { get; set; }
}