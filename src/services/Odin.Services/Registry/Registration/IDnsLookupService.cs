using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Odin.Services.Registry.Registration;

public interface IDnsLookupService
{
    List<DnsConfig> GetDnsConfiguration(string domain);
    Task<string> LookupZoneApexAsync(string domain, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when the parent zone delegates the domain to our configured nameservers.
    /// Checks the delegation records at the parent's authority, so it works before our
    /// own zone exists - this is the domain-control proof for NS-based signups.
    /// </summary>
    Task<bool> IsDomainDelegatedToUsAsync(string domain, CancellationToken cancellationToken = default);
    Task<(bool, List<DnsConfig>)> GetAuthoritativeDomainDnsStatusAsync(string domain, CancellationToken cancellationToken = default);
    Task<(bool, List<DnsConfig>)> GetExternalDomainDnsStatusAsync(string domain, CancellationToken cancellationToken = default);
    Task<bool> IsManagedDomainAvailableAsync(string prefix, string apex, CancellationToken cancellationToken = default);
    void AssertManagedDomainApexAndPrefix(string prefix, string apex);
}