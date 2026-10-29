using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.DataSubscription.Follower;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Follow;

/// <summary>
/// Who the identity follows and who follows it (#1611). <see cref="FollowerService"/> asserts the permissions:
/// ManageFeed to follow, unfollow and sync; ReadWhoIFollow and ReadMyFollowers to read.
/// </summary>
[ApiController]
[Route(UnifiedApiRouteConstants.Followers)]
[UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2FollowerController(FollowerService followerService) : OdinControllerBase
{
    [HttpPost("follow")]
    [SwaggerOperation(Tags = [SwaggerInfo.Followers], Summary = "Follow an identity, or update how it is followed")]
    public async Task<IActionResult> Follow([FromBody] FollowRequest request)
    {
        AssertIsValidOdinId(request.OdinId, out _);
        await followerService.FollowAsync(request, WebOdinContext);
        return Ok();
    }

    [HttpPost("unfollow")]
    [SwaggerOperation(Tags = [SwaggerInfo.Followers], Summary = "Unfollow an identity")]
    public async Task<IActionResult> Unfollow([FromBody] OdinIdRequest request)
    {
        AssertIsValidOdinId(request.OdinId, out var id);
        await followerService.UnfollowAsync(id, WebOdinContext);
        return Ok();
    }

    [HttpPost("sync-feed-history")]
    [SwaggerOperation(Tags = [SwaggerInfo.Followers], Summary = "Pull the feed history of an identity you follow")]
    public async Task<IActionResult> SynchronizeFeedHistory([FromBody] OdinIdRequest request)
    {
        AssertIsValidOdinId(request.OdinId, out var id);
        await followerService.SynchronizeChannelFilesAsync(id, WebOdinContext);
        return Ok();
    }

    [HttpGet("identities-i-follow")]
    [SwaggerOperation(Tags = [SwaggerInfo.Followers], Summary = "Get list of identities you follow")]
    public async Task<CursoredResult<string>> GetIdentitiesIFollow(int count, string cursor)
    {
        return await followerService.GetIdentitiesIFollowAsync(count, cursor, WebOdinContext);
    }

    [HttpGet("identity-i-follow")]
    [SwaggerOperation(Tags = [SwaggerInfo.Followers], Summary = "Get how you follow an identity")]
    public async Task<FollowerDefinition> GetIdentityIFollow([FromQuery] string odinId)
    {
        AssertIsValidOdinId(odinId, out var id);
        return await followerService.GetIdentityIFollowAsync(id, WebOdinContext);
    }

    [HttpGet("following-me")]
    [SwaggerOperation(Tags = [SwaggerInfo.Followers], Summary = "Get list of identities following you")]
    public async Task<CursoredResult<string>> GetFollowingMe(int count, string cursor)
    {
        return await followerService.GetAllFollowersAsync(count, cursor, WebOdinContext);
    }

    [HttpGet("follower")]
    [SwaggerOperation(Tags = [SwaggerInfo.Followers], Summary = "Get how an identity follows you")]
    public async Task<FollowerDefinition> GetFollower([FromQuery] string odinId)
    {
        AssertIsValidOdinId(odinId, out var id);
        return await followerService.GetFollowerAsync(id, WebOdinContext);
    }
}
