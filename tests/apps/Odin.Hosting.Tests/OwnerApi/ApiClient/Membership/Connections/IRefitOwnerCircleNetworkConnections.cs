using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Services.Authentication.Owner;
using Odin.Services.Membership.Connections;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.Base.Membership.Connections;
using Refit;

namespace Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.Connections
{
    public interface IRefitOwnerCircleNetworkConnections
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
        Task<ApiResponse<bool>> Disconnect([Body] OdinIdRequest request, [Query] bool notifyRemote = true);

        [Post(root_path + "/status")]
        Task<ApiResponse<RedactedIdentityConnectionRegistration>> GetConnectionInfo([Body] OdinIdRequest request, bool omitContactData = true);

        [Post(root_path + "/connected")]
        Task<ApiResponse<PagedResult<RedactedIdentityConnectionRegistration>>> GetConnectedProfiles(int pageNumber, int pageSize, bool omitContactData = true);

        [Post(root_path + "/blocked")]
        // count/cursor, matching CircleNetworkControllerBase.GetBlockedProfiles -- the pageNumber/pageSize
        // names this carried bound nothing, so count arrived as 0 and the endpoint answered 500.
        Task<ApiResponse<PagedResult<RedactedIdentityConnectionRegistration>>> GetBlockedProfiles(int count, string cursor, bool omitContactData = true);
    }
}