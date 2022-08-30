using System.Threading.Tasks;
using Refit;
using Youverse.Core;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Hosting.Controllers;
using Youverse.Hosting.Controllers.ClientToken;
using Youverse.Hosting.Controllers.ClientToken.Circles;
using Youverse.Hosting.Controllers.OwnerToken;

namespace Youverse.Hosting.Tests.OwnerApi.Circle
{
    public interface ICircleNetworkConnectionsOwnerClient
    {
        private const string root_path = OwnerApiPathConstants.CirclesV1 + "/connections";

        [Post(root_path + "/unblock")]
        Task<ApiResponse<bool>> Unblock([Body] DotYouIdRequest request);

        [Post(root_path + "/block")]
        Task<ApiResponse<bool>> Block([Body] DotYouIdRequest request);

        [Post(root_path + "/disconnect")]
        Task<ApiResponse<bool>> Disconnect([Body] DotYouIdRequest request);

        [Post(root_path + "/status")]
        Task<ApiResponse<RedactedIdentityConnectionRegistration>> GetConnectionInfo([Body] DotYouIdRequest request);

        [Post(root_path + "/connected")]
        Task<ApiResponse<PagedResult<DotYouProfile>>> GetConnectedProfiles(int pageNumber, int pageSize);

        [Post(root_path + "/blocked")]
        Task<ApiResponse<PagedResult<DotYouProfile>>> GetBlockedProfiles(int pageNumber, int pageSize);
    }
}