using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Hosting.UnifiedV2;
using Odin.Services.DataSubscription.Follower;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public interface IFollowerHttpClientApiV2
{
    private const string Root = UnifiedApiRouteConstants.Followers;

    [Post(Root + "/follow")]
    Task<ApiResponse<HttpContent>> Follow([Body] FollowRequest request);

    [Post(Root + "/unfollow")]
    Task<ApiResponse<HttpContent>> Unfollow([Body] UnfollowRequest request);

    [Get(Root + "/IdentitiesIFollow")]
    Task<ApiResponse<CursoredResult<string>>> GetIdentitiesIFollow(int max, string cursor);

    [Get(Root + "/followingme")]
    Task<ApiResponse<CursoredResult<string>>> GetFollowingMe(int max, string cursor);

    [Get(Root + "/IdentityIFollow")]
    Task<ApiResponse<FollowerDefinition>> GetIdentityIFollow(string odinId);

    [Get(Root + "/follower")]
    Task<ApiResponse<FollowerDefinition>> GetFollower(string odinId);

    [Post(Root + "/sync-feed-history")]
    Task<ApiResponse<HttpContent>> SynchronizeFeedHistory([Body] SynchronizeFeedHistoryRequest request);
}
