﻿using Microsoft.Extensions.Logging;
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

            string certificatePath = context.Certificate.Location.CertificatePath;
            string privateKeyPath = context.Certificate.Location.PrivateKeyPath;

            if (!File.Exists(certificatePath) || !File.Exists(privateKeyPath))
            {
                throw new Exception($"No certificate configured for {context.Certificate.DomainName}");
            }

            using (X509Certificate2 publicKey = new X509Certificate2(certificatePath))
            {
                string encodedKey = File.ReadAllText(privateKeyPath);
                RSA rsaPrivateKey;
                using (rsaPrivateKey = RSA.Create())
                {
                    rsaPrivateKey.ImportFromPem(encodedKey.ToCharArray());

                    using (X509Certificate2 pubPrivEphemeral = publicKey.CopyWithPrivateKey(rsaPrivateKey))
                    {
                        //_logger.LogInformation($"Certificate resolved for [{hostname}]");

                        // Export as PFX and re-import if you want "normal PFX private key lifetime"
                        // (this step is currently required for SslStream, but not for most other things
                        // using certificates)
                        return new X509Certificate2(pubPrivEphemeral.Export(X509ContentType.Pfx));
                    }
                }
            }

        }


    }
}
