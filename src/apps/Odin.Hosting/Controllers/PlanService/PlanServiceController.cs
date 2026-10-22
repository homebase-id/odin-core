using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Services.Admin.Tenants;
using Odin.Services.Configuration;
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
    IIdentityRegistry identityRegistry,
    OdinConfiguration configuration,
    ILogger<PlanServiceController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PlanServiceIdentity>> GetIdentity(string domain)
    {
        var registration = await identityRegistry.GetAsync(domain);
        return registration == null ? NotFound() : IdentityFrom(registration);
    }

    /// <summary>
    /// Sets the identity's plan and, when given, its quota and public web presence; left out, they keep their current
    /// values. 400 for an empty plan Id or a quota that is not more than 0. Setting what it already has changes
    /// nothing, so the service can retry until it hears back.
    /// </summary>
    [HttpPut("plan")]
    public async Task<ActionResult<PlanServiceIdentity>> SetPlan(string domain, [FromBody] SetPlanServicePlanRequest request)
    {
        // A missing plan is a 400 before we get here: [Required] on the property, [ApiController] on the class
        var previous = await tenantAdmin.SetTenantPlanAsync(domain, request.PlanId!, request.QuotaBytes, request.PublicWebPresence);
        if (previous == null)
        {
            return NotFound();
        }

        logger.LogInformation(
            "Account service set {domain} to plan {planId}, quota {quota}, public web presence {publicWebPresence} " +
            "(was {previousPlan}, {previousQuota}, {previousPublicWebPresence}), reference {reference}",
            domain, request.PlanId!.Trim(), request.QuotaBytes?.ToString() ?? "unchanged",
            request.PublicWebPresence?.ToString() ?? "unchanged", previous.PlanId ?? "none",
            previous.QuotaBytes?.ToString() ?? "default", previous.EnablePublicWebPresence, request.Reference ?? "none");

        var registration = await identityRegistry.GetAsync(domain);
        return registration == null ? NotFound() : IdentityFrom(registration);
    }

    private PlanServiceIdentity IdentityFrom(IdentityRegistration registration) => new()
    {
        Domain = registration.PrimaryDomainName,
        PlanId = registration.PlanId,
        QuotaBytes = configuration.HostingProvider.QuotaFor(registration.QuotaBytes),
        PublicWebPresence = registration.EnablePublicWebPresence,
    };
}

public class PlanServiceIdentity
{
    public string Domain { get; init; } = "";

    /// <summary>The plan on the identity's registration, a label; null when it has none</summary>
    public string? PlanId { get; init; }

    /// <summary>The quota in effect: the identity's own, or the host's default</summary>
    public long QuotaBytes { get; init; }

    public bool PublicWebPresence { get; init; }
}

public class SetPlanServicePlanRequest
{
    [Required]
    public string? PlanId { get; init; }

    /// <summary>Bytes the identity may store, more than 0; left out, unchanged</summary>
    public long? QuotaBytes { get; init; }

    /// <summary>Left out, unchanged</summary>
    public bool? PublicWebPresence { get; init; }

    /// <summary>The service's own reference for why, e.g. its payment id; logged, so a change can be traced back</summary>
    public string? Reference { get; init; }
}
