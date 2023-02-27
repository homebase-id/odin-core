using System;
using Youverse.Core.Identity;
using Youverse.Core.Services.Certificate;
using Youverse.Core.Util;

namespace Youverse.Core.Services.Registry
{
    public class IdentityRegistrationRequest
    {
        public OdinId DotYouId { get; set; }
        public bool IsCertificateManaged { get; set; }
        
        /// <summary>
        /// Optional certificates to match the <see cref="IdentityRegistrationRequest.DotYouId"/>
        /// </summary>
        public CertificatePemContent OptionalCertificatePemContent { get; set; }
    }
    
    public class IdentityRegistration
    {

        private string _primaryDomainName;
        private Guid _domainKey;

        public Guid Id { get; set; }

        /// <summary>
        /// A generated Guid based on the domain name
        /// </summary>
        public Guid DomainKey
        {
            get { return _domainKey; }
            set
            {
                //no-op: this is set by the domain name field
            }
        }

        public string PrimaryDomainName
        {
            get => _primaryDomainName;
            set
            {
                _primaryDomainName = value;
                _domainKey = new Guid(HashUtil.ReduceSHA256Hash(value.ToUtf8ByteArray()));
            }
        }
        
        /// <summary>
        /// Specifies the user requested the DI host manage the certificate
        /// </summary>
        public bool IsCertificateManaged { get; set; }

        /// <summary>
        /// A random id linking the registration process to this IdentityRegistration
        /// indicating the bearer was the one who registered the token
        /// </summary>
        public Guid? FirstRunToken { get; set; }
    }
}