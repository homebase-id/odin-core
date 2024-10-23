using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Services.Configuration;
using Odin.Services.Configuration.Eula;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Owner.Configuration;

public class OwnerConfigurationApiClient(OwnerApiTestUtils ownerApi, TestIdentity identity)
{
    public async Task<ApiResponse<bool>> InitializeIdentity(InitialSetupRequest setupConfig)
    {
        var client = ownerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerConfiguration>(client, ownerSharedSecret);
            return await svc.InitializeIdentity(setupConfig);
        }
    }
    
    public async Task<ApiResponse<bool>> IsIdentityConfigured()
    {
        var client = ownerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerConfiguration>(client, ownerSharedSecret);
            return await svc.IsIdentityConfigured();
        }
    }
    
    public async Task<ApiResponse<HttpContent>> MarkEulaSigned(MarkEulaSignedRequest request)
    {
        var client = ownerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerConfiguration>(client, ownerSharedSecret);
            return await svc.MarkEulaSigned(request);
        }
    }
    
    public async Task<ApiResponse<bool>> IsEulaSignatureRequired()
    {
        var client = ownerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerConfiguration>(client, ownerSharedSecret);
            return await svc.IsEulaSignatureRequired();
        }
    }
    public async Task<ApiResponse<bool>> UpdateTenantSettingsFlag(TenantConfigFlagNames flag, string value)
    {
        var client = ownerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerConfiguration>(client, ownerSharedSecret);
            var updateFlagResponse = await svc.UpdateSystemConfigFlag(new UpdateFlagRequest()
            {
                FlagName = Enum.GetName(flag),
                Value = value
            });

            return updateFlagResponse;
        }
    }
    
    public async Task<ApiResponse<TenantSettings>> GetTenantSettings()
    {
        var client = ownerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerConfiguration>(client, ownerSharedSecret);
            return  await svc.GetTenantSettings();
        }
    }
    
    public async Task<ApiResponse<OwnerAppSettings>> GetOwnerAppSettings()
    {
        var client = ownerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerConfiguration>(client, ownerSharedSecret);
            return await svc.GetOwnerAppSettings();
        }
    }
    
    public async Task<ApiResponse<bool>> UpdateOwnerAppSetting(OwnerAppSettings ownerSettings)
    {
        
        var client = ownerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerConfiguration>(client, ownerSharedSecret);

            return await svc.UpdateOwnerAppSetting(ownerSettings);
        }
    }

    public async Task<ApiResponse<List<EulaSignature>>> GetEulaSignatureHistory()
    {
        var client = ownerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerConfiguration>(client, ownerSharedSecret);
            return await svc.GetEulaSignatureHistory();
        }
    }

    public async Task DisableAutoAcceptIntroductions(bool disabled)
    {
        var updateTenantSettingsFlagResponse =
            await this.UpdateTenantSettingsFlag(TenantConfigFlagNames.DisableAutoAcceptIntroductions, disabled.ToString());

        if (!updateTenantSettingsFlagResponse.IsSuccessStatusCode)
        {
            throw new Exception("test setup failed");
        }
    }
}