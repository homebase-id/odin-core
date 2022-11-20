using System;

namespace Youverse.Core.Services.Registry
{
    public class CertificateRenewalInfo
    {
        public UnixTimeUtc CreatedTimestamp { get; set; }
        
        public CertificateSigningRequest CertificateSigningRequest { get; set; }
        
    }
}