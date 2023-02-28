using System.Security.Cryptography.X509Certificates;

namespace Youverse.Core.Services.Registry
{
    public interface ICertificateResolver
    {
        /// <summary>
        /// Returns the SSL certificate for the current OdinId
        /// </summary>
        /// <returns></returns>
        public X509Certificate2 GetSslCertificate();

        /// <summary>
        /// Returns the location of the certificate used to sign documents.
        /// </summary>
        /// <returns></returns>
        public CertificateLocation GetSigningCertificate();
    }
}