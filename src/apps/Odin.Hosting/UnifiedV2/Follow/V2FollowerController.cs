using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base.Follow;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.DataSubscription.Follower;

namespace Odin.Hosting.UnifiedV2.Follow;

/// <summary>
/// Follow/unfollow and follower-list operations on the unified v2 surface.
/// </summary>
/// <remarks>
/// Subclasses <see cref="FollowerControllerBase"/> for the same reason the owner, app and guest v1
/// controllers do: the actions and their route templates live on the base, so v2 stays in lockstep
/// with them instead of drifting behind a second copy.
/// </remarks>
[ApiController]
[Route(UnifiedApiRouteConstants.Followers)]
[UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2FollowerController(FollowerService fs) : FollowerControllerBase(fs)
{
}
