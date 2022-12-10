namespace Youverse.Core.Services.Certificate
{
    public class CertificateRenewalInfo
    {
        public UnixTimeUtc CreatedTimestamp { get; set; }
        
        public CertificateSigningRequest CertificateSigningRequest { get; set; }
        
    }
}