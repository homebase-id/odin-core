using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dawn;
using Microsoft.AspNetCore.Mvc;
using Refit;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Configuration;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Registry;
using Youverse.Core.Services.Registry.Registration;

namespace Youverse.Hosting.Controllers.OwnerToken.Configuration;

/// <summary>
/// Configuration for the owner's system
/// </summary>
[ApiController]
[AuthorizeValidOwnerToken]
[Route(OwnerApiPathConstants.ConfigurationV1)]
public class ConfigurationController : Controller
{
    private readonly IIdentityRegistrationService _regService;
    private readonly TenantConfigService _tenantConfigService;
    private readonly IDriveService _driveService;

    /// <summary />
    public ConfigurationController(TenantConfigService tenantConfigService, IDriveService driveService, IIdentityRegistrationService regService)
    {
        _tenantConfigService = tenantConfigService;
        _driveService = driveService;
        _regService = regService;
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
        return false;
    }

    /// <summary>
    /// Gets the system flags
    /// </summary>
    [HttpPost("system/flags")]
    public TenantSettings GetSystemSettings()
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
        return true;
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
    /// Finalizes registration.  Finalization will fail if you call this before the RegistrationStatus == Complete.  You can just call it again.
    /// </summary>
    /// <param name="frid"></param>
    /// <returns></returns>
    [HttpGet("registration/finalize")]
    public async Task<IActionResult> FinalizeRegistration(Guid frid)
    {
        var status = await _regService.GetRegistrationStatus(frid);

        if (status != RegistrationStatus.ReadyForPassword)
        {
            throw new YouverseClientException("Cannot finalize pending registration", YouverseClientErrorCode.RegistrationStatusNotReadyForFinalization);
        }
            
        await _regService.FinalizeRegistration(frid);
        return Ok();
    }
    //
    
    
}