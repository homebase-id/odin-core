using Youverse.Core;

namespace Youverse.Provisioning.Services.Certificate
{
    public class CertificateRenewalInfo
    {
        public UnixTimeUtc CreatedTimestamp { get; set; }
        
        public CertificateSigningRequest CertificateSigningRequest { get; set; }
        
    }
}