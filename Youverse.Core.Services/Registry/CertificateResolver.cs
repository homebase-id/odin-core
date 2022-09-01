using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Youverse.Core.Cryptography;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Registry
{
    public class CertificateResolver : ICertificateResolver
    {
        private readonly TenantContext _tenantContext;

        public CertificateResolver(TenantContext tenantContext)
        {
            _tenantContext = tenantContext;
        }

        public X509Certificate2 GetSslCertificate()
        {
            Guid domainId = CalculateDomainId(_tenantContext.HostDotYouId);
            string certificatePath = Path.Combine(_tenantContext.DataRoot, "ssl", domainId.ToString(), "certificate.crt");
            string privateKeyPath = Path.Combine(_tenantContext.DataRoot, "ssl", domainId.ToString(), "private.key");
            return LoadCertificate(certificatePath, privateKeyPath);
        }

        public CertificateLocation GetSigningCertificate()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Loads and returns a certificate for the given dotYouId
        /// </summary>
        /// <param name="rootPath"></param>
        /// <param name="registryId"></param>
        /// <param name="dotYouId"></param>
        /// <returns></returns>
        public static X509Certificate2 GetSslCertificate(string rootPath, Guid registryId, DotYouIdentity dotYouId)
        {
            Guid domainId = CalculateDomainId(dotYouId);
            string certificatePath = Path.Combine(rootPath, registryId.ToString(), "ssl", domainId.ToString(), "certificate.crt");
            string privateKeyPath = Path.Combine(rootPath, registryId.ToString(), "ssl", domainId.ToString(), "private.key");
            return LoadCertificate(certificatePath, privateKeyPath);
        }

        private static X509Certificate2 LoadCertificate(string publicKeyPath, string privateKeyPath)
        {
            if (File.Exists(publicKeyPath) == false || File.Exists(privateKeyPath) == false)
            {
                throw new Exception("Cannot find certificate or key file(s)");
            }

            using (X509Certificate2 publicKey = new X509Certificate2(publicKeyPath))
            {
                string encodedKey = File.ReadAllText(privateKeyPath);
                RSA rsaPrivateKey;
                using (rsaPrivateKey = RSA.Create())
                {
                    rsaPrivateKey.ImportFromPem(encodedKey.ToCharArray());

                    return publicKey.CopyWithPrivateKey(rsaPrivateKey);

                    //// Disabled this part as it causes too many changes within Keychain causing Chrome to not open the Page:
                    // using (X509Certificate2 pubPrivEphemeral = publicKey.CopyWithPrivateKey(rsaPrivateKey))
                    // {
                    //     // Export as PFX and re-import if you want "normal PFX private key lifetime"
                    //     // (this step is currently required for SslStream, but not for most other things
                    //     // using certificates)
                    //     return new X509Certificate2(pubPrivEphemeral.Export(X509ContentType.Pfx));
                    // }
                }
            }
        }

        public static Guid CalculateDomainId(DotYouIdentity input)
        {
            var adjustedInput = input.ToString().ToLower();
            using SHA256 hashAlgo = SHA256.Create();
            byte[] bytes = hashAlgo.ComputeHash(Encoding.UTF8.GetBytes(adjustedInput));
            var half = bytes.Length / 2;
            var (part1, part2) = ByteArrayUtil.Split(bytes, half, half);
            var reducedBytes = ByteArrayUtil.EquiByteArrayXor(part1, part2);

            return new Guid(reducedBytes);
        }
    }
}
