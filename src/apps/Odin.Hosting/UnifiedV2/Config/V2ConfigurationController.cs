using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.OwnerToken.Configuration;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Configuration;
using Odin.Services.Configuration.Eula;
using Odin.Services.Util;

namespace Odin.Hosting.UnifiedV2.Config;

/// <summary>
/// The V2 twin of V1's <c>config</c> (<see cref="OwnerConfigurationController"/>), with its request and response
/// shapes. Owner console only, as V1 is. The setup wizard is the owner signed in, so it comes here too.
/// </summary>
[ApiController]
[Route(UnifiedApiRouteConstants.Configuration)]
[UnifiedV2Authorize(UnifiedPolicies.Owner)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2ConfigurationController(TenantConfigService tenantConfigService) : OdinControllerBase
{
    [HttpPost("system/is-configured")]
    public async Task<bool> IsIdentityServerConfigured()
    {
        return await tenantConfigService.IsIdentityServerConfiguredAsync();
    }

    /// <summary>
    /// The route keeps <see cref="OwnerConfigurationController.InitialSetupEndpoint"/> in its path: that is what
    /// VersionUpgradeMiddleware lets through during an upgrade, on either API.
    /// </summary>
    [HttpPost(OwnerConfigurationController.InitialSetupEndpoint)]
    public async Task<bool> InitializeIdentity([FromBody] InitialSetupRequest request)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        await tenantConfigService.EnsureInitialOwnerSetupAsync(request, WebOdinContext);
        return true;
    }

    [HttpPost("system/is-eula-signature-required")]
    public async Task<bool> IsEulaSignatureRequired()
    {
        return await tenantConfigService.IsEulaSignatureRequiredAsync(WebOdinContext);
    }

    [HttpPost("system/required-eula-version")]
    public EulaVersionResponse GetRequiredEulaVersion()
    {
        return tenantConfigService.GetRequiredEulaVersion(WebOdinContext);
    }

    [HttpPost("system/mark-eula-signed")]
    public async Task<IActionResult> MarkEulaSigned([FromBody] MarkEulaSignedRequest request)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        await tenantConfigService.MarkEulaSignedAsync(request, WebOdinContext);
        return Ok();
    }

    /// <remarks>Answers false, as V1 does; the client ignores it.</remarks>
    [HttpPost("system/update-flag")]
    public async Task<bool> UpdateSystemConfigFlag([FromBody] UpdateFlagRequest request)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        OdinValidationUtils.AssertNotNullOrEmpty(request.FlagName, nameof(request.FlagName));

        await tenantConfigService.UpdateSystemFlagAsync(request, WebOdinContext);
        return false;
    }

    /// <remarks>The service takes no context and checks nothing, so it is checked here too.</remarks>
    [HttpPost("system/flags")]
    public async Task<TenantSettings> GetTenantSettings()
    {
        WebOdinContext.Caller.AssertHasMasterKey();
        return await tenantConfigService.GetTenantSettingsAsync();
    }

    [HttpPost("owner-app/settings/update")]
    public async Task<bool> UpdateOwnerAppSetting([FromBody] OwnerAppSettings settings)
    {
        OdinValidationUtils.AssertNotNull(settings?.Settings, nameof(settings.Settings));
        await tenantConfigService.UpdateOwnerAppSettingsAsync(settings, WebOdinContext);
        return true;
    }

    [HttpPost("owner-app/settings/list")]
    public async Task<OwnerAppSettings> GetOwnerSettings()
    {
        return await tenantConfigService.GetOwnerAppSettingsAsync(WebOdinContext);
    }
}
