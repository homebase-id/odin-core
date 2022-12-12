using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Registry;

namespace Youverse.Core.Services.Certificate
{
    // public static class CertificateCache
    // {
    //     
    // }
    public class TenantCertificateService : ITenantCertificateService
    {
        private readonly TenantContext _tenantContext;

        public TenantCertificateService(TenantContext tenantContext)
        {
            _tenantContext = tenantContext;
        }

        public async Task<bool> AreAllCertificatesValid()
        {
            //TODO: this will scan across all domains for this identity.  for alpha = just use this domain
            var primaryDomainCert = GetPrimaryDomainCert();
            var paths = this.GetCertificatePaths(primaryDomainCert.Domain);
            
            var cert = DotYouCertificateLoader.LoadCertificate(paths.publicKeyPath, paths.privateKeyPath);
            return !IsCertificateExpired(cert);
        }

        public X509Certificate2 GetSslCertificate(string domain)
        {
            var (certificatePath, privateKeyPath, _) = this.GetCertificatePaths(domain);
            if (!File.Exists(certificatePath) || !File.Exists(privateKeyPath))
            {
                return null;
            }

            return DotYouCertificateLoader.LoadCertificate(certificatePath, privateKeyPath);
        }

        public X509Certificate2 ResolveCertificate(string domain)
        {
            //TODO: post-alpha should upgrade this to look at other alias's supported by this identity
            var primaryDomainCert = this.GetPrimaryDomainCert();

            if (primaryDomainCert.HasDomain(domain) == false)
            {
                return null;
            }

            var (certificatePath, privateKeyPath, _) = this.GetCertificatePaths(primaryDomainCert.Domain);
            if (!File.Exists(certificatePath) || !File.Exists(privateKeyPath))
            {
                return null;
            }

            var cert = DotYouCertificateLoader.LoadCertificate(certificatePath, privateKeyPath);
            if (!IsCertificateExpired(cert))
            {
                return cert;
            }

            return null;
        }

        public async Task SaveSslCertificate(Guid registryId, string domain, CertificatePemContent content)
        {
            var (certificatePath, privateKeyPath, fullChainPath) = this.GetCertificatePaths(domain);

            this.EnsurePath(certificatePath);
            this.EnsurePath(privateKeyPath);
            this.EnsurePath(fullChainPath);

            await File.WriteAllTextAsync(certificatePath, content.PublicKeyCertificate);
            await File.WriteAllTextAsync(privateKeyPath, content.PrivateKey);
            await File.WriteAllTextAsync(fullChainPath, content.FullChain);
        }

        private void EnsurePath(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "");
        }

        public bool IsCertificateExpired(X509Certificate2 cert)
        {
            if (cert == null)
            {
                return false;
            }

            var now = DateTime.Now;
            return !(now < cert.NotAfter && now > cert.NotBefore);
        }

        public Task<List<IdentityCertificateDefinition>> GetIdentitiesRequiringNewCertificate(bool force)
        {
            //TODO: this will scan across all domains for this identity.  for alpha = just use this domain
            var primaryDomainCert = GetPrimaryDomainCert();
            var paths = this.GetCertificatePaths(primaryDomainCert.Domain);

            if (force)
            {
                return Task.FromResult(new List<IdentityCertificateDefinition>() { primaryDomainCert });
            }

            var cert = DotYouCertificateLoader.LoadCertificate(paths.publicKeyPath, paths.privateKeyPath);
            if (IsCertificateExpired(cert))
            {
                return Task.FromResult(new List<IdentityCertificateDefinition>() { primaryDomainCert });
            }

            var now = DateTime.Now;
            var expiring = cert.NotAfter;
            int daysBeforeExpiration = 10;

            if (now > expiring.Subtract(TimeSpan.FromDays(daysBeforeExpiration)))
            {
                return Task.FromResult(new List<IdentityCertificateDefinition>() { primaryDomainCert });
            }

            return Task.FromResult(new List<IdentityCertificateDefinition>());
        }

        private IdentityCertificateDefinition GetPrimaryDomainCert()
        {
            string domain = _tenantContext.HostDotYouId;
            return new IdentityCertificateDefinition()
            {
                Domain = domain,
                AlternativeNames = new List<string>()
                {
                    $"www.{domain}",
                    $"api.{domain}"
                }
            };
        }


        private (string publicKeyPath, string privateKeyPath, string fullChainPath) GetCertificatePaths(string domain)
        {
            string root = _tenantContext.SslRoot;
            string publicKey = Path.Combine(root, domain, "certificate.crt");
            string privateKey = Path.Combine(root, domain, "private.key");
            string fullchain = Path.Combine(root, domain, "fullchain.crt");
            return (publicKey, privateKey, fullchain);
        }
    }
}