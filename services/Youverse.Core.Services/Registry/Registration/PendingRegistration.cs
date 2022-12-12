using System;
using Youverse.Core.Util;

namespace Youverse.Core.Services.Registry.Registration
{
    /// <summary>
    /// Information about domain in the process of being registered.
    /// </summary>
    public class PendingRegistration
    {
        private Guid _domainKey;
        private string _domain;

        public PendingRegistration()
        {
            Id = Guid.NewGuid();
        }

        public Guid Id { get; set; } 

        /// <summary>
        /// Time in Unix-time Milliseconds when the registration was created
        /// </summary>
        public UnixTimeUtc CreatedTimestamp { get; set; }

        public string Domain
        {
            get => _domain;
            set
            {
                _domain = value;
                _domainKey = HashUtil.ReduceSHA256Hash(value);
            }
        }

        public Guid DomainKey
        {
            get { return _domainKey; }
        }

        // public RegistrationStatus Status { get; set; }

        /// <summary>
        /// Associated email address 
        /// </summary>
        public string EmailAddress { get; set; }

        /// <summary>
        /// Specifies the user requested the DI host manage the certificate
        /// </summary>
        public bool IsCertificateManaged { get; set; }
        
        /// <summary>
        /// The reservation first used for the domain
        /// </summary>
        public Reservation Reservation { get; set; }
        
        // public CertificateOrder Order { get; set; }

        /// <summary>
        /// The certificate order used to track certificate creation status 
        /// </summary>
        public Guid OrderId { get; set; }
    }
}