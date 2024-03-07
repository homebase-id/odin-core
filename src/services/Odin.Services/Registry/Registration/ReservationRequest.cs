using System;

namespace Odin.Services.Registry.Registration
{
    /// <summary>
    /// Holds the information required when requesting the reservation of a given domain name. 
    /// </summary>
    public class ReservationRequest
    {
        /// <summary>
        /// The domain name being reserved
        /// </summary>
        public string DomainName { get; set; }
        
        /// <summary>
        /// Used when a user decides to change their domain name after having previously reserved another domain name. 
        /// </summary>
        public Guid? PreviousReservationId { get; set; }
    }
}