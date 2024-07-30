using System.Collections.Generic;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.Base.Drive.Reactions2;

public class AddReactionRequestRedux
{
    public FileIdentifier File { get; init; }

    public string Reaction { get; init; }

    public ReactionTransitOptions TransitOptions { get; init; }

}

public class ReactionTransitOptions
{
    private List<string> Recipients { get; set; }
}