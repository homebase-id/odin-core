using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Hosting.Controllers;
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
    Task<ApiResponse<HttpContent>> Unfollow([Body] OdinIdRequest request);

    [Get(Root + "/identities-i-follow")]
    Task<ApiResponse<CursoredResult<string>>> GetIdentitiesIFollow(int count, string cursor);

    [Get(Root + "/following-me")]
    Task<ApiResponse<CursoredResult<string>>> GetFollowingMe(int count, string cursor);

    [Get(Root + "/identity-i-follow")]
    Task<ApiResponse<FollowerDefinition>> GetIdentityIFollow(string odinId);

    [Get(Root + "/follower")]
    Task<ApiResponse<FollowerDefinition>> GetFollower(string odinId);

    [Get(Root + "/suggestions")]
    Task<ApiResponse<FollowerSuggestionsResult>> GetSuggestions(int count, bool refresh);

    [Get(Root + "/follower-configuration")]
    Task<ApiResponse<FollowerDefinition>> GetFollowerConfiguration();

    [Post(Root + "/sync-feed-history")]
    Task<ApiResponse<HttpContent>> SynchronizeFeedHistory([Body] OdinIdRequest request);
}
