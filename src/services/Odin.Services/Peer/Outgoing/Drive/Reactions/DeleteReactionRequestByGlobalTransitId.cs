using Odin.Services.Drives;

namespace Odin.Services.Peer.Outgoing.Drive.Reactions;

public class DeleteReactionRequestByGlobalTransitId
{
    public string Reaction { get; set; }

    public GlobalTransitIdFileIdentifier File { get; set; }
}