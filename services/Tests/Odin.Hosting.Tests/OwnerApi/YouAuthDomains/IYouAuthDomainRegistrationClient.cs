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

        [Post(RootPath + "/domain")]
        Task<ApiResponse<RedactedYouAuthDomainRegistration>> GetRegisteredDomain([Body] GetYouAuthDomainRequest request);

        [Post(RootPath + "/register/domain")]
        Task<ApiResponse<RedactedYouAuthDomainRegistration>> RegisterDomain([Body] YouAuthDomainRegistrationRequest registration);


    }
}