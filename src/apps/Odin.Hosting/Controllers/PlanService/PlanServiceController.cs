using System.Threading.Tasks;
using Autofac;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Services.Admin.Tenants;
using Odin.Services.Registry;
using Odin.Services.Tenant.Container;

#nullable enable

namespace Odin.Hosting.Controllers.PlanService;

/// <summary>
/// For the hosting company's account service (raven-hosting), which sells bigger plans: read an identity's plan, and set it after
/// a payment (docs/hosting-plans.md section 9). On the provisioning domain, with the account service key
/// (Registry:AccountService:ApiKey); it can do nothing else, unlike the admin key. 404 for an identity not on this
/// host, which is how the service learns where an identity lives.
/// It answers what the identity has, not what the service last set: someone may have changed it here since, and the
/// service shows the owner what they really have, and how much of it they use.
/// </summary>
[ApiController]
[Route(PlanServiceApiPathConstants.BasePathV1 + "/identities/{domain}")]
[ServiceFilter(typeof(PlanServiceRestrictedAttribute))]
[ApiExplorerSettings(IgnoreApi = true)]
public class PlanServiceController(
    ITenantAdmin tenantAdmin,
    IIdentityRegistry identityRegistry,
    IMultiTenantContainer tenantContainer,
    ILogger<PlanServiceController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PlanServiceIdentity>> GetIdentity(string domain)
    {
        var registration = await identityRegistry.GetAsync(domain);
        return registration == null ? NotFound() : await IdentityFromAsync(registration);
    }

    /// <summary>
    /// Sets the identity's plan and, when given, its quota and public web presence; left out, they keep their current
    /// values. 400 for an empty plan Id or a quota that is not more than 0. Setting what it already has changes
    /// nothing, so the service can retry until it hears back.
    /// </summary>
    [HttpPut("plan")]
    public async Task<ActionResult<PlanServiceIdentity>> SetPlan(string domain, [FromBody] SetPlanServicePlanRequest request)
    {
        var previous = await tenantAdmin.SetTenantPlanAsync(domain, request.PlanId!, request.QuotaBytes, request.PublicWebPresence);
        if (previous == null)
        {
            return NotFound();
        }

        // A request value left out (null) is unchanged; a previous quota of null was the host's default
        logger.LogInformation(
            "Account service set {domain} to plan {planId}, quota {quota}, public web presence {publicWebPresence} " +
            "(was {previous}), reference {reference}",
            domain, request.PlanId!.Trim(), request.QuotaBytes, request.PublicWebPresence, previous, request.Reference);

        var registration = await identityRegistry.GetAsync(domain);
        return registration == null ? NotFound() : await IdentityFromAsync(registration);
    }

    // What the owner sees on their own plan page (TenantQuotaGuard.GetPlanAsync), asked in the identity's scope: the
    // same quota rule, the same usage (up to a minute old) and the same "full"
    private async Task<PlanServiceIdentity> IdentityFromAsync(IdentityRegistration registration)
    {
        var plan = await tenantContainer.GetTenantScope(registration.PrimaryDomainName)
            .Resolve<TenantQuotaGuard>()
            .GetPlanAsync();
        return new PlanServiceIdentity
        {
            Domain = registration.PrimaryDomainName,
            PlanId = plan.Id,
            QuotaBytes = plan.Storage.QuotaBytes,
            PublicWebPresence = plan.PublicWebPresence,
            UsedBytes = plan.Storage.UsedBytes,
            IsOverQuota = plan.Storage.IsOverQuota,
        };
    }
}

public class PlanServiceIdentity
{
    public string Domain { get; init; } = "";

    /// <summary>The plan on the identity's registration, a label; null when it has none</summary>
    public string? PlanId { get; init; }

    /// <summary>The quota in effect: the identity's own, or the host's default</summary>
    public long QuotaBytes { get; init; }

    public bool PublicWebPresence { get; init; }

    /// <summary>What it stores now, against the quota; up to a minute old</summary>
    public long UsedBytes { get; init; }

    /// <summary>At or over its quota, or marked out of quota by an admin: new payloads are refused</summary>
    public bool IsOverQuota { get; init; }
}

/// <summary>
/// The admin's plan request (<see cref="SetTenantPlanRequest"/>), plus the account service's reference
/// </summary>
public class SetPlanServicePlanRequest : SetTenantPlanRequest
{
    /// <summary>The service's own reference for why, e.g. its payment id; logged, so a change can be traced back</summary>
    public string? Reference { get; init; }
}
