using System;
using System.Net;
using Odin.Core.Exceptions;
using Odin.Services.Base;

#nullable enable

namespace Odin.Services.Registry;

/// <summary>
/// Refuses writes that add payload bytes while this identity is <see cref="TenantStatus.OutOfQuota"/>.
/// Everything else (metadata, reactions, read receipts, deletes, outgoing sends) keeps working.
/// </summary>
/// <remarks>
/// Call it before reading any bytes, so the caller is refused rather than stored. Peers are told to
/// retry later and keep the item in their outbox; see the outbox retry-later handling.
/// </remarks>
public class TenantQuotaGuard(IIdentityRegistry identityRegistry, TenantContext tenantContext)
{
    public void AssertCanAddPayloadBytes()
    {
        var domain = tenantContext.HostOdinId.DomainName;

        // The same in-memory lookup the multi-tenant middleware uses, so a status set on another
        // node takes effect here as soon as it has been announced.
        var registration = identityRegistry.ResolveIdentityRegistration(domain, out _);
        if (registration?.Status == TenantStatus.OutOfQuota)
        {
            throw new OdinRetryLaterException(
                $"{domain} is out of storage quota",
                HttpStatusCode.InsufficientStorage,
                TimeSpan.FromSeconds(TenantStatusRules.OutOfQuotaRetryAfterSeconds));
        }
    }
}
