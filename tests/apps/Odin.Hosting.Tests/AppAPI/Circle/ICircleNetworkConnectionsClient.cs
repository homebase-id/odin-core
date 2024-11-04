using System;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Services.Membership.Connections;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.ClientToken;
using Odin.Hosting.Controllers.ClientToken.App;
using Refit;

namespace Odin.Hosting.Tests.AppAPI.Circle
{
    [Obsolete]
    public interface ICircleNetworkConnectionsClient
    {
        private const string root_path = AppApiPathConstants.CirclesV1 + "/connections";

        [Post(root_path + "/unblock")]
        Task<ApiResponse<bool>> Unblock([Body]OdinIdRequest request);

        [Post(root_path + "/block")]
        Task<ApiResponse<bool>> Block([Body]OdinIdRequest request);

        [Post(root_path + "/disconnect")]
        Task<ApiResponse<bool>> Disconnect([Body]OdinIdRequest request);

        [Get(root_path + "/connected")]
        Task<ApiResponse<PagedResult<RedactedIdentityConnectionRegistration>>> GetConnectedProfiles(int pageNumber, int pageSize);

        [Get(root_path + "/blocked")]
        Task<ApiResponse<PagedResult<RedactedIdentityConnectionRegistration>>> GetBlockedProfiles(int pageNumber, int pageSize);
    }
}