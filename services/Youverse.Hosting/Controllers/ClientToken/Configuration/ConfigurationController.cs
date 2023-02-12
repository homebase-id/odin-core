using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core.Services.Configuration;
using Youverse.Core.Services.Drive;
using Youverse.Hosting.Controllers.Anonymous;

namespace Youverse.Hosting.Controllers.ClientToken.Configuration;

/// <summary>
/// Configuration for the owner's system
/// </summary>
[ApiController]
[Route(AppApiPathConstants.DrivesV1)]
[Route(YouAuthApiPathConstants.DrivesV1)]
[AuthorizeValidExchangeGrant]
public class ConfigurationController : Controller
{
    private readonly TenantConfigService _tenantConfigService;
    private readonly IDriveStorageService _driveStorageService;

    /// <summary />
    public ConfigurationController(TenantConfigService tenantConfigService, IDriveStorageService driveStorageService)
    {
        _tenantConfigService = tenantConfigService;
        _driveStorageService = driveStorageService;
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

}