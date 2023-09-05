using Odin.Core.Services.Drives;
using Odin.Core.Time;

namespace Odin.Core.Services.Transit.ReceivingHost.Reactions;

public class PerimeterReaction
{
    public string OdinId { get; set; }

    public GlobalTransitIdFileIdentifier GlobalTransitIdFileIdentifier { get; set; }

    public UnixTimeUtcUnique Created { get; set; }

    public string ReactionContent { get; set; }
}