using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Odin.Services.Registry;

namespace Odin.Services.Certificate
{
    public interface ICertificateService
    {
        /// <summary>
        /// Returns the SSL certificate for the current OdinId
        /// </summary>
        /// <param name="domain"></param>
        /// <returns></returns>
        public X509Certificate2 GetSslCertificate(string domain);

        /// <summary>
        /// Looks up the certificate to be used for the domain; even if the domain is supported as a SAN
        /// </summary>
        X509Certificate2 ResolveCertificate(string domain);
        
        /// <summary>
        /// Create certificate for domain
        /// </summary>
        Task<X509Certificate2> CreateCertificateAsync(string domain, CancellationToken cancellationToken = default);

        /// <summary>
        /// Create certificate for domain with sans (Subject Alternative Names)
        /// </summary>
        Task<X509Certificate2> CreateCertificateAsync(string domain, string[] sans, CancellationToken cancellationToken = default);

        /// <summary>
        /// Renew certificate for domain if about to expire
        /// </summary>
        Task<bool> RenewIfAboutToExpireAsync(IdentityRegistration idReg, CancellationToken cancellationToken = default);
    }

    public class AcmeAccountConfig
    {
        public string AcmeContactEmail { get; set; }
        public string AcmeAccountFolder { get; set; }
    }
}