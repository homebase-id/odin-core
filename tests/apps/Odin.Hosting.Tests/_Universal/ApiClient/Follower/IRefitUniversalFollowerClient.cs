using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Services.DataSubscription.Follower;
using Odin.Core.Storage.Database.Identity.Table;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Follower
{
    public interface IRefitUniversalFollowerClient
    {
        private const string RootPath = "/followers";

        [Get(RootPath + "/IdentitiesIFollow")]
        Task<ApiResponse<CursoredResult<string>>> GetIdentitiesIFollow(string cursor);

        [Get(RootPath + "/followingme")]
        Task<ApiResponse<CursoredResult<string>>> GetIdentitiesFollowingMe(string cursor);

        [Get(RootPath + "/follower")]
        Task<ApiResponse<FollowerDefinition>> GetFollower(string odinId);

        [Get(RootPath + "/IdentityIFollow")]
        Task<ApiResponse<FollowerDefinition>> GetIdentityIFollow(string odinId);

        [Post(RootPath + "/follow")]
        Task<ApiResponse<HttpContent>> Follow([Body] FollowRequest request);

        [Post(RootPath + "/unfollow")]
        Task<ApiResponse<HttpContent>> Unfollow([Body] UnfollowRequest request);
        
        [Post(RootPath + "/sync-feed-history")]
        Task<ApiResponse<HttpContent>> SynchronizeFeedHistory([Body] SynchronizeFeedHistoryRequest request);

        [Get(RootPath + "/my-subscriptions")]
        Task<ApiResponse<List<MySubscriptionsRecord>>> GetMySubscriptions();

        [Get(RootPath + "/my-subscribers")]
        Task<ApiResponse<List<MySubscribersRecord>>> GetMySubscribers();
    }
}