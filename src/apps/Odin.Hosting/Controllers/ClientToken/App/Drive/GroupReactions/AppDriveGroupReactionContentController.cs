using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base.Drive.GroupReactions;
using Odin.Services.Base;
using Odin.Services.Drives.Reactions.Group;
using Odin.Services.Drives.Reactions.Redux.Group;

namespace Odin.Hosting.Controllers.ClientToken.App.Drive.GroupReactions;

/// <summary />
[ApiController]
[Route(AppApiPathConstantsV1.DriveGroupReactionsV1)]
[AuthorizeValidAppToken]
public class AppDriveGroupReactionContentController : DriveGroupReactionControllerBase
{
    /// <summary />
    public AppDriveGroupReactionContentController(GroupReactionService groupReactionService) : base(
        groupReactionService)
    {
    }
}