using System;

namespace Youverse.Core.Services.Registry
{
    public class CertificateRenewalInfo
    {
        public Int64 CreatedTimestamp { get; set; }
        
        public CertificateSigningRequest CertificateSigningRequest { get; set; }
        
    }
}