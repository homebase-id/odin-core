using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dawn;
using Microsoft.AspNetCore.Mvc;
using Refit;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Provisioning;

namespace Youverse.Hosting.Controllers.OwnerToken.Configuration;

/// <summary>
/// Configuration for the owner's system
/// </summary>
[ApiController]
[AuthorizeValidOwnerToken]
[Route(OwnerApiPathConstants.ConfigurationV1)]
public class ConfigurationController : Controller
{
    private readonly TenantConfigService _tenantConfigService;
    private readonly IDriveService _driveService;

    /// <summary />
    public ConfigurationController(TenantConfigService tenantConfigService, IDriveService driveService)
    {
        _tenantConfigService = tenantConfigService;
        _driveService = driveService;
    }

    /// <summary>
    /// Ensures all new configuration is setup when a new tenant is configured.  Only needs to be called once
    /// but will not cause issues if called multiple times
    /// </summary>
    [HttpPost("system/initialize")]
    public async Task<bool> InitializeIdentity([FromBody]InitialSetupRequest request)
    {
        await _tenantConfigService.EnsureInitialOwnerSetup(request);
        return true;
    }

    /// <summary>
    /// Updates the specified
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
    /// Returns the built-in <see cref="TargetDrive"/>Info for the built-in drives
    /// </summary>
    /// <returns></returns>
    [HttpGet("system/driveinfo")]
    public Task<Dictionary<string, TargetDrive>> GetSystemDrives()
    {
        var d = new Dictionary<string, TargetDrive>()
        {
            { "contact", SystemDriveConstants.ContactDrive },
            { "profile", SystemDriveConstants.ProfileDrive }
        };

        return Task.FromResult(d);
    }

    /// <summary>
    /// Updates a setting for use in the owner-app
    /// </summary>
    [HttpPost("ownerapp/updatesetting")]
    public async Task<bool> UpdateOwnerAppSetting([FromBody] UpdateFlagRequest request)
    {
        Guard.Argument(request, nameof(request)).NotNull();
        Guard.Argument(request.FlagName, nameof(request.FlagName)).NotNull().NotEmpty();

        //todo: map to all the various flags
        return false;
    }

    //
}