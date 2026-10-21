using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Services.Admin.Tenants;
using Odin.Services.Registry;

#nullable enable

namespace Odin.Hosting.Controllers.PlanService;

/// <summary>
/// For the hosting company's account service (homebase-invites), which sells bigger plans: read an identity's plan, and set it after
/// a payment (docs/hosting-plans.md section 9). On the provisioning domain, with the account service key
/// (Registry:AccountService:ApiKey); it can do nothing else, unlike the admin key. 404 for an identity not on this
/// host, which is how the service learns where an identity lives.
/// </summary>
[ApiController]
[Route(PlanServiceApiPathConstants.BasePathV1 + "/identities/{domain}")]
[ServiceFilter(typeof(PlanServiceRestrictedAttribute))]
[ApiExplorerSettings(IgnoreApi = true)]
public class PlanServiceController(
    ITenantAdmin tenantAdmin,
    HostingPlans hostingPlans,
    ILogger<PlanServiceController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PlanServiceIdentity>> GetIdentity(string domain)
    {
        var tenant = await tenantAdmin.GetTenantAsync(domain, includePayload: false);
        if (tenant == null)
        {
            return NotFound();
        }

        return new PlanServiceIdentity { Domain = tenant.Domain, PlanId = hostingPlans.PlanFor(tenant.PlanId)?.Id };
    }

    /// <summary>
    /// Sets the identity's plan; 400 for a plan not in Registry:Plans. Setting the plan it already has changes
    /// nothing, so the service can retry until it hears back.
    /// </summary>
    [HttpPut("plan")]
    public async Task<ActionResult<PlanServiceIdentity>> SetPlan(string domain, [FromBody] SetPlanServicePlanRequest request)
    {
        if (!await tenantAdmin.TenantExists(domain))
        {
            return NotFound();
        }

        // A missing plan is a 400 before we get here: [Required] on the property, [ApiController] on the class
        var previous = await tenantAdmin.SetTenantPlanAsync(domain, request.PlanId!);
        logger.LogInformation("Account service set {domain} to plan {planId} (was {previous}), reference {reference}",
            domain, request.PlanId!.Trim(), previous ?? "none", request.Reference ?? "none");

        return new PlanServiceIdentity { Domain = domain, PlanId = hostingPlans.PlanFor(request.PlanId)?.Id };
    }
}

public class PlanServiceIdentity
{
    public string Domain { get; init; } = "";

    /// <summary>The plan the identity gets, as configured: the default's for no plan or an unknown one; null while plans are off</summary>
    public string? PlanId { get; init; }
}

public class SetPlanServicePlanRequest
{
    [Required]
    public string? PlanId { get; init; }

    /// <summary>The service's own reference for why, e.g. its payment id; logged, so a change can be traced back</summary>
    public string? Reference { get; init; }
}
