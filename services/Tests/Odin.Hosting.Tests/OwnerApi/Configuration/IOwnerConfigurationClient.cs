using System.Threading.Tasks;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Configuration;
using Odin.Hosting.Controllers.OwnerToken;
using Refit;

namespace Odin.Hosting.Tests.OwnerApi.Configuration
{
    public interface IOwnerConfigurationClient
    {
        private const string RootEndpoint = OwnerApiPathConstants.ConfigurationV1;


        [Post(RootEndpoint + "/system/isconfigured")]
        Task<ApiResponse<bool>> IsIdentityConfigured();

        [Post(RootEndpoint + "/system/initialize")]
        Task<ApiResponse<bool>> InitializeIdentity([Body] InitialSetupRequest request);

        [Post(RootEndpoint + "/system/updateflag")]
        Task<ApiResponse<bool>> UpdateSystemConfigFlag([Body] UpdateFlagRequest request);
        
        [Post(RootEndpoint + "/ownerapp/settings/update")]
        Task<ApiResponse<bool>> UpdateOwnerAppSetting([Body] OwnerAppSettings settings);

        [Post(RootEndpoint + "/ownerapp/settings/list")]
        Task<ApiResponse<OwnerAppSettings>> GetOwnerAppSettings();
    }
}