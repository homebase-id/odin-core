using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Authentication.Owner;
using Odin.Services.Configuration;
using Odin.Services.Configuration.Eula;
using Odin.Services.Drives;
using Odin.Services.Util;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.OwnerToken.Configuration;

/// <summary>
/// Configuration for the owner's system
/// </summary>
[ApiController]
[AuthorizeValidOwnerToken]
[Route(OwnerApiPathConstants.ConfigurationV1)]
public class ConfigurationController : OdinControllerBase
{
    private readonly TenantConfigService _tenantConfigService;
    private readonly TenantSystemStorage _tenantSystemStorage;

    /// <summary />
    public ConfigurationController(TenantConfigService tenantConfigService, TenantSystemStorage tenantSystemStorage)
    {
        _tenantConfigService = tenantConfigService;
        _tenantSystemStorage = tenantSystemStorage;
    }

    /// <summary>
    /// Indicates if the identity has been completed first-run configuration
    /// </summary>
    /// <returns></returns>
    [HttpPost("system/isconfigured")]
    public Task<bool> IsIdentityServerConfigured()
    {
        using var cn = _tenantSystemStorage.CreateConnection();
        var result = _tenantConfigService.IsIdentityServerConfigured(cn);
        return Task.FromResult(result);
    }


    [HttpPost("system/IsEulaSignatureRequired")]
    public Task<bool> IsEulaSignatureRequired()
    {
        using var cn = _tenantSystemStorage.CreateConnection();
        var result = _tenantConfigService.IsEulaSignatureRequired(WebOdinContext, cn);
        return Task.FromResult(result);
    }

    [HttpPost("system/GetRequiredEulaVersion")]
    public Task<EulaVersionResponse> GetRequiredEulaVersion()
    {
        var result = _tenantConfigService.GetRequiredEulaVersion(WebOdinContext);
        return Task.FromResult(result);
    }

    [HttpPost("system/GetEulaSignatureHistory")]
    public Task<List<EulaSignature>> GetEulaSignatureHistory()
    {
        using var cn = _tenantSystemStorage.CreateConnection();
        var result = _tenantConfigService.GetEulaSignatureHistory(WebOdinContext, cn);
        return Task.FromResult(result);
    }

    [HttpPost("system/MarkEulaSigned")]
    public IActionResult MarkEulaSigned([FromBody] MarkEulaSignedRequest request)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        using var cn = _tenantSystemStorage.CreateConnection();
        _tenantConfigService.MarkEulaSigned(request, WebOdinContext, cn);
        return Ok();
    }

    /// <summary>
    /// Ensures all new configuration is setup when a new tenant is configured.
    /// </summary>
    [HttpPost("system/initialize")]
    public async Task<bool> InitializeIdentity([FromBody] InitialSetupRequest request)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        // using var cn = _tenantSystemStorage.CreateConnection();
        await _tenantConfigService.EnsureInitialOwnerSetup(request, WebOdinContext, null);
        return true;
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

        using var cn = _tenantSystemStorage.CreateConnection();
        await _tenantConfigService.UpdateSystemFlag(request, WebOdinContext, cn);

        //todo: map to all the various flags
        return await Task.FromResult(false);
    }

    /// <summary>
    /// Gets the system flags
    /// </summary>
    [HttpPost("system/flags")]
    public TenantSettings GetTenantSettings()
    {
        using var cn = _tenantSystemStorage.CreateConnection();
        var settings = _tenantConfigService.GetTenantSettings(cn);
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
        using var cn = _tenantSystemStorage.CreateConnection();
        _tenantConfigService.UpdateOwnerAppSettings(settings, WebOdinContext, cn);
        return await Task.FromResult(true);
    }

    /// <summary>
    /// Gets a map/dictionary of all settings specified by the owner-app
    /// </summary>
    [HttpPost("ownerapp/settings/list")]
    public OwnerAppSettings GetOwnerSettings()
    {
        using var cn = _tenantSystemStorage.CreateConnection();
        var settings = _tenantConfigService.GetOwnerAppSettings(WebOdinContext, cn);
        return settings;
    }

    /// <summary>
    /// Marks registration for an identity complete
    /// </summary>
    /// <returns></returns>
    [HttpGet("registration/finalize")]
    public async Task<IActionResult> Finalize(Guid frid)
    {
        //TODO: how do i finalize from here with teh first run token?
        return await Task.FromResult(Ok());
    }
}