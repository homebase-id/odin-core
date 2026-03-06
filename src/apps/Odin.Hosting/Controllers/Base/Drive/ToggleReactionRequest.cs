using Odin.Services.Drives.Reactions.Redux.Group;

namespace Odin.Hosting.Controllers.Base.Drive;

public class ToggleReactionRequest
{
    public string Reaction { get; set; }
    public ReactionTransitOptions TransitOptions { get; init; }
}
