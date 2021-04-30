using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace DotYou.TenantHost.Certificate.FileBased
{

    /// <summary>
    /// Certificates are expected to be next to the assemblies
    /// </summary>
    internal class FilebasedBasedCertificateResolver : ICertificateResolver
    {
        private readonly ILogger<FilebasedBasedCertificateResolver> _logger;

        //TODO: enable when i figure out DI at the hostbuilder level
        //public FileBasedCertificateResolver(ILogger<FileBasedCertificateResolver> logger)
        //{
        //    _logger = logger;
        //}

        public X509Certificate2 Resolve(string hostname)
        {
            //_logger.LogDebug($"looking up cert for [{hostname}]");

            string certificatePath = Path.Combine(Environment.CurrentDirectory, "https", hostname, "certificate.crt");
            string privateKeyPath = Path.Combine(Environment.CurrentDirectory, "https", hostname, "private.key");

            //string rootPath = $"Identity.Web.https.{hostname}";
            //var assembly = this.GetType().Assembly;
            //Stream resource = assembly.GetManifestResourceStream($"{rootPath}");
            if (!File.Exists(certificatePath))
            {
                certificatePath = Path.Combine(Environment.CurrentDirectory, "https", hostname, "certificate.cer");
            }

            if (!File.Exists(certificatePath) || !File.Exists(privateKeyPath))
            {
                throw new Exception($"No certificate configured for {hostname}");
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
