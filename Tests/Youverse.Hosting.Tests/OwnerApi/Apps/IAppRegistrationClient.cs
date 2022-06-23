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

        [Get(RootPath)]
        Task<ApiResponse<PagedResult<AppRegistrationResponse>>> GetRegisteredApps([Query]int pageNumber, [Query]int pageSize);

        [Get(RootPath+"/{appId}")]
        Task<ApiResponse<AppRegistrationResponse>> GetRegisteredApp(Guid appId);

        [Post(RootPath)]
        Task<ApiResponse<AppRegistrationResponse>> RegisterApp([Body] AppRegistrationRequest appRegistration);

        [Post(RootPath + "/revoke/{appId}")]
        Task<ApiResponse<NoResultResponse>> RevokeApp(Guid appId);
        
        [Post(RootPath+"/allow/{appId}")]
        Task<ApiResponse<NoResultResponse>> RemoveAppRevocation(Guid appId);

        [Post(RootPath + "/clients")]
        Task<ApiResponse<AppClientRegistrationResponse>> RegisterAppOnClient([Body] AppClientRegistrationRequest appClientRegistration);

        [Post(RootPath + "/clients/revoke")]
        Task<ApiResponse<NoResultResponse>> RevokeAppClient([Query]Guid appClientId);
        
        [Post(RootPath + "/clients/allow")]
        Task<ApiResponse<NoResultResponse>> RemoveAppDeviceRevocation([Query] Guid appId, [Query] string deviceId64);
    }
}