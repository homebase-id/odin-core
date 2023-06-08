using System;

namespace Odin.Core.Services.Registry.Registration
{
    /// <summary>
    /// Holds information required when registering an Identity.
    /// </summary>
    // [ValidateNever]
    public class RegistrationInfo
    {
        public Guid ReservationId { get; set; }
        
        /// <summary>
        /// Specifies the user requested the DI host manage the certificate
        /// </summary>
        public bool RequestedManagedCertificate { get; set; }
    }
}