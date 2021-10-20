using System;
using System.Threading.Tasks;
using Refit;
using Youverse.Core;
using Youverse.Core.Services.Authorization.AppRegistration;
using Youverse.Core.Services.Contacts.Circle;
using Youverse.Hosting.Controllers.Owner;

namespace Youverse.Hosting.Tests.AppRegistration
{
    public interface IAppRegistrationTestHttpClient
    {
        private const string RootPath = "/api/admin/apps";

        [Get(RootPath)]
        Task<ApiResponse<NoResultResponse>> GetRegisteredApps([Query]int pageNumber, [Query]int pageSize);

        [Get(RootPath + "/{applicationId}")]
        Task<ApiResponse<NoResultResponse>> GetRegisteredApp(Guid applicationId);

        [Post(RootPath)]
        Task<ApiResponse<NoResultResponse>> RegisterApp([Body] AppRegistrationPayload appRegistration);

        [Delete(RootPath)]
        Task<ApiResponse<NoResultResponse>> RevokeApp([Query]Guid applicationId);

        [Post(RootPath)]
        Task<ApiResponse<NoResultResponse>> RegisterAppOnDevice([Body] AppDeviceRegistrationPayload appDeviceRegistration);

        [Get(RootPath + "/devices")]
        Task<ApiResponse<NoResultResponse>> GetRegisteredAppDevice([Query] Guid applicationId, [Query] string deviceId64);

        [Get(RootPath + "/devices")]
        Task<ApiResponse<NoResultResponse>> GetRegisteredAppDeviceList([Query] int pageNumber, [Query] int pageSize);

        [Delete(RootPath + "/devices}")]
        Task<ApiResponse<NoResultResponse>> RevokeDevice([Query] string deviceId64);

        [Delete(RootPath + "/devices")]
        Task<ApiResponse<NoResultResponse>> RevokeAppDevice([Query] Guid applicationId, [Query] string deviceId64);
    }
}