using System.Threading.Tasks;
using Refit;
using Youverse.Core;
using Youverse.Hosting.Controllers.Owner;

namespace Youverse.Hosting.Tests.OwnerApi.Provisioning
{
    public interface IProvisioningClient
    {
        private const string RootPath = OwnerApiPathConstants.ProvisioningV1;

        [Post(RootPath + "/systemapps")]
        Task<ApiResponse<NoResultResponse>> EnsureSystemApps();
    }
}