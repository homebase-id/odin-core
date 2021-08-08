using System.Threading.Tasks;
using DotYou.Types.DataAttribute;
using Refit;

namespace DotYou.Types.ApiClient
{
    public interface IAdminIdentityAttributeClient
    {
        private const string RootPath = "/api/admin/identity";

        [Get(RootPath + "/primary")]
        Task<ApiResponse<NameAttribute>> GetPrimaryName();

        [Post(RootPath + "/primary")]
        Task<ApiResponse<NoResultResponse>> SavePrimaryName([Body]NameAttribute name);

        [Get(RootPath + "/primary/avatar")]
        Task<ApiResponse<AvatarUri>> GetPrimaryAvatarUri();
    }
}