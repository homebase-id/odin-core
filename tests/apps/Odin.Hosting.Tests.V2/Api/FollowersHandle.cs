#nullable enable
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Services.DataSubscription.Follower;
using Refit;

namespace Odin.Hosting.Tests.V2.Api;

/// <summary>
/// The <c>/api/v2/followers/*</c> surface. Every route here is served by
/// <c>V2FollowerController</c>, which subclasses the same <c>FollowerControllerBase</c> as the
/// owner/app/guest v1 controllers — so these tests cover the v2 routing and policy, not the
/// follow logic itself, which the v1 tests already exercise.
/// </summary>
public sealed class FollowersHandle
{
    private readonly V2FollowerClient _v2Followers;

    internal FollowersHandle(OwnerSession owner)
    {
        _v2Followers = new V2FollowerClient(owner.Identity, owner.Factory);
    }

    /// <summary>POST /api/v2/followers/follow</summary>
    public Task<ApiResponse<HttpContent>> FollowAsync(OdinId identity,
        FollowerNotificationType notificationType = FollowerNotificationType.AllNotifications,
        bool synchronizeFeedHistoryNow = false)
        => _v2Followers.FollowAsync(new FollowRequest
        {
            OdinId = identity,
            NotificationType = notificationType,
            Channels = [],
            SynchronizeFeedHistoryNow = synchronizeFeedHistoryNow
        });

    /// <summary>POST /api/v2/followers/unfollow</summary>
    public Task<ApiResponse<HttpContent>> UnfollowAsync(OdinId identity)
        => _v2Followers.UnfollowAsync(new UnfollowRequest { OdinId = identity });

    /// <summary>GET /api/v2/followers/IdentitiesIFollow</summary>
    public Task<ApiResponse<CursoredResult<string>>> GetIdentitiesIFollowAsync(int max = 100, string cursor = "")
        => _v2Followers.GetIdentitiesIFollowAsync(max, cursor);

    /// <summary>GET /api/v2/followers/followingme</summary>
    public Task<ApiResponse<CursoredResult<string>>> GetFollowingMeAsync(int max = 100, string cursor = "")
        => _v2Followers.GetFollowingMeAsync(max, cursor);

    /// <summary>GET /api/v2/followers/IdentityIFollow</summary>
    public Task<ApiResponse<FollowerDefinition>> GetIdentityIFollowAsync(OdinId identity)
        => _v2Followers.GetIdentityIFollowAsync(identity);

    /// <summary>GET /api/v2/followers/follower</summary>
    public Task<ApiResponse<FollowerDefinition>> GetFollowerAsync(OdinId identity)
        => _v2Followers.GetFollowerAsync(identity);

    /// <summary>POST /api/v2/followers/sync-feed-history</summary>
    public Task<ApiResponse<HttpContent>> SynchronizeFeedHistoryAsync(OdinId identity)
        => _v2Followers.SynchronizeFeedHistoryAsync(new SynchronizeFeedHistoryRequest { OdinId = identity });
}
