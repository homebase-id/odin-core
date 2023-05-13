using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Youverse.Core.Services.Configuration;

namespace Youverse.Core.Services.Registry.Registration;

/// <summary>
/// Handles registration of a new domain identities; including creating SSL certificates.
/// </summary>
public interface IIdentityRegistrationService
{
    /// <summary>
    /// Returns a list of domains managed by this identity host.
    /// </summary>
    /// <returns></returns>
    Task<List<YouverseConfiguration.RegistrySection.ManagedDomainApex>> GetManagedDomainApexes();
    
    /// <summary>
    /// Returns the required <see cref="DnsConfig"/> for the domain
    /// </summary>
    /// <param name="domain"></param>
    /// <returns></returns>
    Task<List<DnsConfig>> GetDnsConfiguration(string domain);

    /// <summary>
    /// Does a DNS lookup on domain records using configured DNS Resolvers
    /// </summary>
    /// <param name="domain"></param>
    /// <returns></returns>
    Task<ExternalDnsResolverLookupResult> ExternalDnsResolverRecordLookup(string domain);
    
    /// <summary>
    /// Create identity on own or managed domain
    /// </summary>
    /// <param name="domain"></param>
    /// <returns>First-run token</returns>
    Task<Guid> CreateIdentityOnDomain(string domain);
    
    //
    // Managed Domain
    //
    
    Task<bool> IsManagedDomainAvailable(string prefix, string apex);
    public Task DeleteManagedDomain(string prefix, string apex);
    public Task CreateManagedDomain(string prefix, string apex);
    
    //
    // Own Domain
    //

    public Task<bool> IsOwnDomainAvailable(string domain);
    
    /// <summary>
    /// Verifies if DNS records are correctly configured on own-domain
    /// </summary>
    /// <returns></returns>
    Task<(bool, List<DnsConfig>)> GetOwnDomainDnsStatus(string domain);
    
    public Task DeleteOwnDomain(string domain);
    
    //
    // Helpers
    //
    Task<bool> CanConnectToHostAndPort(string domain, int port);
    Task<bool> HasValidCertifacte(string domain);
}

//
// DTOs
//

public class ExternalDnsResolverLookupResult
{
    public class ResolverStatus
    {
        public string ResolverIp { get; set; } = "";
        public string Domain { get; set; } = "";
        public bool Success { get; set; } = false;
    }
    public bool Success { get; set; }
    public List<ResolverStatus> Statuses { get; } = new();
}
