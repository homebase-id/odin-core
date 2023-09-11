using System;
using System.Collections.Generic;
using Odin.Core.Identity;
using Odin.Core.Services.Certificate;
using Odin.Core.Services.Registry.Registration;
using Odin.Core.Util;

namespace Odin.Core.Services.Registry
{
    public class IdentityRegistrationRequest
    {
        public OdinId OdinId { get; set; }
        public string Email { get; set; }
        
        /// <summary>
        /// The hosting PlanId as defined by the hosting company.  This is a string of text that
        /// gets stored with the identity   
        /// </summary>
        public string PlanId { get; set; }
        public bool IsCertificateManaged { get; set; }
        
        /// <summary>
        /// Optional certificates to match the <see cref="OdinId"/>
        /// </summary>
        public CertificatePemContent OptionalCertificatePemContent { get; set; }
    }
    
    public class IdentityRegistration
    {
        private string _primaryDomainName;

        public Guid Id { get; set; }
        public string Email { get; set; }


        public string PrimaryDomainName
        {
            get => _primaryDomainName;
            set
            {
                _primaryDomainName = value.ToLower();
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

        public string PlanId { get; set; }

        public override string ToString()
        {
            return PrimaryDomainName;
        }

        public string[] GetSans()
        {
            var result = new List<string>();
            foreach (var prefix in DnsConfigurationSet.WellknownPrefixes)
            {
                result.Add(prefix + "." + PrimaryDomainName);
            }
            return result.ToArray();
        }
    }
}