using System.Threading.Tasks;
using Refit;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Services.Contacts.Circle;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Hosting.Controllers.ClientToken;

namespace Youverse.Hosting.Tests.AppAPI.Circle
{
    public interface ICircleNetworkConnectionsClient
    {
        private const string root_path = AppApiPathConstants.CirclesV1 + "/connections";

        [Get(root_path + "/unblock/{dotYouId}")]
        Task<ApiResponse<bool>> Unblock(string dotYouId);

        [Get(root_path + "/block/{dotYouId}")]
        Task<ApiResponse<bool>> Block(string dotYouId);

        [Get(root_path + "/disconnect/{dotYouId}")]
        Task<ApiResponse<bool>> Disconnect(string dotYouId);

        [Get(root_path + "/status/{dotYouId}")]
        Task<ApiResponse<IdentityConnectionRegistration>> GetStatus(string dotYouId);

        [Delete(root_path + "/{dotYouId}")]
        Task<ApiResponse<bool>> Delete(string dotYouId);

        [Get(root_path + "/connected")]
        Task<ApiResponse<PagedResult<DotYouProfile>>> GetConnectedProfiles(int pageNumber, int pageSize);

        [Get(root_path + "/blocked")]
        Task<ApiResponse<PagedResult<DotYouProfile>>> GetBlockedProfiles(int pageNumber, int pageSize);
    }
}