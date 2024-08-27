using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.Base.Membership.Connections;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;
using Refit;

namespace Odin.Hosting.Tests.AppAPI.ApiClient.Membership.Connections.t
{
    public interface IRefitUniversalOwnerCircleNetworkConnections
    {
        private const string RootPath  = "/circles/connections";

        [Post(RootPath + "/circles/list")]
        Task<ApiResponse<IEnumerable<OdinId>>> GetCircleMembers([Body] GetCircleMembersRequest circleId);
        
        [Post(RootPath + "/circles/add")]
        Task<ApiResponse<bool>> AddCircle([Body] AddCircleMembershipRequest request);
        
        [Post(RootPath + "/circles/revoke")]
        Task<ApiResponse<bool>> RevokeCircle([Body] RevokeCircleMembershipRequest request);

        [Post(RootPath + "/unblock")]
        Task<ApiResponse<bool>> Unblock([Body] OdinIdRequest request);

        [Post(RootPath + "/block")]
        Task<ApiResponse<bool>> Block([Body] OdinIdRequest request);

        [Post(RootPath + "/disconnect")]
        Task<ApiResponse<bool>> Disconnect([Body] OdinIdRequest request);

        [Post(RootPath + "/status")]
        Task<ApiResponse<RedactedIdentityConnectionRegistration>> GetConnectionInfo([Body] OdinIdRequest request, bool omitContactData = true);

        [Post(RootPath + "/connected")]
        Task<ApiResponse<PagedResult<RedactedIdentityConnectionRegistration>>> GetConnectedProfiles(int pageNumber, int pageSize, bool omitContactData = true);

        [Post(RootPath + "/blocked")]
        Task<ApiResponse<PagedResult<RedactedIdentityConnectionRegistration>>> GetBlockedProfiles(int pageNumber, int pageSize, bool omitContactData = true);
        
        [Post(RootPath + "/verify-connection")]
        Task<ApiResponse<IcrVerificationResult>> VerifyConnection([Body] OdinIdRequest request);

    }
}