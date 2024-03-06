using System.Threading.Tasks;
using Odin.Services.Authentication.Owner;
using Odin.Hosting.Controllers.OwnerToken.YouAuth;
using Refit;

namespace Odin.Hosting.Controllers.Home.Auth
{
    public interface IHomePerimeterHttpClient
    {
        [Post(OwnerApiPathConstants.YouAuthV1Token)]
        Task<ApiResponse<YouAuthTokenResponse>> ExchangeCodeForToken(YouAuthTokenRequest tokenRequest);
    }
}