using System;
using System.Threading.Tasks;
using Refit;
using Youverse.Core;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Hosting.Controllers.OwnerToken;
using Youverse.Hosting.Controllers.OwnerToken.AppManagement;

namespace Youverse.Hosting.Tests.OwnerApi.Apps
{
    public interface IAppRegistrationClient
    {
        private const string RootPath = OwnerApiPathConstants.AppManagementV1;

        [Get(RootPath + "/list")]
        Task<ApiResponse<PagedResult<AppRegistrationResponse>>> GetRegisteredApps([Query] int pageNumber, [Query] int pageSize);

        [Post(RootPath + "/app")]
        Task<ApiResponse<AppRegistrationResponse>> GetRegisteredApp([Body] GetAppRequest request);

        [Post(RootPath + "/register/app")]
        Task<ApiResponse<AppRegistrationResponse>> RegisterApp([Body] AppRegistrationRequest appRegistration);

        [Post(RootPath + "/revoke")]
        Task<ApiResponse<NoResultResponse>> RevokeApp([Body] GetAppRequest request);

        [Post(RootPath + "/allow")]
        Task<ApiResponse<NoResultResponse>> RemoveAppRevocation([Body] GetAppRequest request);

        [Post(RootPath + "/register/client")]
        Task<ApiResponse<AppClientRegistrationResponse>> RegisterAppOnClient([Body] AppClientRegistrationRequest appClientRegistration);
    }
}