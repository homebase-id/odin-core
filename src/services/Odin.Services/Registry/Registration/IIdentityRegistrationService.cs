using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Odin.Services.Configuration;

namespace Odin.Services.Registry.Registration;

public enum CreateOwnDomainZoneResult
{
    /// <summary>The zone exists: created now or already present</summary>
    Created,

    /// <summary>Zone hosting is not configured on this deployment (permanent)</summary>
    NotConfigured,

    /// <summary>The domain lies inside a zone we already host; a child zone would shadow it (permanent)</summary>
    ShadowsHostedZone,

    /// <summary>No proof of domain control yet - retry after DNS setup (transient)</summary>
    ControlNotProven,
}

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
    Task<Guid> CreateIdentityOnDomainAsync(string domain, string email, string planId, string invitationCode);
    
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

    /// <summary>
    /// True when we can host DNS zones for own-domains (PowerDNS + nameservers configured).
    /// </summary>
    bool CanHostOwnDomainZones { get; }

    /// <summary>
    /// Pre-provisions the DNS zone for an own-domain in our PowerDNS so the user can
    /// delegate to our nameservers now or later. Idempotent; no-op when zone hosting
    /// is not configured. Requires proof of domain control (registered identity,
    /// delegation to our nameservers at the parent, or valid manual DNS records) and
    /// refuses domains inside zones we already host.
    /// </summary>
    Task<CreateOwnDomainZoneResult> CreateOwnDomainZone(string domain, CancellationToken cancellationToken = default);

    /// <summary>
    /// Best-effort removal of an own-domain's zone. Never throws.
    /// </summary>
    Task DeleteOwnDomainZone(string domain);

    /// <summary>
    /// Best-effort DNS cleanup for a deleted tenant: managed domains get their records
    /// removed from the shared apex zone, own domains get their zone deleted. Never throws.
    /// </summary>
    Task DeleteDnsRecordsForDomain(string domain);
    
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

    /// <summary>
    /// Determines if the invitation code grants a public web presence.
    /// True unless the code is configured in Registry:InvitationCodesWithoutPublicWebPresence.
    /// </summary>
    Task<bool> CodeGrantsPublicWebPresence(string code);
}

