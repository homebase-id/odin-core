using System.Threading.Tasks;
using Refit;

namespace Youverse.Hosting.Tests.OwnerApi.Provisioning
{
    public interface IProvisioningClient
    {
        private const string RootPath = "/owner/api/v1/provisioning";

        [Post(RootPath)]
        Task<ApiResponse<bool>> ConfigureDefaults();
    }
}