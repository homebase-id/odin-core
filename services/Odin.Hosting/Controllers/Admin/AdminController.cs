using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Admin.Tenants;

namespace Odin.Hosting.Controllers.Admin;

[ApiController]
[Route(AdminApiPathConstants.BasePathV1)]
[ServiceFilter(typeof(AdminApiRestrictedAttribute))]
public class AdminController : ControllerBase
{
    private readonly ITenantAdmin _tenantAdmin;

    public AdminController(ITenantAdmin tenantAdmin)
    {
        _tenantAdmin = tenantAdmin;
    }

    //

    [HttpGet("ping")]
    public ActionResult<string> Ping()
    {
        return "pong";
    }

    //

    [HttpGet("tenants")]
    public async Task<ActionResult<List<TenantModel>>> GetTenants()
    {
        return await _tenantAdmin.GetTenants();
    }

    //

    [HttpGet("tenants/{domain}")]
    public async Task<ActionResult<TenantModel>> GetTenant(string domain)
    {
        var tenant = await _tenantAdmin.GetTenant(domain);
        if (tenant == null)
        {
            return NotFound();
        }
        return tenant;
    }

    //

    [HttpPatch("tenants/{domain}/enable")]
    public async Task<ActionResult> EnableTenant(string domain)
    {
        if (!await _tenantAdmin.TenantExists(domain))
        {
            return NotFound();
        }

        await _tenantAdmin.EnableTenant(domain);

        return Ok();
    }

    //

    [HttpPatch("tenants/{domain}/disable")]
    public async Task<ActionResult> ResumeTenant(string domain)
    {
        if (!await _tenantAdmin.TenantExists(domain))
        {
            return NotFound();
        }

        await _tenantAdmin.DisableTenant(domain);

        return Ok();
    }

    //

}
