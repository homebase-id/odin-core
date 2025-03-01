using Odin.Core.Time;
using Odin.Services.Drives;

namespace Odin.Services.Peer.Incoming.Reactions;

public class PerimeterReaction
{
    public string OdinId { get; set; }

    public GlobalTransitIdFileIdentifier GlobalTransitIdFileIdentifier { get; set; }

    public UnixTimeUtc Created { get; set; }

    public string ReactionContent { get; set; }
}