using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Services.DataSubscription.Follower;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public class V2FollowerClient(OdinId identity, IApiClientFactory factory)
{
    private IFollowerHttpClientApiV2 Service()
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        return RefitCreator.RestServiceFor<IFollowerHttpClientApiV2>(client, sharedSecret);
    }

    public async Task<ApiResponse<HttpContent>> FollowAsync(FollowRequest request)
        => await Service().Follow(request);

    public async Task<ApiResponse<HttpContent>> UnfollowAsync(UnfollowRequest request)
        => await Service().Unfollow(request);

    public async Task<ApiResponse<CursoredResult<string>>> GetIdentitiesIFollowAsync(int max = 100, string cursor = "")
        => await Service().GetIdentitiesIFollow(max, cursor);

    public async Task<ApiResponse<CursoredResult<string>>> GetFollowingMeAsync(int max = 100, string cursor = "")
        => await Service().GetFollowingMe(max, cursor);

    public async Task<ApiResponse<FollowerDefinition>> GetIdentityIFollowAsync(OdinId odinId)
        => await Service().GetIdentityIFollow(odinId.DomainName);

    public async Task<ApiResponse<FollowerDefinition>> GetFollowerAsync(OdinId odinId)
        => await Service().GetFollower(odinId.DomainName);

    public async Task<ApiResponse<HttpContent>> SynchronizeFeedHistoryAsync(SynchronizeFeedHistoryRequest request)
        => await Service().SynchronizeFeedHistory(request);
}
