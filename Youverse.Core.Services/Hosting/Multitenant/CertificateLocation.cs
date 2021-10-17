namespace DotYou.IdentityRegistry
{
    /// <summary>
    /// Specifies the name information of the individual who owns a certificate
    /// </summary>
    public class NameInfo
    {
        public string GivenName { get; set; }
        public string Surname { get; set; }
    }

    public class CertificateLocation
    {
        public string PrivateKeyPath { get; set; }

        public string CertificatePath { get; set; }

    }
}