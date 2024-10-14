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
        var db = _tenantSystemStorage.IdentityDatabase;
        var result = _tenantConfigService.IsIdentityServerConfigured();
        return Task.FromResult(result);
    }

}