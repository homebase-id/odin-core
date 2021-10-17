using System;
using MessagePack;

namespace DotYou.DigitalIdentityHost.IdentityRegistry
{
    [MessagePackObject]
    public class CertificateRenewalInfo
    {
        [Key(0)]
        public Int64 CreatedTimestamp { get; set; }
        
        [Key(1)]
        public CertificateSigningRequest CertificateSigningRequest { get; set; }
        
    }
}