using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Registry
{
    public class CertificateResolver : ICertificateResolver
    {
        private readonly DotYouContext _context;
        private readonly CertificateLocation _certificateLocation;

        public CertificateResolver(DotYouContext context)
        {
            _context = context;

            //TODO: maybe use a sha1 one-way hash for the domain? or md5 hash?
            //note: the primary is a placeholder for when we support multiple domains for a given dotYouReferenceId
            string certRoot = Path.Combine(context.DataRoot, "ssl", "primary");
            _certificateLocation = new CertificateLocation()
            {
                CertificatePath = Path.Combine(certRoot, "primary", "certificate.crt"),
                PrivateKeyPath = Path.Combine(certRoot, "primary", "private.key")
            };
        }
        
        public X509Certificate2 GetSSLCertificate()
        {
            using (X509Certificate2 publicKey = new X509Certificate2(_certificateLocation.CertificatePath))
            {
                string encodedKey = File.ReadAllText(_certificateLocation.PrivateKeyPath);
                RSA rsaPrivateKey;
                using (rsaPrivateKey = RSA.Create())
                {
                    rsaPrivateKey.ImportFromPem(encodedKey.ToCharArray());

                    using (X509Certificate2 pubPrivEphemeral = publicKey.CopyWithPrivateKey(rsaPrivateKey))
                    {
                        // Export as PFX and re-import if you want "normal PFX private key lifetime"
                        // (this step is currently required for SslStream, but not for most other things
                        // using certificates)
                        return new X509Certificate2(pubPrivEphemeral.Export(X509ContentType.Pfx));
                    }
                }
            }
        }

        public CertificateLocation GetSigningCertificate()
        {
            throw new NotImplementedException();
        }
    }
}