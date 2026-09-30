using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Exceptions;
using Odin.Services.Admin;
using Odin.Services.Admin.Tenants;
using Odin.Services.Registry.PayloadMove;
using Odin.Services.Registry;
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
    private readonly PayloadMoveAdmin _payloadMoveAdmin;

    public AdminController(ITenantAdmin tenantAdmin, PayloadMoveAdmin payloadMoveAdmin)
    {
        _tenantAdmin = tenantAdmin;
        _payloadMoveAdmin = payloadMoveAdmin;
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

    // NOTE: a literal route segment outranks the {domain} parameter below, so this does not
    // shadow "tenants/{domain}". Pinned by AdminControllerTest.ItShouldNotShadowTheTenantByDomainRoute.
    [HttpGet("tenants/metrics")]
    public async Task<ActionResult<TenantMetricsResponse>> GetTenantMetrics()
    {
        return await _tenantAdmin.GetTenantMetricsAsync();
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

    /// <summary>
    /// Queues the tenant's deletion; a copy disabled as moved is purged and keeps its DNS. 400 while a move is under way,
    /// or for a moved copy with email unless discard-mail.
    /// </summary>
    [HttpDelete("tenants/{domain}")]
    public async Task<ActionResult> DeleteTenant(string domain, [FromQuery(Name = "discard-mail")] bool discardMail = false)
    {
        if (!await _tenantAdmin.TenantExists(domain))
        {
            return NotFound();
        }

        var jobId = await _tenantAdmin.EnqueueDeleteTenant(domain, discardMail);
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
    public async Task<ActionResult> DisableTenant(string domain)
    {
        if (!await _tenantAdmin.TenantExists(domain))
        {
            return NotFound();
        }

        await _tenantAdmin.DisableTenant(domain);

        return Ok();
    }

    //

    /// <summary>
    /// The tenant's payload move: its handoff and completion here if it was exported from this host, the
    /// transfer's progress if it was imported into it.
    /// </summary>
    [HttpGet("tenants/{domain}/payload-move")]
    public async Task<ActionResult<PayloadMoveReport>> GetPayloadMove(string domain)
    {
        var report = await _payloadMoveAdmin.GetAsync(domain);
        return report == null ? NotFound() : report;
    }

    /// <summary>
    /// Runs the tenant's payload transfer again from the newest file (what already arrived is skipped).
    /// 404 if it has none here, 409 while a slice is running.
    /// </summary>
    [HttpPost("tenants/{domain}/payload-move/retry")]
    public async Task<IActionResult> RetryPayloadMove(string domain)
    {
        return await _payloadMoveAdmin.RetryAsync(domain) switch
        {
            PayloadMoveRetryResult.Rearmed => Ok(),
            PayloadMoveRetryResult.Running => Conflict(),
            _ => NotFound()
        };
    }

    //

    /// <summary>
    /// Sets the tenant's status and returns the previous one. 400 if the transition is not allowed.
    /// </summary>
    [HttpPatch("tenants/{domain}/status")]
    public async Task<ActionResult<TenantStatusState>> SetTenantStatus(string domain, [FromBody] SetTenantStatusRequest request)
    {
        // A missing status is a 400 before we get here: [Required] on the property, [ApiController] on the class
        var previous = await _tenantAdmin.SetTenantStatusAsync(domain, request.Status!.Value, request.DisabledReason, request.UnlockMoved);
        if (previous == null)
        {
            return NotFound();
        }

        return Ok(previous);
    }

    //

    [HttpPatch("tenants/{domain}/public-web-presence/enable")]
    public async Task<ActionResult> EnablePublicWebPresence(string domain)
    {
        if (!await _tenantAdmin.TenantExists(domain))
        {
            return NotFound();
        }

        await _tenantAdmin.EnablePublicWebPresence(domain);

        return Ok();
    }

    //

    [HttpPatch("tenants/{domain}/public-web-presence/disable")]
    public async Task<ActionResult> DisablePublicWebPresence(string domain)
    {
        if (!await _tenantAdmin.TenantExists(domain))
        {
            return NotFound();
        }

        await _tenantAdmin.DisablePublicWebPresence(domain);

        return Ok();
    }

    //

}
