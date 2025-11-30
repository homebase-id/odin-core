using System.Net.Http;
using System.Threading.Tasks;
using Odin.Hosting.UnifiedV2;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public interface IAuthHttpClientApiV2
{
    private const string Endpoint = UnifiedApiRouteConstants.Auth;

    [Get(Endpoint + "/verify-token")]
    Task<ApiResponse<HttpContent>> VerifyToken();
    
    [Get(Endpoint + "/verify-shared-secret-encryption")]
    Task<ApiResponse<string>> VerifySharedSecretEncryption(string checkValue64);
    
    [Post(Endpoint + "/logout")]
    Task<ApiResponse<string>> Logout();

}