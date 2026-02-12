using Odin.Services.Base;
using Odin.Services.Drives.Reactions.Redux.Group;

namespace Odin.Hosting.Controllers.Base.Drive.GroupReactions;

public class AddReactionRequestRedux
{
    public FileIdentifier File { get; init; }

    public string Reaction { get; init; }

    public ReactionTransitOptions TransitOptions { get; init; }
}