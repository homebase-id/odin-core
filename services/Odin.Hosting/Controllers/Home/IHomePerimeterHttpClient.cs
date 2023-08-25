using System.Threading.Tasks;
using Odin.Core.Services.Authentication.Owner;
using Odin.Hosting.Controllers.OwnerToken.YouAuth;
using Refit;

namespace Odin.Hosting.Controllers.Home
{
    public interface IHomePerimeterHttpClient
    {
        [Get(OwnerApiPathConstants.YouAuthV1Token)]
        Task<ApiResponse<YouAuthTokenResponse>> ExchangeCodeForToken(YouAuthTokenRequest tokenRequest);
    }
}