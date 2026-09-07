using System.Threading.Tasks;
using Odin.Core;
using Odin.Services.Membership.Connections;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Connections
{
    /// <summary>
    /// The connections list as a third party sees it, on the guest route.
    /// </summary>
    public interface IRefitGuestCircleNetworkConnections
    {
        // Relative to GuestApiClientFactory's base address, which already carries /api/guest/v1.
        [Get("/circles/connections/connected")]
        Task<ApiResponse<CursoredResult<RedactedIdentityConnectionRegistration>>> GetConnectedIdentities(int count, string cursor);
    }
}
