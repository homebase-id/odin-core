using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Refit;
using Youverse.Core.Services.Configuration;
using Youverse.Core.Services.Drive;
using Youverse.Hosting.Controllers.OwnerToken;

namespace Youverse.Hosting.Tests.OwnerApi.Configuration
{
    public interface IOwnerConfigurationClient
    {
        private const string RootEndpoint = OwnerApiPathConstants.ConfigurationV1;

        
        [Post(RootEndpoint + "/system/initialize")]
        Task<ApiResponse<bool>> InitializeIdentity([Body] InitialSetupRequest request);

        [Post(RootEndpoint + "/system/updateflag")]
        Task<ApiResponse<bool>> UpdateSystemConfigFlag([Body] UpdateFlagRequest request);

        [Get(RootEndpoint + "/system/driveinfo")]
        Task<ApiResponse<Dictionary<string, TargetDrive>>> GetSystemDrives();
        
        [Post(RootEndpoint + "/ownerapp/settings/update")]
        Task<ApiResponse<bool>> UpdateOwnerAppSetting([Body] OwnerAppSettings settings);
        
        [Post(RootEndpoint + "/ownerapp/settings/list")]
        Task<ApiResponse<OwnerAppSettings>> GetOwnerAppSettings();
    }
}