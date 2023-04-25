using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Youverse.Core.Services.Registry.Registration
{
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
        Task<List<string>> GetManagedDomainApexes();

        Task<bool> IsManagedDomainAvailable(string prefix, string apex);
        public Task CreateManagedDomain(string prefix, string apex);

        /// <summary>
        /// Returns the required <see cref="DnsConfig"/> for the domain
        /// </summary>
        /// <param name="domain"></param>
        /// <returns></returns>
        Task<DnsConfigurationSet> GetDnsConfiguration(string domain);
        
        /// <summary>
        /// Verifies if DNS records are correctly configured on own-domain
        /// </summary>
        /// <returns></returns>
        Task<(bool, DnsConfigurationSet)> VerifyOwnDomain(string domain);
    }
}