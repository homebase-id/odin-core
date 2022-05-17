using System;
using System.Threading.Tasks;
using Refit;

namespace Youverse.Core.Services.Contacts.Circle.Membership
{
    /// <summary>
    /// Handles getting profile data from the profile drive
    /// </summary>
    public interface ICircleNetworkProfileCacheClient
    {
        private const string RootPath = "/api/perimeter/notification";
        
        [Get("/api/youauth/v1/drive")]
        Task<ApiResponse<NoResultResponse>> GetProfile(Guid profileId);
    }
}