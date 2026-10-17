using System;
using System.Collections.Generic;
using System.Linq;
using Odin.Services.Configuration;
using HostingPlan = Odin.Services.Configuration.OdinConfiguration.RegistrySection.HostingPlan;

#nullable enable

namespace Odin.Services.Registry;

/// <summary>
/// An identity's disk quota, from its plan and Registry:Plans (docs/plan-quota.md). The one place a plan
/// becomes a limit, so the quota guard, the usage endpoint and the admin metrics always agree.
/// </summary>
public class PlanQuotas(OdinConfiguration configuration)
{
    // Ids are trimmed at load; empty when quotas are off
    private readonly Dictionary<string, long?> _quotas = configuration.Registry.Plans
        .ToDictionary(p => p.Id, p => p.QuotaBytes, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Bytes the plan allows; null for unlimited, and for everyone while Registry:Plans is empty. A plan not in
    /// the config, or no plan, gets the default plan's quota.
    /// </summary>
    public long? QuotaFor(string? planId)
    {
        return planId != null && _quotas.TryGetValue(planId.Trim(), out var quota)
            ? quota
            : _quotas.GetValueOrDefault(HostingPlan.DefaultPlanId);
    }
}
