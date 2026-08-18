using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Util;

namespace Odin.Services.Registry.Registration;

// Full identity domains are passed as AsciiDomainName - guaranteed valid and lowercased
// by construction, so implementations need no re-validation. Raw strings remain only
// where the value is not a full domain (prefix/apex parts) or where arbitrary DNS names
// (TLDs, roots) are legal - that lower-level surface lives in Odin.Core.Dns.
public interface IDnsLookupService
{
    List<DnsConfig> GetDnsConfiguration(AsciiDomainName domain);
    Task<string> LookupZoneApexAsync(AsciiDomainName domain, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when the parent zone delegates the domain to our configured nameservers.
    /// Checks the delegation records at the parent's authority, so it works before our
    /// own zone exists - this is the domain-control proof for NS-based signups.
    /// </summary>
    Task<bool> IsDomainDelegatedToUsAsync(AsciiDomainName domain, CancellationToken cancellationToken = default);
    Task<(bool, List<DnsConfig>)> GetAuthoritativeDomainDnsStatusAsync(AsciiDomainName domain, CancellationToken cancellationToken = default);
    Task<(bool, List<DnsConfig>)> GetExternalDomainDnsStatusAsync(AsciiDomainName domain, CancellationToken cancellationToken = default);
    Task<bool> IsManagedDomainAvailableAsync(string prefix, string apex, CancellationToken cancellationToken = default);
    void AssertManagedDomainApexAndPrefix(string prefix, string apex);
}
