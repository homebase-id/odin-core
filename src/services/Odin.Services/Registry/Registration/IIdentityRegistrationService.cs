using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Odin.Services.Configuration;

namespace Odin.Services.Registry.Registration;

/// <summary>
/// Handles registration of a new domain identities; including creating SSL certificates.
/// </summary>
public interface IIdentityRegistrationService
{
    Task<string> LookupZoneApexAsync(string domain, CancellationToken cancellationToken = default);

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
    /// Verifies if DNS records are correctly configured using authoritative name servers
    /// </summary>
    /// <returns></returns>
    Task<(bool, List<DnsConfig>)> GetAuthoritativeDomainDnsStatus(string domain, CancellationToken cancellationToken = default);


    /// <summary>
    /// Verifies if DNS records are correctly configured using external name servers
    /// </summary>
    /// <returns></returns>
    Task<(bool, List<DnsConfig>)> GetExternalDomainDnsStatus(string domain, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Create identity on own or managed domain
    /// </summary>
    /// <returns>First-run token</returns>
    Task<Guid> CreateIdentityOnDomainAsync(string domain, string email, string planId);
    
    //
    // Managed Domain
    //
    
    Task<bool> IsManagedDomainAvailable(string prefix, string apex, CancellationToken cancellationToken = default);
    public Task DeleteManagedDomain(string prefix, string apex);
    public Task CreateManagedDomain(string prefix, string apex);
    
    //
    // Own Domain
    //

    public Task<bool> IsOwnDomainAvailable(string domain);
    public Task DeleteOwnDomain(string domain);
    
    //
    // OldHelpers
    //
    Task<bool> CanConnectToHostAndPort(string domain, int port);
    Task<bool> HasValidCertificate(string domain);

    /// <summary>
    /// Checks if invitation code is needed
    /// </summary>
    Task<bool> IsInvitationCodeNeeded();

    /// <summary>
    /// Determines if the invitation code is valid
    /// </summary>
    Task<bool> IsValidInvitationCode(string code);
}

