using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace Youverse.Core.Services.Certificate
{
    public interface ITenantCertificateService
    {
        /// <summary>
        /// Returns the SSL certificate for the current OdinId
        /// </summary>
        /// <param name="domain"></param>
        /// <returns></returns>
        public X509Certificate2 GetSslCertificate(string domain);

        /// <summary>
        /// Looks up the certificate to be used for the domain; even if the domain is supported as a SAN (i.e. www.frodo.digital comes from the certificate for frodo.digital)
        /// </summary>
        /// <param name="domain"></param>
        X509Certificate2 ResolveCertificate(string domain);
        
        /// <summary>
        /// Create certificate for domain
        /// </summary>
        Task<X509Certificate2> CreateCertificate(string domain);

        /// <summary>
        /// Renew certificate for domain if about to expire
        /// </summary>
        Task<bool> RenewIfAboutToExpire(string domain);
    }

    public class AcmeAccountConfig
    {
        public string AcmeContactEmail { get; set; }
        public string AcmeAccountFolder { get; set; }
    }
}