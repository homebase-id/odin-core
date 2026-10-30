#nullable enable
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Hosting.Controllers;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Services.DataSubscription.Follower;
using Refit;

namespace Odin.Hosting.Tests.V2.Api;

/// <summary>
/// The <c>/api/v2/followers/*</c> surface, served by <c>V2FollowerController</c>.
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
        => _v2Followers.UnfollowAsync(new OdinIdRequest { OdinId = identity });

    /// <summary>GET /api/v2/followers/identities-i-follow</summary>
    public Task<ApiResponse<CursoredResult<FollowEntry>>> GetIdentitiesIFollowAsync(int count = 100, string cursor = "")
        => _v2Followers.GetIdentitiesIFollowAsync(count, cursor);

    /// <summary>GET /api/v2/followers/following-me</summary>
    public Task<ApiResponse<CursoredResult<FollowEntry>>> GetFollowingMeAsync(int count = 100, string cursor = "")
        => _v2Followers.GetFollowingMeAsync(count, cursor);

    /// <summary>GET /api/v2/followers/identity-i-follow</summary>
    public Task<ApiResponse<FollowerDefinition>> GetIdentityIFollowAsync(OdinId identity)
        => _v2Followers.GetIdentityIFollowAsync(identity);

    /// <summary>GET /api/v2/followers/follower</summary>
    public Task<ApiResponse<FollowerDefinition>> GetFollowerAsync(OdinId identity)
        => _v2Followers.GetFollowerAsync(identity);

    /// <summary>POST /api/v2/followers/sync-feed-history</summary>
    public Task<ApiResponse<HttpContent>> SynchronizeFeedHistoryAsync(OdinId identity)
        => _v2Followers.SynchronizeFeedHistoryAsync(new OdinIdRequest { OdinId = identity });
}
