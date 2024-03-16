using Odin.Core.Identity;

namespace Odin.Services.Peer.Outgoing.Drive.Reactions;

public class PeerGetReactionsRequest
{
    public string OdinId { get; set; }

    public GetRemoteReactionsRequest Request { get; set; }
}

public class PeerAddReactionRequest
{
    public OdinId OdinId { get; set; }
    
    public AddRemoteReactionRequest Request { get; set; }
}