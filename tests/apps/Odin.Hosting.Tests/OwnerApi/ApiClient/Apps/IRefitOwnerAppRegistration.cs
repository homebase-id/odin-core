using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Fluff;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.Apps;
using Odin.Hosting.Controllers.OwnerToken.AppManagement;
using Odin.Hosting.Controllers.OwnerToken.YouAuth;
using Refit;

namespace Odin.Hosting.Tests.OwnerApi.ApiClient.Apps
{
    public interface IRefitOwnerAppRegistration
    {
        private const string RootPath = OwnerApiPathConstants.AppManagementV1;

        [Get(RootPath + "/list")]
        Task<ApiResponse<PagedResult<RedactedAppRegistration>>> GetRegisteredApps([Query] int pageNumber, [Query] int pageSize);

        [Post(RootPath + "/app")]
        Task<ApiResponse<RedactedAppRegistration>> GetRegisteredApp([Body] GetAppRequest request);

        [Post(RootPath + "/register/app")]
        Task<ApiResponse<RedactedAppRegistration>> RegisterApp([Body] AppRegistrationRequest appRegistration);

        [Post(RootPath + "/revoke")]
        Task<ApiResponse<NoResultResponse>> RevokeApp([Body] GetAppRequest request);


        [Post(RootPath + "/deleteapp")]
        Task<ApiResponse<NoResultResponse>> DeleteApp([Body] GetAppRequest request);

        [Post(RootPath + "/allow")]
        Task<ApiResponse<NoResultResponse>> RemoveAppRevocation([Body] GetAppRequest request);

        [Post(RootPath + "/revokeClient")]
        Task<ApiResponse<HttpContent>> RevokeClient([Body] GetAppClientRequest accessRegistrationId);

        [Post(RootPath + "/deleteClient")]
        Task<ApiResponse<HttpContent>> DeleteClient([Body] GetAppClientRequest accessRegistrationId);

        [Post(RootPath + "/allowClient")]
        Task<ApiResponse<HttpContent>> AllowClient([Body] GetAppClientRequest accessRegistrationId);

        [Post(RootPath + "/clients")]
        Task<ApiResponse<List<RegisteredAppClientResponse>>> GetRegisteredClients([Body] GetAppRequest request);

        [Post(RootPath + "/register/client-ecc")]
        Task<ApiResponse<AppClientEccRegistrationResponse>> RegisterAppOnClientUsingEcc([Body] AppClientRegistrationRequest appClientRegistration);
        
        [Post(RootPath + "/register/client-ecc-exchange")]
        Task<ApiResponse<YouAuthTokenResponse>> ExchangeDigestForToken([Body] YouAuthTokenRequest request);
        
        [Post(RootPath + "/register/updateauthorizedcircles")]
        Task UpdateAuthorizedCircles([Body] UpdateAuthorizedCirclesRequest request);

        [Post(RootPath + "/register/updateapppermissions")]
        Task UpdateAppPermissions([Body] UpdateAppPermissionsRequest request);
    }
}