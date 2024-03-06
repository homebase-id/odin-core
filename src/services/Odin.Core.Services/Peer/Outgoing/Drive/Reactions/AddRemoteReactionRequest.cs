using Odin.Core.Services.Drives;

namespace Odin.Core.Services.Peer.Outgoing.Drive.Reactions;

public class AddRemoteReactionRequest
{
    public GlobalTransitIdFileIdentifier File { get; set; }
    public string Reaction { get; set; }
}