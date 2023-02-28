using System.Collections.Generic;
using System.Threading.Tasks;
using Refit;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Hosting.Controllers;
using Youverse.Hosting.Controllers.OwnerToken;
using Youverse.Hosting.Controllers.OwnerToken.Circles;

namespace Youverse.Hosting.Tests.OwnerApi.Circle
{
    public interface ICircleNetworkConnectionsOwnerClient
    {
        private const string root_path = OwnerApiPathConstants.CirclesV1 + "/connections";

        [Post(root_path + "/circles/list")]
        Task<ApiResponse<IEnumerable<OdinId>>> GetCircleMembers([Body] GetCircleMembersRequest circleId);
        
        [Post(root_path + "/circles/add")]
        Task<ApiResponse<bool>> AddCircle([Body] AddCircleMembershipRequest request);
        
        [Post(root_path + "/circles/revoke")]
        Task<ApiResponse<bool>> RevokeCircle([Body] RevokeCircleMembershipRequest request);

        [Post(root_path + "/unblock")]
        Task<ApiResponse<bool>> Unblock([Body] OdinIdRequest request);

        [Post(root_path + "/block")]
        Task<ApiResponse<bool>> Block([Body] OdinIdRequest request);

        [Post(root_path + "/disconnect")]
        Task<ApiResponse<bool>> Disconnect([Body] OdinIdRequest request);

        [Post(root_path + "/status")]
        Task<ApiResponse<RedactedIdentityConnectionRegistration>> GetConnectionInfo([Body] OdinIdRequest request, bool omitContactData = true);

        [Post(root_path + "/connected")]
        Task<ApiResponse<PagedResult<RedactedIdentityConnectionRegistration>>> GetConnectedProfiles(int pageNumber, int pageSize, bool omitContactData = true);

        [Post(root_path + "/blocked")]
        Task<ApiResponse<PagedResult<RedactedIdentityConnectionRegistration>>> GetBlockedProfiles(int pageNumber, int pageSize, bool omitContactData = true);
    }
}