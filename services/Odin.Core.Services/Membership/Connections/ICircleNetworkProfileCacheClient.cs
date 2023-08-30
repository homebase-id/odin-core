using System;
using System.Threading.Tasks;
using Odin.Core.Fluff;
using Refit;

namespace Odin.Core.Services.Membership.Connections
{
    /// <summary>
    /// Handles getting profile data from the profile drive
    /// </summary>
    public interface ICircleNetworkProfileCacheClient
    {
        [Get("/api/youauth/v1/drive")]
        Task<ApiResponse<NoResultResponse>> GetProfile(Guid profileId);
    }
}