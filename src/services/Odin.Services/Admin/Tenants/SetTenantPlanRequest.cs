using System.ComponentModel.DataAnnotations;

namespace Odin.Services.Admin.Tenants;
#nullable enable

/// <summary>
/// An identity's plan and what it gives (docs/hosting-plans.md). Left out, the quota and public web presence keep
/// the identity's current values.
/// </summary>
public class SetTenantPlanRequest
{
    /// <summary>
    /// The hosting provider's plan, a label; required
    /// </summary>
    [Required]
    public string? PlanId { get; set; }

    /// <summary>
    /// Bytes the identity may store; more than 0
    /// </summary>
    public long? QuotaBytes { get; set; }

    public bool? PublicWebPresence { get; set; }
}

public class SetTenantPlanResponse
{
    public string? PreviousPlanId { get; set; }

    /// <summary>The quota it had stored before; null for the host's default</summary>
    public long? PreviousQuotaBytes { get; set; }

    public bool? PreviousPublicWebPresence { get; set; }
}
