using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core.Services.Configuration;
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

}