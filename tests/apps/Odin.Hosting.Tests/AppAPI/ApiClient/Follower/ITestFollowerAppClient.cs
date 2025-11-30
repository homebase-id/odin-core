using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Services.Authentication.Owner;
using Odin.Services.DataSubscription.Follower;
using Odin.Hosting.Controllers.ClientToken.App;
using Refit;

namespace Odin.Hosting.Tests.AppAPI.ApiClient.Follower
{
    public interface ITestFollowerAppClient
    {
        private const string RootPath = AppApiPathConstantsV1.FollowersV1;

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