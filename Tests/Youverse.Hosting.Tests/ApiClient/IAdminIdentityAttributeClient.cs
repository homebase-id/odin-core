using System.Threading.Tasks;
using Refit;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Identity.DataAttribute;
using Youverse.Core.Services.Profile;

namespace Youverse.Hosting.Tests.ApiClient
{
    public interface IAdminIdentityAttributeClient
    {
        private const string RootPath = "/api/admin/identity";
        
        [Post(RootPath + "/public")]
        Task<ApiResponse<NoResultResponse>> SavePublicProfile([Body]BasicProfileInfo profile);

        [Get(RootPath + "/public")]
        Task<ApiResponse<BasicProfileInfo>> GetPublicProfile();

        
        [Post(RootPath + "/connected")]
        Task<ApiResponse<NoResultResponse>> SaveConnectedProfile([Body]BasicProfileInfo profile);

        [Get(RootPath + "/connected")]
        Task<ApiResponse<BasicProfileInfo>> GetConnectedProfile();

        
    }
}