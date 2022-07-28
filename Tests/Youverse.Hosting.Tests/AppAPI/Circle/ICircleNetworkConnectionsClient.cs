using System.Threading.Tasks;
using Refit;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Services.Contacts.Circle;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Hosting.Controllers;
using Youverse.Hosting.Controllers.ClientToken;

namespace Youverse.Hosting.Tests.AppAPI.Circle
{
    public interface ICircleNetworkConnectionsClient
    {
        private const string root_path = AppApiPathConstants.CirclesV1 + "/connections";

        [Post(root_path + "/unblock")]
        Task<ApiResponse<bool>> Unblock([Body]DotYouIdRequest request);

        [Post(root_path + "/block")]
        Task<ApiResponse<bool>> Block([Body]DotYouIdRequest request);

        [Post(root_path + "/disconnect")]
        Task<ApiResponse<bool>> Disconnect([Body]DotYouIdRequest request);

        [Post(root_path + "/status")]
        Task<ApiResponse<IdentityConnectionRegistration>> GetStatus([Body]DotYouIdRequest request);

        [Get(root_path + "/connected")]
        Task<ApiResponse<PagedResult<DotYouProfile>>> GetConnectedProfiles(int pageNumber, int pageSize);

        [Get(root_path + "/blocked")]
        Task<ApiResponse<PagedResult<DotYouProfile>>> GetBlockedProfiles(int pageNumber, int pageSize);
    }
}