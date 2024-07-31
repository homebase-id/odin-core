using Odin.Services.Base;
using Odin.Services.Drives.Reactions.Group;

namespace Odin.Hosting.Controllers.Base.Drive.ReactionsRedux;

public class DeleteReactionRequestRedux
{
    public FileIdentifier File { get; init; }

    public string Reaction { get; init; }

    public ReactionTransitOptions TransitOptions { get; init; }
}