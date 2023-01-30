using System.Net.Http;
using System.Threading.Tasks;
using Refit;
using Youverse.Core;
using Youverse.Core.Services.Contacts.Follower;
using Youverse.Hosting.Controllers.OwnerToken;

namespace Youverse.Hosting.Tests.OwnerApi.Follower
{
    public interface ITestFollowerOwnerClient
    {
        private const string RootPath = OwnerApiPathConstants.FollowersV1;

        [Get(RootPath + "/IdentitiesIFollow")]
        Task<ApiResponse<CursoredResult<string>>> GetIdentitiesIFollow(string cursor);

        [Get(RootPath + "/followingme")]
        Task<ApiResponse<CursoredResult<string>>> GetIdentitiesFollowingMe(string cursor);
        
        [Get(RootPath + "/follower")]
        Task<ApiResponse<FollowerDefinition>> GetFollower(string dotYouId);
        
        [Get(RootPath + "/IdentityIFollow")]
        Task<ApiResponse<FollowerDefinition>> GetIdentityIFollow(string dotYouId);
        
        [Post(RootPath + "/follow")]
        Task<ApiResponse<HttpContent>> Follow([Body] FollowRequest request);

        [Post(RootPath + "/unfollow")]
        Task<ApiResponse<HttpContent>> Unfollow([Body] UnfollowRequest request);
    }
}