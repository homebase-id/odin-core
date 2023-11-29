using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Membership.YouAuth;
using Odin.Hosting.Controllers.OwnerToken.Membership.YouAuth;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Owner.YouAuth
{
    public interface IRefitYouAuthDomainRegistration
    {
        private const string RootPath = OwnerApiPathConstants.YouAuthDomainManagementV1;
        
        [Get(RootPath + "/list")]
        Task<ApiResponse<List<RedactedYouAuthDomainRegistration>>> GetRegisteredDomains();
        
        [Post(RootPath + "/domain")]
        Task<ApiResponse<RedactedYouAuthDomainRegistration>> GetRegisteredDomain([Body] GetYouAuthDomainRequest request);

        [Post(RootPath + "/register/domain")]
        Task<ApiResponse<RedactedYouAuthDomainRegistration>> RegisterDomain([Body] YouAuthDomainRegistrationRequest registration);
       
        [Post(RootPath + "/deletedomain")]
        Task<ApiResponse<HttpContent>> DeleteDomain([Body] GetYouAuthDomainRequest request);
        
        [Post(RootPath + "/circles/add")]
        Task<ApiResponse<bool>> GrantCircle([Body] GrantYouAuthDomainCircleRequest request);
        
        [Post(RootPath + "/circles/revoke")]
        Task<ApiResponse<bool>> RevokeCircle([Body] RevokeYouAuthDomainCircleRequest request);
        
        [Post(RootPath + "/revoke")]
        Task<ApiResponse<HttpContent>> RevokeDomain([Body] GetYouAuthDomainRequest request);
        
        [Post(RootPath + "/allow")]
        Task<ApiResponse<HttpContent>> RemoveDomainRevocation([Body] GetYouAuthDomainRequest request);

        [Get(RootPath + "/clients")]
        Task<ApiResponse<List<RedactedYouAuthDomainClient>>> GetRegisteredClients(string domain);
    
        [Post(RootPath + "/register/client")]
        Task<ApiResponse<YouAuthDomainClientRegistrationResponse>> RegisterClient([Body] YouAuthDomainClientRegistrationRequest clientRegistrationRequest);
        
        [Post(RootPath + "/deleteClient")]
        Task<ApiResponse<HttpContent>> DeleteClient([Body] GetYouAuthDomainClientRequest accessRegistrationId);
    }
}