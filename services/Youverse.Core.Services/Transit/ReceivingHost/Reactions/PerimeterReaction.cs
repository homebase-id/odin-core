using Youverse.Core.Services.Drives;

namespace Youverse.Core.Services.Transit.ReceivingHost.Reactions;

public class PerimeterReaction
{
    public string OdinId { get; set; }

    public GlobalTransitIdFileIdentifier GlobalTransitIdFileIdentifier { get; set; }

    public UnixTimeUtcUnique Created { get; set; }

    public string ReactionContent { get; set; }
}