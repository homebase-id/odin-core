using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Configuration;
using Odin.Hosting.Controllers.ClientToken.App;
using Odin.Hosting.Controllers.ClientToken.Guest;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.ClientToken.Shared.Configuration;

/// <summary>
/// Configuration for the owner's system
/// </summary>
[ApiController]
[Route(AppApiPathConstants.DriveV1)]
[Route(GuestApiPathConstants.DriveV1)]
[AuthorizeValidGuestOrAppToken]
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
    public async Task<bool> IsIdentityServerConfigured()
    {
        var result = await _tenantConfigService.IsIdentityServerConfiguredAsync();
        return result;
    }

}