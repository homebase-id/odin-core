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
/// <remarks>
/// Each action names its policy: authorization policies combine, so one on the class would also bind the reads.
/// Writes are the owner's or an app's. The reads are open to a signed-in visitor (YouAuth) too, as on the v1
/// guest API, so the public site can use them: a visitor holds ReadWhoIFollow only when the owner's tenant
/// settings or a circle grant it, and ReadMyFollowers only from a circle.
/// </remarks>
[ApiController]
[Route(UnifiedApiRouteConstants.Followers)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2FollowerController(FollowerService followerService, FollowerSuggestionService suggestionService)
    : OdinControllerBase
{
    [HttpPost("follow")]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
    [SwaggerOperation(Tags = [SwaggerInfo.Followers], Summary = "Follow an identity, or update how it is followed")]
    public async Task<IActionResult> Follow([FromBody] FollowRequest request)
    {
        AssertIsValidOdinId(request.OdinId, out _);
        await followerService.FollowAsync(request, WebOdinContext);
        return Ok();
    }

    [HttpPost("unfollow")]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
    [SwaggerOperation(Tags = [SwaggerInfo.Followers], Summary = "Unfollow an identity")]
    public async Task<IActionResult> Unfollow([FromBody] OdinIdRequest request)
    {
        AssertIsValidOdinId(request.OdinId, out var id);
        await followerService.UnfollowAsync(id, WebOdinContext);
        return Ok();
    }

    [HttpPost("sync-feed-history")]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
    [SwaggerOperation(Tags = [SwaggerInfo.Followers], Summary = "Pull the feed history of an identity you follow")]
    public async Task<IActionResult> SynchronizeFeedHistory([FromBody] OdinIdRequest request)
    {
        AssertIsValidOdinId(request.OdinId, out var id);
        await followerService.SynchronizeChannelFilesAsync(id, WebOdinContext);
        return Ok();
    }

    [HttpGet("identities-i-follow")]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrAppOrGuest)]
    [SwaggerOperation(Tags = [SwaggerInfo.Followers], Summary = "Get list of identities you follow")]
    public async Task<CursoredResult<string>> GetIdentitiesIFollow(int count, string cursor)
    {
        return await followerService.GetIdentitiesIFollowAsync(count, cursor, WebOdinContext);
    }

    [HttpGet("identity-i-follow")]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrAppOrGuest)]
    [SwaggerOperation(Tags = [SwaggerInfo.Followers], Summary = "Get how you follow an identity")]
    public async Task<FollowerDefinition> GetIdentityIFollow([FromQuery] string odinId)
    {
        AssertIsValidOdinId(odinId, out var id);
        return await followerService.GetIdentityIFollowAsync(id, WebOdinContext);
    }

    [HttpGet("following-me")]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrAppOrGuest)]
    [SwaggerOperation(Tags = [SwaggerInfo.Followers], Summary = "Get list of identities following you")]
    public async Task<CursoredResult<string>> GetFollowingMe(int count, string cursor)
    {
        return await followerService.GetAllFollowersAsync(count, cursor, WebOdinContext);
    }

    [HttpGet("follower")]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrAppOrGuest)]
    [SwaggerOperation(Tags = [SwaggerInfo.Followers], Summary = "Get how an identity follows you")]
    public async Task<FollowerDefinition> GetFollower([FromQuery] string odinId)
    {
        AssertIsValidOdinId(odinId, out var id);
        return await followerService.GetFollowerAsync(id, WebOdinContext);
    }

    /// <summary>
    /// People you may know: what your connections share with you of their own connections and follows, ranked by how
    /// many of them lead to each person. See <see cref="FollowerSuggestionService"/>.
    /// </summary>
    [HttpGet("suggestions")]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
    [SwaggerOperation(Tags = [SwaggerInfo.Followers], Summary = "Get people you may know, from your connections' lists")]
    public async Task<FollowerSuggestionsResult> GetSuggestions(int count = 50, bool refresh = false)
    {
        return await suggestionService.GetSuggestionsAsync(count, refresh, WebOdinContext);
    }

    /// <summary>
    /// How the caller follows this identity, or nothing when they don't: the public site's Follow button. A caller
    /// may always read their own follow, so no permission is needed. For the owner or an app the caller is the
    /// identity itself, which follows no one as itself.
    /// </summary>
    [HttpGet("follower-configuration")]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrAppOrGuest)]
    [SwaggerOperation(Tags = [SwaggerInfo.Followers], Summary = "Get how the caller follows you")]
    public async Task<FollowerDefinition> GetFollowerConfiguration()
    {
        var caller = WebOdinContext.Caller.OdinId;
        return caller == null ? null : await followerService.GetFollowerAsync(caller.Value, WebOdinContext);
    }
}
