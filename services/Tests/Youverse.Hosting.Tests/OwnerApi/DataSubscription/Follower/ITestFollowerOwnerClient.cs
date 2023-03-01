using System.Net.Http;
using System.Threading.Tasks;
using Refit;
using Youverse.Core;
using Youverse.Core.Services.DataSubscription.Follower;
using Youverse.Hosting.Controllers.OwnerToken;

namespace Youverse.Hosting.Tests.OwnerApi.DataSubscription.Follower
{
    public interface ITestFollowerOwnerClient
    {
        private const string RootPath = OwnerApiPathConstants.FollowersV1;

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
    }
}