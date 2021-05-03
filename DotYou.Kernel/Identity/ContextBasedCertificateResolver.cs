using DotYou.Types.Certificate;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace DotYou.Kernel.Identity
{

    /// <summary>
    /// <see cref="ICertificateResolver"/> implementation which gets certificates from the IdentityRegistry
    /// </summary>
    public class ContextBasedCertificateResolver : ICertificateResolver
    {
        public X509Certificate2 Resolve(DotYouContext context)
        {
            //_logger.LogDebug($"looking up cert for [{hostname}]");

            string certificatePath = context.TenantCertificate.Location.CertificatePath;
            string privateKeyPath = context.TenantCertificate.Location.PrivateKeyPath;

            if (!File.Exists(certificatePath) || !File.Exists(privateKeyPath))
            {
                throw new Exception($"No certificate configured for {context.TenantCertificate.DomainName}");
            }

            return CertificateLoader.LoadPublicPrivateRSAKey(certificatePath, privateKeyPath);

        }


    }
}
