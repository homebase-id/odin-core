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
public class PlanQuotas
{
    private readonly Dictionary<string, long?> _quotas;
    private readonly long? _defaultQuota;

    public PlanQuotas(OdinConfiguration configuration)
    {
        _quotas = configuration.Registry.Plans
            .ToDictionary(p => p.Id.Trim(), p => p.QuotaBytes, StringComparer.OrdinalIgnoreCase);
        _defaultQuota = _quotas.GetValueOrDefault(HostingPlan.DefaultPlanId);
    }

    /// <summary>
    /// False when Registry:Plans is empty: no identity has a quota
    /// </summary>
    public bool IsEnabled => _quotas.Count > 0;

    /// <summary>
    /// Bytes the plan allows; null for unlimited, and for everyone while quotas are off. A plan not in the
    /// config, or no plan, gets the default plan's quota.
    /// </summary>
    public long? QuotaFor(string? planId)
    {
        if (!IsEnabled)
        {
            return null;
        }

        return planId != null && _quotas.TryGetValue(planId.Trim(), out var quota) ? quota : _defaultQuota;
    }
}
