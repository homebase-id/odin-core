using System.Collections.Generic;
using Odin.Core.Identity;

namespace Odin.Services.Drives.Reactions.Redux.Group;

public class ReactionTransitOptions
{
    public List<OdinId> Recipients { get; init; }
}