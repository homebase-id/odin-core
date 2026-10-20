using System.ComponentModel.DataAnnotations;

namespace Odin.Services.Admin.Tenants;
#nullable enable

public class SetTenantPlanRequest
{
    /// <summary>
    /// Required, and must be in Registry:Plans while quotas are on (docs/hosting-plans.md)
    /// </summary>
    [Required]
    public string? PlanId { get; set; }
}

public class SetTenantPlanResponse
{
    public string? PreviousPlanId { get; set; }
}
