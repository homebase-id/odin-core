using System.Threading.Tasks;
using Dawn;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core.Services.Provisioning;

namespace Youverse.Hosting.Controllers.OwnerToken.Configuration;

[ApiController]
[AuthorizeValidOwnerToken]
[Route(OwnerApiPathConstants.ConfigurationV1)]
public class ConfigurationController : Controller
{
    private readonly TenantProvisioningService _tenantProvisioningService;

    public ConfigurationController(TenantProvisioningService tenantProvisioningService)
    {
        _tenantProvisioningService = tenantProvisioningService;
    }

    /// <summary>
    /// Ensures all new configuration is setup when a new tenant is configured.  Only needs to be called once
    /// but will not cause issues if called multiple times
    /// </summary>
    [HttpPost("ensureInitialSetup")]
    public async Task<bool> EnsureInitialSetup()
    {
        await _tenantProvisioningService.EnsureInitialOwnerSetup();
        return true;
    }

    /// <summary>
    /// Updates the specified
    /// </summary>
    /// <returns></returns>
    [HttpPost("updateFlag")]
    public async Task<bool> UpdateFlag([FromBody] UpdateFlagRequest request)
    {
        Guard.Argument(request, nameof(request)).NotNull();
        Guard.Argument(request.FlagName, nameof(request.FlagName)).NotNull().NotEmpty();

        //todo: map to all the various flags
        return false;
    }
}

public class UpdateFlagRequest
{
    public string FlagName { get; set; }
    public bool Value { get; set; }
}