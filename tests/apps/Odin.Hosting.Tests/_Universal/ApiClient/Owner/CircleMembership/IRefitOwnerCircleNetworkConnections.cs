using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Membership.Connections;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.Base.Membership.Connections;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Owner.CircleMembership
{
    public interface IRefitOwnerCircleNetworkConnections
    {
        private const string root_path = OwnerApiPathConstants.CirclesV1 + "/connections";

        [Post(root_path + "/circles/list")]
        Task<ApiResponse<IEnumerable<OdinId>>> GetCircleMembers([Body] GetCircleMembersRequest circleId);
        
        [Post(root_path + "/circles/add")]
        Task<ApiResponse<HttpContent>> AddCircle([Body] AddCircleMembershipRequest request);
        
        [Post(root_path + "/circles/revoke")]
        Task<ApiResponse<HttpContent>> RevokeCircle([Body] RevokeCircleMembershipRequest request);

        [Post(root_path + "/unblock")]
        Task<ApiResponse<HttpContent>> Unblock([Body] OdinIdRequest request);

        [Post(root_path + "/block")]
        Task<ApiResponse<HttpContent>> Block([Body] OdinIdRequest request);

        [Post(root_path + "/disconnect")]
        Task<ApiResponse<HttpContent>> Disconnect([Body] OdinIdRequest request);

        [Post(root_path + "/status")]
        Task<ApiResponse<RedactedIdentityConnectionRegistration>> GetConnectionInfo([Body] OdinIdRequest request, bool omitContactData = true);

        [Post(root_path + "/connected")]
        Task<ApiResponse<PagedResult<RedactedIdentityConnectionRegistration>>> GetConnectedProfiles(int pageNumber, int pageSize, bool omitContactData = true);

        [Post(root_path + "/blocked")]
        Task<ApiResponse<PagedResult<RedactedIdentityConnectionRegistration>>> GetBlockedProfiles(int pageNumber, int pageSize, bool omitContactData = true);
    }
}