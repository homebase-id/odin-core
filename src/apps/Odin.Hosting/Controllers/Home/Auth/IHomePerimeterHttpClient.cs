using System.Threading;
using System.Threading.Tasks;
using Odin.Hosting.UnifiedV2;
using Odin.Hosting.Controllers.OwnerToken.YouAuth;
using Refit;

namespace Odin.Hosting.Controllers.Home.Auth
{
    public interface IHomePerimeterHttpClient
    {
        [Post(UnifiedApiRouteConstants.YouAuthToken)]
        Task<ApiResponse<YouAuthTokenResponse>> ExchangeCodeForToken(YouAuthTokenRequest tokenRequest, CancellationToken cancellationToken = default);
    }
}