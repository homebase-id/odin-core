using Youverse.Provisioning.Services.Certificate;

namespace Youverse.Provisioning.Services.Registration
{
    /// <summary>
    /// Holds information required when registering an Identity.
    /// </summary>
    public class RegistrationInfo
    {
        public Guid ReservationId { get; set; }
        public string EmailAddress { get; set; }
        
        /// <summary>
        /// Specifies the user requested the DI host manage the certificate
        /// </summary>
        public bool RequestedManagedCertificate { get; set; }
        
        
        public CertificateSigningRequest CertificateSigningRequest { get; set; }
    }
}