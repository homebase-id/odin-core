using System;
using System.Net;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Services.Base;

#nullable enable

namespace Odin.Services.Registry;

/// <summary>
/// The identity's hosting plan, what it gives, and how much of it is used (docs/hosting-plans.md)
/// </summary>
/// <param name="Id">The plan the identity gets: the default's for no plan or an unknown one; null while plans are off</param>
/// <param name="IsDefault">Whether that is the default plan</param>
/// <param name="PublicWebPresence">Whether the identity has a public home page: the plan's say, or its own</param>
/// <param name="UpgradeUrl">The hosting provider's page for a bigger plan, with this identity's ?id=; null when the
/// host has none</param>
public record IdentityPlan(string? Id, bool IsDefault, bool PublicWebPresence, StorageUsage Storage, string? UpgradeUrl);

/// <summary>
/// How much an identity stores, against what its plan allows
/// </summary>
/// <param name="QuotaBytes">Null for unlimited</param>
/// <param name="IsOverQuota">Exactly what <see cref="TenantQuotaGuard"/> decides: over the plan's quota, or set
/// <see cref="TenantStatus.OutOfQuota"/> by an admin</param>
public record StorageUsage(long UsedBytes, long? QuotaBytes, bool IsOverQuota);

/// <summary>
/// Refuses writes that add payload bytes while this identity is out of quota: set
/// <see cref="TenantStatus.OutOfQuota"/> by an admin, or storing at least what its plan allows.
/// Everything else (headers, metadata, reactions, read receipts, deletes, outgoing sends) keeps working.
/// </summary>
/// <remarks>
/// Call it before reading any bytes, so the caller is refused rather than stored. Peers are told to
/// retry later and keep the item in their outbox; see the outbox retry-later handling.
///
/// A soft limit: an upload's size is not known until it has been read, so this asks "is the identity
/// already full", not "would this fit", and the usage it reads may be up to a minute old. An upload or two
/// can go over, and freeing space can take a minute to count.
/// </remarks>
public class TenantQuotaGuard(
    IIdentityRegistry identityRegistry,
    TenantContext tenantContext,
    HostingPlans hostingPlans,
    TableDriveMainIndexCached driveMainIndex)
{
    // How stale the usage behind the quota may be; see GetTotalSizeAllDrivesForQuotaAsync
    private static readonly TimeSpan UsageMaxAge = TimeSpan.FromMinutes(1);

    public async Task AssertCanAddPayloadBytesAsync()
    {
        var registration = Registration();
        var quota = hostingPlans.QuotaFor(registration?.PlanId);

        // Read usage only when it can matter: not with no quota, nor when the status already decides
        var used = quota == null || registration?.Status == TenantStatus.OutOfQuota ? 0 : await UsedBytesAsync();
        if (IsOverQuota(registration, used, quota))
        {
            throw new OdinRetryLaterException(
                $"{tenantContext.HostOdinId.DomainName} is out of storage quota",
                HttpStatusCode.InsufficientStorage,
                TimeSpan.FromSeconds(TenantStatusRules.OutOfQuotaRetryAfterSeconds));
        }
    }

    public async Task<IdentityPlan> GetPlanAsync()
    {
        var registration = Registration();
        var plan = hostingPlans.PlanFor(registration?.PlanId);
        var used = await UsedBytesAsync();
        return new IdentityPlan(
            plan?.Id,
            plan?.Default ?? false,
            registration != null && hostingPlans.PublicWebPresenceFor(registration),
            new StorageUsage(used, plan?.QuotaBytes, IsOverQuota(registration, used, plan?.QuotaBytes)),
            hostingPlans.UpgradeUrlFor(tenantContext.HostOdinId));
    }

    // The one rule: set OutOfQuota by an admin, or storing at least what the plan allows
    private static bool IsOverQuota(IdentityRegistration? registration, long used, long? quota)
    {
        return registration?.Status == TenantStatus.OutOfQuota || used >= quota;
    }

    private Task<long> UsedBytesAsync() => driveMainIndex.GetTotalSizeAllDrivesForQuotaAsync(UsageMaxAge);

    // The same in-memory lookup the multi-tenant middleware uses, so a status set on another node takes
    // effect here as soon as it has been announced
    private IdentityRegistration? Registration()
    {
        return identityRegistry.ResolveIdentityRegistration(tenantContext.HostOdinId.DomainName, out _);
    }
}
