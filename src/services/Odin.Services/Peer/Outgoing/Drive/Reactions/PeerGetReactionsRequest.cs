using Odin.Core.Identity;

namespace Odin.Services.Peer.Outgoing.Drive.Reactions;

public class PeerGetReactionsRequest
{
    public OdinId OdinId { get; set; }

    public GetRemoteReactionsRequest Request { get; set; }
}