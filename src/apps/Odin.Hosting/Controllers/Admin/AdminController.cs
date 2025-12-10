using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Exceptions;
using Odin.Services.Admin;
using Odin.Services.Admin.Tenants;
using Odin.Hosting.Controllers.Job;

namespace Odin.Hosting.Controllers.Admin;
#nullable enable

[ApiController]
[Route(AdminApiPathConstants.BasePathV1)]
[ServiceFilter(typeof(AdminApiRestrictedAttribute))]
[ApiExplorerSettings(GroupName = "admin-v1")]
public class AdminController : ControllerBase
{
    private const string AdminJobStateRouteName = "AdminJobStateRoute";
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
        var tenant = await _tenantAdmin.GetTenantAsync(domain, includePayload);
        if (tenant == null)
        {
            return NotFound();
        }
        return tenant;
    }

    //

    [HttpDelete("tenants/{domain}")]
    public async Task<ActionResult> DeleteTenant(string domain)
    {
        if (!await _tenantAdmin.TenantExists(domain))
        {
            return NotFound();
        }

        var jobId = await _tenantAdmin.EnqueueDeleteTenant(domain);
        return AcceptedAtRoute(JobController.GetJobResponseRouteName, new { jobId });
    }

    //

    [HttpPost("tenants/{domain}/export")]
    public async Task<ActionResult> ExportTenant(string domain)
    {
        if (!await _tenantAdmin.TenantExists(domain))
        {
            return NotFound();
        }

        var jobId = await _tenantAdmin.EnqueueExportTenant(domain);
        return AcceptedAtRoute(JobController.GetJobResponseRouteName, new { jobId });
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
