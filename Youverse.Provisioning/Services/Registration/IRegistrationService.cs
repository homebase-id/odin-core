using Youverse.Provisioning.Controllers;

namespace Youverse.Provisioning.Services.Registration
{
    /// <summary>
    /// Handles registration of a new domain identities; including creating SSL certificates.
    /// </summary>
    public interface IRegistrationService
    {
        /// <summary>
        /// Starts the registers a domain based on the reservation Id.  This will also start certificate
        /// generation which is an async process therefore you need to call <see cref="GetRegistrationStatus"/>
        /// to determine when you can call <see cref="FinalizeRegistration"/>. 
        /// </summary>
        /// <returns>A Guid used to monitor the completion of the registration</returns>
        Task<Guid> StartRegistration(RegistrationInfo registrationInfo);

        Task<RegistrationStatus> GetRegistrationStatus(Guid pendingRegistrationId);

        /// <summary>
        /// Finalizes the registration by storing the final records as well as providing certificate files if requested by the user.
        /// </summary>
        /// <param name="pendingRegistrationId"></param>
        /// <returns></returns>
        Task<object> FinalizeRegistration(Guid pendingRegistrationId);
        
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
    }
}