using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace DotYou.Types.Certificate
{
    public static class CertificateLoader
    {
        public static X509Certificate2 LoadWithKeyFile(string certificatePath, string privateKeyPath)
        {
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
