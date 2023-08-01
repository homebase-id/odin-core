using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Fluff;
using Odin.Core.Services.Authorization.Apps;
using Odin.Core.Services.Authorization.YouAuth;
using Odin.Hosting.Controllers.OwnerToken;
using Odin.Hosting.Controllers.OwnerToken.AppManagement;
using Odin.Hosting.Controllers.OwnerToken.YouAuthDomainManagement;
using Refit;

namespace Odin.Hosting.Tests.OwnerApi.YouAuthDomains
{
    public interface IYouAuthDomainRegistrationClient
    {
        private const string RootPath = OwnerApiPathConstants.YouAuthDomainManagementV1;

        
        [Get(RootPath + "/list")]
        Task<ApiResponse<PagedResult<RedactedYouAuthDomainRegistration>>> GetRegisteredDomains([Query] int pageNumber, [Query] int pageSize);
        
        [Post(RootPath + "/domain")]
        Task<ApiResponse<RedactedYouAuthDomainRegistration>> GetRegisteredDomain([Body] GetYouAuthDomainRequest request);

        [Post(RootPath + "/register/domain")]
        Task<ApiResponse<RedactedYouAuthDomainRegistration>> RegisterDomain([Body] YouAuthDomainRegistrationRequest registration);

        [Post(RootPath + "/register/updatepermissions")]
        Task<ApiResponse<HttpContent>> UpdatePermissions([Body] UpdateYouAuthDomainPermissionsRequest request);
        
        [Post(RootPath + "/revoke")]
        Task<ApiResponse<HttpContent>> RevokeDomain([Body] GetYouAuthDomainRequest request);
        
        [Post(RootPath + "/deletedomain")]
        Task<ApiResponse<HttpContent>> DeleteDomain([Body] GetYouAuthDomainRequest request);

        [Post(RootPath + "/allow")]
        Task<ApiResponse<HttpContent>> RemoveDomainRevocation([Body] GetYouAuthDomainRequest request);

        [Get(RootPath + "/clients")]
        Task<ApiResponse<List<RedactedYouAuthDomainClient>>> GetRegisteredClients();
        
        [Post(RootPath + "/revokeClient")]
        Task<ApiResponse<HttpContent>> RevokeClient([Body] GetYouAuthDomainClientRequest accessRegistrationId);

        [Post(RootPath + "/allowClient")]
        Task<ApiResponse<HttpContent>> AllowClient([Body] GetYouAuthDomainClientRequest accessRegistrationId);

        [Post(RootPath + "/deleteClient")]
        Task<ApiResponse<HttpContent>> DeleteClient([Body] GetYouAuthDomainClientRequest accessRegistrationId);

        [Post(RootPath + "/register/client")]
        Task<ApiResponse<AppClientRegistrationResponse>> RegisterAppOnClient([Body] YouAuthDomainClientRegistrationRequest appClientRegistration);
        
    }
}