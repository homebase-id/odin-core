using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Authentication.Owner;
using Odin.Services.Configuration;
using Odin.Services.Configuration.Eula;
using Odin.Services.Drives;
using Odin.Services.Util;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Registry;

namespace Odin.Hosting.Controllers.OwnerToken.Configuration;

/// <summary>
/// Configuration for the owner's system
/// </summary>
[ApiController]
[AuthorizeValidOwnerToken]
[Route(OwnerApiPathConstants.ConfigurationV1)]
public class OwnerConfigurationController : OdinControllerBase
{
    private readonly TenantConfigService _tenantConfigService;
    private readonly IIdentityRegistry _registry;

    public const string InitialSetupEndpoint = "system/initialize";

    /// <summary />
    public OwnerConfigurationController(TenantConfigService tenantConfigService, IIdentityRegistry registry)
    {
        _tenantConfigService = tenantConfigService;
        _registry = registry;
    }

    /// <summary>
    /// Indicates if the identity has been completed first-run configuration
    /// </summary>
    /// <returns></returns>
    [HttpPost("system/isconfigured")]
    public async Task<bool> IsIdentityServerConfigured()
    {
        var result = await _tenantConfigService.IsIdentityServerConfiguredAsync();
        return result;
    }


    [HttpPost("system/IsEulaSignatureRequired")]
    public async Task<bool> IsEulaSignatureRequired()
    {
        var result = await _tenantConfigService.IsEulaSignatureRequiredAsync(WebOdinContext);
        return result;
    }

    [HttpPost("system/GetRequiredEulaVersion")]
    public Task<EulaVersionResponse> GetRequiredEulaVersion()
    {
        var result = _tenantConfigService.GetRequiredEulaVersion(WebOdinContext);
        return Task.FromResult(result);
    }

    [HttpPost("system/GetEulaSignatureHistory")]
    public async Task<List<EulaSignature>> GetEulaSignatureHistory()
    {
        var result = await _tenantConfigService.GetEulaSignatureHistoryAsync(WebOdinContext);
        return result;
    }

    [HttpPost("system/MarkEulaSigned")]
    public async Task<IActionResult> MarkEulaSigned([FromBody] MarkEulaSignedRequest request)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        await _tenantConfigService.MarkEulaSignedAsync(request, WebOdinContext);
        return Ok();
    }

    /// <summary>
    /// Ensures all new configuration is set up when a new tenant is configured.
    /// </summary>
    [HttpPost(InitialSetupEndpoint)]
    public async Task<bool> InitializeIdentity([FromBody] InitialSetupRequest request)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        await _tenantConfigService.EnsureInitialOwnerSetupAsync(request, WebOdinContext);
        return true;
    }

    [HttpPost("system/enable-auto-password-recovery")]
    public async Task EnableAutoPasswordRecovery()
    {
        await _tenantConfigService.EnableAutoPasswordRecovery(WebOdinContext);
    }

    /// <summary>
    /// Updates the specified flag
    /// </summary>
    /// <returns></returns>
    [HttpPost("system/updateflag")]
    public async Task<bool> UpdateSystemConfigFlag([FromBody] UpdateFlagRequest request)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        OdinValidationUtils.AssertNotNullOrEmpty(request.FlagName, nameof(request.FlagName));

        await _tenantConfigService.UpdateSystemFlagAsync(request, WebOdinContext);

        //todo: map to all the various flags
        return false;
    }

    /// <summary>
    /// Gets the system flags
    /// </summary>
    [HttpPost("system/flags")]
    public async Task<TenantSettings> GetTenantSettings()
    {
        var settings = await _tenantConfigService.GetTenantSettingsAsync();
        return settings;
    }

    /// <summary>
    /// Returns the built-in <see cref="TargetDrive"/>Info for the built-in drives
    /// </summary>
    /// <returns></returns>
    [HttpGet("system/driveinfo")]
    public Task<Dictionary<string, TargetDrive>> GetSystemDrives()
    {
        var d = new Dictionary<string, TargetDrive>()
        {
            { "contact", SystemDriveConstants.ContactDrive },
            { "profile", SystemDriveConstants.ProfileDrive },
            { "wallet", SystemDriveConstants.WalletDrive },
            { "chat", SystemDriveConstants.ChatDrive }
        };

        return Task.FromResult(d);
    }

    /// <summary>
    /// Updates a setting for use in the owner-app
    /// </summary>
    [HttpPost("ownerapp/settings/update")]
    public async Task<bool> UpdateOwnerAppSetting([FromBody] OwnerAppSettings settings)
    {
        OdinValidationUtils.AssertNotNull(settings?.Settings, nameof(settings.Settings));
        await _tenantConfigService.UpdateOwnerAppSettingsAsync(settings, WebOdinContext);
        return true;
    }

    /// <summary>
    /// Gets a map/dictionary of all settings specified by the owner-app
    /// </summary>
    [HttpPost("ownerapp/settings/list")]
    public async Task<OwnerAppSettings> GetOwnerSettings()
    {
        var settings = await _tenantConfigService.GetOwnerAppSettingsAsync(WebOdinContext);
        return settings;
    }

    [AllowAnonymous]
    [HttpGet("can-use-auto-recovery")]
    public IActionResult CanUseAutoRecovery([FromQuery(Name = "frt")] Guid firstRunToken)
    {
        _registry.AssetValidFirstRunToken(firstRunToken, WebOdinContext);
        _tenantConfigService.AssertCanUseAutoPasswordRecovery(WebOdinContext);
        return Ok();
    }
}