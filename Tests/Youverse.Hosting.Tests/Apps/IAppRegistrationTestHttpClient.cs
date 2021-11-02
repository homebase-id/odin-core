﻿using System;
using System.Threading.Tasks;
using Refit;
using Youverse.Core;
using Youverse.Core.Services.Contacts.Circle;
using Youverse.Hosting.Controllers.Owner;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Hosting.Controllers.Apps;

namespace Youverse.Hosting.Tests.Apps
{
    public interface IAppRegistrationTestHttpClient
    {
        private const string RootPath = "/api/admin/apps";

        [Get(RootPath)]
        Task<ApiResponse<PagedResult<AppRegistration>>> GetRegisteredApps([Query]int pageNumber, [Query]int pageSize);

        [Get(RootPath+"/{appId}")]
        Task<ApiResponse<AppRegistration>> GetRegisteredApp(Guid appId);

        [Post(RootPath)]
        Task<ApiResponse<Guid>> RegisterApp([Body] AppRegistrationPayload appRegistration);

        [Post(RootPath + "/revoke/{appId}")]
        Task<ApiResponse<NoResultResponse>> RevokeApp(Guid appId);
        
        [Post(RootPath+"/allow/{appId}")]
        Task<ApiResponse<NoResultResponse>> RemoveAppRevocation(Guid appId);

        [Post(RootPath + "/devices")]
        Task<ApiResponse<AppDeviceRegistrationReply>> RegisterAppOnDevice([Body] AppDeviceRegistrationPayload appDeviceRegistration);

        [Get(RootPath + "/devices/{appId}")]
        Task<ApiResponse<AppDeviceRegistration>> GetRegisteredAppDevice(Guid appId, [Query] string deviceId64);

        [Get(RootPath + "/devices")]
        Task<ApiResponse<PagedResult<AppDeviceRegistration>>> GetRegisteredAppDeviceList([Query] int pageNumber, [Query] int pageSize);

        [Post(RootPath + "/devices/revoke")]
        Task<ApiResponse<NoResultResponse>> RevokeAppDevice([Query] Guid appId, [Query] string deviceId64);
        
        [Post(RootPath + "/devices/allow")]
        Task<ApiResponse<NoResultResponse>> RemoveAppDeviceRevocation([Query] Guid appId, [Query] string deviceId64);
    }
}