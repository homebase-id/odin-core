using Odin.Core.Services.Drives;

namespace Odin.Core.Services.Peer.Outgoing.Drive.Reactions;

public class DeleteReactionRequestByGlobalTransitId
{
    public string Reaction { get; set; }

    public GlobalTransitIdFileIdentifier File { get; set; }
}