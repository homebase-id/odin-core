using System;
using System.Net;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Services.Base;
using Odin.Services.Configuration;

#nullable enable

namespace Odin.Services.Registry;

/// <summary>
/// The identity's plan, what it gives, how much of it is used, and who to ask for more (docs/hosting-plans.md)
/// </summary>
/// <param name="Id">The plan on the identity's registration, a label; null when it has none</param>
/// <param name="PublicWebPresence">Whether the identity has a public home page</param>
/// <param name="HostingProvider">Who runs this host for its owners; null when the host names none</param>
public record IdentityPlan(string? Id, bool PublicWebPresence, StorageUsage Storage, HostingProviderInfo? HostingProvider);

/// <summary>
/// How much an identity stores, against its quota
/// </summary>
/// <param name="QuotaBytes">The identity's own quota, or the host's default when it has none</param>
/// <param name="IsOverQuota">Exactly what <see cref="TenantQuotaGuard"/> decides: at or over the quota, or set
/// <see cref="TenantStatus.OutOfQuota"/> by an admin</param>
public record StorageUsage(long UsedBytes, long QuotaBytes, bool IsOverQuota);

/// <summary>
/// The hosting provider, for owners: whom they are sent to for a bigger plan
/// </summary>
/// <param name="UpgradeUrl">Its upgrade page for this identity, ready to open (?id=&amp;host= added); null when it has none</param>
public record HostingProviderInfo(string DisplayName, string? UpgradeUrl);

/// <summary>
/// Refuses writes that add payload bytes while this identity is out of quota: set
/// <see cref="TenantStatus.OutOfQuota"/> by an admin, or storing at least its quota (its registration's, or the
/// host's default).
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
    OdinConfiguration configuration,
    TableDriveMainIndexCached driveMainIndex)
{
    // How stale the usage behind the quota may be; see GetTotalSizeAllDrivesForQuotaAsync
    private static readonly TimeSpan UsageMaxAge = TimeSpan.FromMinutes(1);

    public async Task AssertCanAddPayloadBytesAsync()
    {
        var registration = Registration();
        var quota = Quota(registration);

        // Read usage only when it can matter: not when the status already decides
        var used = registration?.Status == TenantStatus.OutOfQuota ? 0 : await UsedBytesAsync();
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
        var quota = Quota(registration);
        var used = await UsedBytesAsync();
        return new IdentityPlan(
            registration?.PlanId,
            registration?.EnablePublicWebPresence ?? false,
            new StorageUsage(used, quota, IsOverQuota(registration, used, quota)),
            configuration.HostingProvider.InfoFor(tenantContext.HostOdinId.DomainName, configuration.Registry.ProvisioningDomain));
    }

    // The one rule: set OutOfQuota by an admin, or storing at least the quota
    private static bool IsOverQuota(IdentityRegistration? registration, long used, long quota)
    {
        return registration?.Status == TenantStatus.OutOfQuota || used >= quota;
    }

    private long Quota(IdentityRegistration? registration) =>
        configuration.HostingProvider.QuotaFor(registration?.QuotaBytes);

    private Task<long> UsedBytesAsync() => driveMainIndex.GetTotalSizeAllDrivesForQuotaAsync(UsageMaxAge);

    // The same in-memory lookup the multi-tenant middleware uses, so a status set on another node takes
    // effect here as soon as it has been announced
    private IdentityRegistration? Registration()
    {
        return identityRegistry.ResolveIdentityRegistration(tenantContext.HostOdinId.DomainName, out _);
    }
}
