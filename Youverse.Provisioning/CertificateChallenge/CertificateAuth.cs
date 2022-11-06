namespace Youverse.Provisioning.CertificateChallenge
{
    /// <summary>
    /// Information for the AcmeChallenge
    /// </summary>
    public class CertificateAuth
    {
        public string Token { get; set; }

        public string Auth { get; set; }
    }
}