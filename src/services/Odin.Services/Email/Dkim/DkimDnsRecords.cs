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
}
