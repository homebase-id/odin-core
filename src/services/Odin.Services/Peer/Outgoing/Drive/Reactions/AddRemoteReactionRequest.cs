using Odin.Services.Drives;

namespace Odin.Services.Peer.Outgoing.Drive.Reactions;

public class AddRemoteReactionRequest
{
    public GlobalTransitIdFileIdentifier File { get; set; }
    public string Reaction { get; set; }
}