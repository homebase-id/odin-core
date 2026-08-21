using System.Collections.Generic;
using Odin.Services.Registry.Registration;

namespace Odin.Services.Email.Dkim;

/// <summary>
/// Shapes a tenant's DKIM keys as DnsConfig records - the on-activation record set
/// from docs/email-dns-plan.md. Not part of GetDnsConfiguration (which is config-only):
/// DKIM values are per-tenant and exist only after email activation, so they are
/// written via IIdentityRegistrationService.WriteOnActivationRecords and shown in the
/// owner console from the tenant's stored keys.
/// </summary>
public static class DkimDnsRecords
{
    public static List<DnsConfig> ToDnsConfigs(string domainName, IEnumerable<DkimKey> keys)
    {
        var result = new List<DnsConfig>();
        foreach (var key in keys)
        {
            result.Add(new DnsConfig
            {
                Type = "TXT",
                Name = key.DnsRecordName,
                Domain = $"{key.DnsRecordName}.{domainName}",
                Value = key.DnsRecordValue,
                AltValue = key.DnsRecordValue,
                Description = $"DKIM key ({key.KTag})",
                // Like the other email records: never part of the identity-validation
                // success rule or the certificate DNS gate
                Optional = true,
            });
        }

        return result;
    }

    /// <summary>
    /// The record list for DELETING a domain's DKIM records without touching the key
    /// store: deletion dispatches on record type + name only, and the selector set is
    /// fixed (s1/s2), so no decryption - and no Email:DkimStorageKey - is needed.
    /// Used by the tenant-deletion ride-along.
    /// </summary>
    public static List<DnsConfig> DeletionConfigs(string domainName)
    {
        return
        [
            DeletionConfig(domainName, DkimKeyGenerator.Ed25519Selector),
            DeletionConfig(domainName, DkimKeyGenerator.RsaSelector),
        ];
    }

    private static DnsConfig DeletionConfig(string domainName, string selector)
    {
        var name = $"{selector}._domainkey";
        return new DnsConfig
        {
            Type = "TXT",
            Name = name,
            Domain = $"{name}.{domainName}",
            Value = "", // unused on delete
            Description = $"DKIM key ({selector})",
            Optional = true,
        };
    }
}
