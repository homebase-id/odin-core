using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dawn;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Configuration;
using Odin.Core.Services.Drives;

namespace Odin.Hosting.Controllers.OwnerToken.Configuration;

/// <summary>
/// Configuration for the owner's system
/// </summary>
[ApiController]
[AuthorizeValidOwnerToken]
[Route(OwnerApiPathConstants.ConfigurationV1)]
public class ConfigurationController : Controller
{
    private readonly TenantConfigService _tenantConfigService;

    /// <summary />
    public ConfigurationController(TenantConfigService tenantConfigService)
    {
        _tenantConfigService = tenantConfigService;
    }

    /// <summary>
    /// Indicates if the identity has been completed first-run configuration
    /// </summary>
    /// <returns></returns>
    [HttpPost("system/isconfigured")]
    public Task<bool> IsIdentityServerConfigured()
    {
        var result = _tenantConfigService.IsIdentityServerConfigured();
        return Task.FromResult(result);
    }
    
    

    [HttpPost("system/iseulasignaturerequired")]
    public Task<bool> IsEulaSignatureRequired()
    {
        var result = _tenantConfigService.IsEulaSignatureRequired();
        return Task.FromResult(result);
    }
    
    [HttpPost("system/MarkEulaSigned")]
    public IActionResult MarkEulaSigned([FromBody] MarkEulaSignedRequest request)
    {
         _tenantConfigService.MarkEulaSigned(request);
         return Ok();
    }
    
    /// <summary>
    /// Ensures all new configuration is setup when a new tenant is configured.
    /// </summary>
    [HttpPost("system/initialize")]
    public async Task<bool> InitializeIdentity([FromBody] InitialSetupRequest request)
    {
        await _tenantConfigService.EnsureInitialOwnerSetup(request);
        return true;
    }

    /// <summary>
    /// Updates the specified flag
    /// </summary>
    /// <returns></returns>
    [HttpPost("system/updateflag")]
    public async Task<bool> UpdateSystemConfigFlag([FromBody] UpdateFlagRequest request)
    {
        Guard.Argument(request, nameof(request)).NotNull();
        Guard.Argument(request.FlagName, nameof(request.FlagName)).NotNull().NotEmpty();

        _tenantConfigService.UpdateSystemFlag(request);

        //todo: map to all the various flags
        return await Task.FromResult(false);
    }

    /// <summary>
    /// Gets the system flags
    /// </summary>
    [HttpPost("system/flags")]
    public TenantSettings GetTenantSettings()
    {
        var settings = _tenantConfigService.GetTenantSettings();
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
        _tenantConfigService.UpdateOwnerAppSettings(settings);
        return await Task.FromResult(true);
    }

    /// <summary>
    /// Gets a map/dictionary of all settings specified by the owner-app
    /// </summary>
    [HttpPost("ownerapp/settings/list")]
    public OwnerAppSettings GetOwnerSettings()
    {
        var settings = _tenantConfigService.GetOwnerAppSettings();
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