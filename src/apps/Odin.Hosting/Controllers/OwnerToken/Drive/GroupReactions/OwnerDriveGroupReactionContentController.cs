using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base.Drive.GroupReactions;
using Odin.Hosting.Controllers.OwnerToken;
using Odin.Services.Authentication.Owner;
using Odin.Services.Base;
using Odin.Services.Drives.Reactions.Redux.Group;

namespace Odin.Hosting.Controllers.ClientToken.App.Drive.GroupReactions;

/// <summary />
[ApiController]
[Route(OwnerApiPathConstants.DriveGroupReactionsV1)]
[ApiExplorerSettings(GroupName = "owner-v1")]
[AuthorizeValidOwnerToken]
public class OwnerDriveGroupReactionContentController : DriveGroupReactionControllerBase
{
    /// <summary />
    public OwnerDriveGroupReactionContentController(GroupReactionService groupReactionService) : base(
        groupReactionService)
    {
    }
}