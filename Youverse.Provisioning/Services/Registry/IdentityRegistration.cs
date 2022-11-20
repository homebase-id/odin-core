﻿using Youverse.Core.Services.Registry;
using Youverse.Core.Util;
using Youverse.Provisioning.Services.Certificate;

namespace Youverse.Provisioning.Services.Registry
{
    public class mIdentityRegistration
    {
        private string _domainName;
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

        public string DomainName
        {
            get => _domainName;
            set
            {
                _domainName = value;
                
                _domainKey = HashUtil.ReduceSHA256Hash(value);
            }
        }

        public string PrivateKeyRelativePath { get; set; }

        public string PublicKeyCertificateRelativePath { get; set; }

        public string FullChainCertificateRelativePath { get; set; }

        /// <summary>
        /// Specifies the user requested the DI host manage the certificate
        /// </summary>
        public bool IsCertificateManaged { get; set; }
        
        public CertificateRenewalInfo CertificateRenewalInfo { get; set; }
    }
}