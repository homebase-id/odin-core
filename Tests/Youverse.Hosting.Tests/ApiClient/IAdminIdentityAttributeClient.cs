using System.Threading.Tasks;
using Refit;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Identity.DataAttribute;

namespace Youverse.Hosting.Tests.ApiClient
{
    public interface IAdminIdentityAttributeClient
    {
        private const string RootPath = "/api/admin/identity";
        

        [Get(RootPath + "/primary/avatar")]
        Task<ApiResponse<AvatarUri>> GetPrimaryAvatarUri();
    }
}