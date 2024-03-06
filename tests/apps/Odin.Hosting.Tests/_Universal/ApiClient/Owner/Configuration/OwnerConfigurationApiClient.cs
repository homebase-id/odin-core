using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Services.Configuration;
using Odin.Services.Configuration.Eula;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Owner.Configuration;

public class OwnerConfigurationApiClient
{
    private readonly TestIdentity _identity;
    private readonly OwnerApiTestUtils _ownerApi;

    public OwnerConfigurationApiClient(OwnerApiTestUtils ownerApi, TestIdentity identity)
    {
        _ownerApi = ownerApi;
        _identity = identity;
    }

    public async Task<ApiResponse<bool>> InitializeIdentity(InitialSetupRequest setupConfig)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerConfiguration>(client, ownerSharedSecret);
            return await svc.InitializeIdentity(setupConfig);
        }
    }
    
    public async Task<ApiResponse<bool>> IsIdentityConfigured()
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerConfiguration>(client, ownerSharedSecret);
            return await svc.IsIdentityConfigured();
        }
    }
    
    public async Task<ApiResponse<HttpContent>> MarkEulaSigned(MarkEulaSignedRequest request)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerConfiguration>(client, ownerSharedSecret);
            return await svc.MarkEulaSigned(request);
        }
    }
    
    public async Task<ApiResponse<bool>> IsEulaSignatureRequired()
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerConfiguration>(client, ownerSharedSecret);
            return await svc.IsEulaSignatureRequired();
        }
    }
    public async Task<ApiResponse<bool>> UpdateTenantSettingsFlag(TenantConfigFlagNames flag, string value)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
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
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerConfiguration>(client, ownerSharedSecret);
            return  await svc.GetTenantSettings();
        }
    }
    
    public async Task<ApiResponse<OwnerAppSettings>> GetOwnerAppSettings()
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerConfiguration>(client, ownerSharedSecret);
            return await svc.GetOwnerAppSettings();
        }
    }
    
    public async Task<ApiResponse<bool>> UpdateOwnerAppSetting(OwnerAppSettings ownerSettings)
    {
        
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerConfiguration>(client, ownerSharedSecret);

            return await svc.UpdateOwnerAppSetting(ownerSettings);
        }
    }

    public async Task<ApiResponse<List<EulaSignature>>> GetEulaSignatureHistory()
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerConfiguration>(client, ownerSharedSecret);
            return await svc.GetEulaSignatureHistory();
        }
    }
}