using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Configuration;
using Odin.Core.Services.Configuration.Eula;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Owner.Configuration
{
    public interface IRefitOwnerConfiguration
    {
        private const string RootEndpoint = OwnerApiPathConstants.ConfigurationV1;

        [Post(RootEndpoint + "/system/GetEulaSignatureHistory")]
        Task<ApiResponse<List<EulaSignature>>> GetEulaSignatureHistory();

        [Post(RootEndpoint + "/system/iseulasignaturerequired")]
        Task<ApiResponse<bool>> IsEulaSignatureRequired();

        [Post(RootEndpoint + "/system/MarkEulaSigned")]
        Task<ApiResponse<HttpContent>> MarkEulaSigned([Body] MarkEulaSignedRequest request);

        [Post(RootEndpoint + "/system/isconfigured")]
        Task<ApiResponse<bool>> IsIdentityConfigured();

        [Post(RootEndpoint + "/system/initialize")]
        Task<ApiResponse<bool>> InitializeIdentity([Body] InitialSetupRequest request);

        [Post(RootEndpoint + "/system/updateflag")]
        Task<ApiResponse<bool>> UpdateSystemConfigFlag([Body] UpdateFlagRequest request);

        [Post(RootEndpoint + "/system/flags")]
        Task<ApiResponse<TenantSettings>> GetTenantSettings();

        [Post(RootEndpoint + "/ownerapp/settings/update")]
        Task<ApiResponse<bool>> UpdateOwnerAppSetting([Body] OwnerAppSettings settings);

        [Post(RootEndpoint + "/ownerapp/settings/list")]
        Task<ApiResponse<OwnerAppSettings>> GetOwnerAppSettings();
    }
}