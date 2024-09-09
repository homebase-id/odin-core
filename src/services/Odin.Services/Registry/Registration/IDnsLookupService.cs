using System.Collections.Generic;
using System.Threading.Tasks;

namespace Odin.Services.Registry.Registration;

public interface IDnsLookupService
{
    List<DnsConfig> GetDnsConfiguration(string domain);
    Task<string> LookupZoneApex(string domain);
    Task<(bool, List<DnsConfig>)> GetAuthoritativeDomainDnsStatus(string domain);
    Task<(bool, List<DnsConfig>)> GetExternalDomainDnsStatus(string domain);
    Task<bool> IsManagedDomainAvailable(string prefix, string apex);
    void AssertManagedDomainApexAndPrefix(string prefix, string apex);
}