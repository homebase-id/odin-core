using System;
using System.Collections.Generic;
using System.Linq;
using Odin.Services.Configuration;
using HostingPlan = Odin.Services.Configuration.OdinConfiguration.RegistrySection.HostingPlan;

#nullable enable

namespace Odin.Services.Registry;

/// <summary>
/// What an identity's plan gives it, from Registry:Plans (docs/hosting-plans.md): its disk quota and whether it has a
/// public web presence. The one place a plan id becomes either, so the quota guard, the usage endpoint, the public
/// pages and the admin metrics always agree. A plan not in the config, or no plan, gets the default plan's.
/// </summary>
public class HostingPlans(OdinConfiguration configuration)
{
    // Ids are trimmed at load; empty when plans are off
    private readonly Dictionary<string, HostingPlan> _plans = configuration.Registry.Plans
        .ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);

    // Null exactly when plans are off: the config refuses a list without one
    private readonly HostingPlan? _default = configuration.Registry.Plans.SingleOrDefault(p => p.Default);

    /// <summary>
    /// Bytes the plan allows; null for unlimited, and for everyone while Registry:Plans is empty.
    /// </summary>
    public long? QuotaFor(string? planId)
    {
        return PlanFor(planId)?.QuotaBytes;
    }

    /// <summary>
    /// Whether the identity has a public web presence: its plan's say if the plan has one, otherwise its own
    /// setting (from its invitation code, or an admin).
    /// </summary>
    public bool PublicWebPresenceFor(string? planId, bool identitySetting)
    {
        return PlanFor(planId)?.PublicWebPresence ?? identitySetting;
    }

    public bool PublicWebPresenceFor(IdentityRegistration registration)
    {
        return PublicWebPresenceFor(registration.PlanId, registration.EnablePublicWebPresence);
    }

    /// <summary>
    /// Whether an admin may give an identity this plan: one in Registry:Plans, so a typo cannot silently mean
    /// the default plan. Any plan while plans are off, so plans can be assigned before they are turned on.
    /// </summary>
    public bool IsAssignable(string planId)
    {
        return planId != "" && (_plans.Count == 0 || _plans.ContainsKey(planId));
    }

    /// <summary>
    /// The plan an identity with this plan id gets; null while Registry:Plans is empty
    /// </summary>
    public HostingPlan? PlanFor(string? planId)
    {
        return planId != null && _plans.TryGetValue(planId.Trim(), out var plan)
            ? plan
            : _default;
    }
}
