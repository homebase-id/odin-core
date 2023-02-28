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

        public Task SaveSslCertificate(Guid registryId, string domain, CertificatePemContent content);

        /// <summary>
        /// Tests if all certificates for the identity are valid
        /// </summary>
        Task<bool> AreAllCertificatesValid();
        
        bool IsCertificateExpired(X509Certificate2 cert);

        /// <summary>
        /// Gets a list of the <see cref="IdentityCertificateDefinition"/>s that need a new certificate, either because it's expiring soon, invalid, or missing.
        /// </summary>
        /// <param name="force"></param>
        /// <returns></returns>
        public Task<List<IdentityCertificateDefinition>> GetIdentitiesRequiringNewCertificate(bool force);

        /// <summary>
        /// Looks up the certificate to be used for the domain; even if the domain is supported as a SAN (i.e. www.frodo.digital comes from the certificate for frodo.digital)
        /// </summary>
        /// <param name="domain"></param>
        X509Certificate2 ResolveCertificate(string domain);
    }
}