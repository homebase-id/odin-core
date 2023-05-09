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
    /// Starts the registers a domain based on the reservation Id.  To complete registration,
    /// you must call <see cref="GetRegistrationStatus"/>. 
    /// </summary>
    /// <returns>A first run token used to allow the setting of the owner password</returns>
    Task<Guid> StartRegistration(RegistrationInfo registrationInfo);

    /// <summary>
    /// Performs all of the ending tasks to complete a registration
    /// </summary>
    /// <param name="firstRunToken"></param>
    /// <returns></returns>
    Task FinalizeRegistration(Guid firstRunToken);
    
    /// <summary>
    /// Finalizes the registration by storing the final records as well as providing certificate files if requested by the user.
    /// </summary>
    /// <param name="firstRunToken"></param>
    /// <returns></returns>
    Task<RegistrationStatus> GetRegistrationStatus(Guid firstRunToken);
    
    /// <summary>
    /// Reserves a domain for a configured amount of time.
    /// </summary>
    /// <param name="request"></param>
    /// <returns>Returns a <see cref="Reservation"/> describing the registration.  The <see cref="Reservation.Id"/> can be used to renew or finalize the reservation.</returns>
    Task<Reservation> Reserve(ReservationRequest request);

    /// <summary>
    /// Determines if the specified domain is available for registration.
    /// </summary>
    Task<bool> IsAvailable(string domain);

    /// <summary>
    /// Cancels an existing reservation, if it exists
    /// </summary>
    /// <param name="reservationId"></param>
    /// <returns></returns>
    Task CancelReservation(Guid reservationId);

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
    Task<DnsConfigurationSet> GetDnsConfiguration(string domain);

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
    /// <returns></returns>
    Task CreateIdentityOnDomain(string domain);
    
    //
    // Managed Domain
    //
    
    Task<bool> IsManagedDomainAvailable(string prefix, string apex);
    public Task CreateManagedDomain(string prefix, string apex);
    public Task DeleteManagedDomain(string prefix, string apex);
    
    //
    // Own Domain
    //

    public Task<bool> IsOwnDomainAvailable(string domain);
    
    /// <summary>
    /// Verifies if DNS records are correctly configured on own-domain
    /// </summary>
    /// <returns></returns>
    Task<(bool, DnsConfigurationSet)> GetOwnDomainDnsStatus(string domain);
    
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
    
public class DnsConfigurationSet
{
    public List<DnsConfig> BackendDnsRecords { get; init; } = new ();
    public List<DnsConfig> FrontendDnsRecords { get; init; } = new ();
    public List<DnsConfig> StorageDnsRecords { get; init; } = new ();
    public List<DnsConfig> AllDnsRecords =>
        BackendDnsRecords
            .Concat(FrontendDnsRecords)
            .Concat(StorageDnsRecords)
            .ToList();
}

//

public class DnsConfig
{
    public enum LookupRecordStatus 
    {
        Unknown,
        Success,                // domain found, correct value returned
        DomainOrRecordNotFound, // domain not found, retry later
        IncorrectValue,         // domain found, but DNS value is incorrect
    } 
    
    public string Type { get; init; } = "";            // e.g. "CNAME"
    public string Name { get; init; } = "";            // e.g. "www" or ""
    public string Domain { get; init; } = "";          // e.g. "www.example.com" or "example.com"
    public string Value { get; init; } = "";           // e.g. "example.com" or "127.0.0.1"
    public string Description { get; init; } = "";
    public LookupRecordStatus Status { get; set; } = LookupRecordStatus.Unknown;
}
    
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
