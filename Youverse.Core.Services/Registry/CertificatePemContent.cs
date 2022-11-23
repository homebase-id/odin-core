namespace Youverse.Core.Services.Registry
{
    /// <summary>
    /// Holds the content of a generated certificate
    /// </summary>
    public class CertificatePemContent
    {
        public string PrivateKey { get; set; }
        
        public string PublicKeyCertificate { get; set; }
        public string FullChain { get; set; }
    }
}