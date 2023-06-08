namespace Youverse.Core.Services.Certificate
{
    /// <summary>
    /// Holds the content of a generated certificate
    /// </summary>
    public class CertificatePemContent
    {
        public string PrivateKey { get; set; }
        public string Certificate { get; set; }
    }
}