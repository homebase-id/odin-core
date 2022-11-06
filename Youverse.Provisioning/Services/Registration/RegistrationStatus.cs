namespace Youverse.Provisioning.Services.Registration
{
    public enum RegistrationStatus
    {
        /// <summary>
        /// The registration is waiting for certificates to be generated
        /// </summary>
        AwaitingCertificate = 1,
        
        /// <summary>
        /// The registration is complete and ready for usage
        /// </summary>
        ReadyToFinalize = 2,
        
        /// <summary>
        /// Failed to create the SSL certificate during registration
        /// </summary>
        CertificateFailed = 3
    }
}