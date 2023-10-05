using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Services.Configuration;

namespace Odin.Core.Services.Registry.Registration;

/// <summary>
/// Handles registration of a new domain identities; including creating SSL certificates.
/// </summary>
public interface IIdentityRegistrationService
{


    Task<string> LookupZoneApex(string domain);

    /// <summary>
    /// Returns a list of domains managed by this identity host.
    /// </summary>
    /// <returns></returns>
    Task<List<OdinConfiguration.RegistrySection.ManagedDomainApex>> GetManagedDomainApexes();
    
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
    /// <returns>First-run token</returns>
    Task<Guid> CreateIdentityOnDomain(string domain, string email, string planId);
    
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
    Task<bool> HasValidCertificate(string domain);

    /// <summary>
    /// Determines if the invitation code is valid
    /// </summary>
    Task<bool> IsValidInvitationCode(string code);
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
