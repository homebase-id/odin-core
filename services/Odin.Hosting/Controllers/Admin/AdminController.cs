using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Admin;
using Odin.Core.Services.Admin.Tenants;

namespace Odin.Hosting.Controllers.Admin;
#nullable enable

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
    public async Task<ActionResult<List<TenantModel>>> GetTenants(
        [FromQuery(Name = "include-payload")] bool includePayload = false)
    {
        return await _tenantAdmin.GetTenants(includePayload);
    }

    //

    [HttpGet("tenants/{domain}")]
    public async Task<ActionResult<TenantModel>> GetTenant(
        string domain, [FromQuery(Name = "include-payload")] bool includePayload = false)
    {
        var tenant = await _tenantAdmin.GetTenant(domain, includePayload);
        if (tenant == null)
        {
            return NotFound();
        }
        return tenant;
    }

    //

    [HttpDelete("tenants/{domain}")]
    public async Task<ActionResult<AdminJobStatus>> DeleteTenant(string domain)
    {
        if (!await _tenantAdmin.TenantExists(domain))
        {
            return NotFound();
        }

        try
        {
            await _tenantAdmin.EnqueueDeleteTenant(domain);
        }
        catch (AdminValidationException e)
        {
            return BadRequest(new ProblemDetails
            {
                Title = e.Message
            });
        }

        return Accepted();
    }

    //

    // [HttpPost("tenants/{domain}/copy")]
    // public async Task<ActionResult<TenantCopyResponse>> CopyTenant(string domain)
    // {
    //     if (!await _tenantAdmin.TenantExists(domain))
    //     {
    //         return NotFound();
    //     }
    //
    //     try
    //     {
    //         var path = await _tenantAdmin.EnqueueCopyTenant(domain);
    //         return Accepted(new TenantCopyResponse {Path = path});
    //     }
    //     catch (AdminValidationException e)
    //     {
    //         return BadRequest(new ProblemDetails
    //         {
    //             Title = e.Message
    //         });
    //     }
    // }

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
