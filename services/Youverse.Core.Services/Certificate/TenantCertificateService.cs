using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Certificate
{
    public class TenantCertificateService : ITenantCertificateService
    {
        private readonly ILogger<TenantCertificateService> _logger;
        private readonly ICertesAcme _certesAcme;
        private readonly AcmeAccountConfig _accountConfig;
        private readonly TenantContext _tenantContext;

        private static readonly ConcurrentDictionary<string, SemaphoreSlim> DomainSemaphores = new();
        
        public TenantCertificateService(
            ILogger<TenantCertificateService> logger, 
            ICertesAcme certesAcme,
            AcmeAccountConfig accountConfig,
            TenantContext tenantContext)
        {
            _logger = logger;
            _certesAcme = certesAcme;
            _accountConfig = accountConfig;
            _tenantContext = tenantContext;
        }
        
        //

        // SEB:TODO check if we need this
        public async Task<bool> AreAllCertificatesValid()
        {
            //TODO: this will scan across all domains for this identity.  for alpha = just use this domain
            var primaryDomainCert = GetPrimaryDomainCert();
            var paths = GetCertificatePaths(_tenantContext.SslRoot, primaryDomainCert.Domain);
            
            var cert = DotYouCertificateCache.LoadCertificate(paths.privateKeyPath, paths.certificatePath);
            return await Task.FromResult(!IsCertificateExpired(cert));
        }
        
        //

        // SEB:TODO check if we need this
        public X509Certificate2 GetSslCertificate(string domain)
        {
            var (privateKeyPath, certificatePath) = GetCertificatePaths(_tenantContext.SslRoot, domain);
            if (!File.Exists(certificatePath) || !File.Exists(privateKeyPath))
            {
                return null;
            }

            return DotYouCertificateCache.LoadCertificate(privateKeyPath, certificatePath);
        }
        
        //

        public X509Certificate2 ResolveCertificate(string domain)
        {
            //TODO: post-alpha should upgrade this to look at other alias's supported by this identity
            var primaryDomainCert = this.GetPrimaryDomainCert();

            if (primaryDomainCert.HasDomain(domain) == false)
            {
                return null;
            }

            var (privateKeyPath, certificatePath) = GetCertificatePaths(_tenantContext.SslRoot, primaryDomainCert.Domain);

            var cert = DotYouCertificateCache.LoadCertificate(privateKeyPath, certificatePath);
            if (cert == null)
            {
                return null;
            }
            
            return cert;
        }
        
        //

        // SEB:TODO check if we need this
        public bool IsCertificateExpired(X509Certificate2 cert)
        {
            if (cert == null)
            {
                return false;
            }

            var now = DateTime.Now;
            return !(now < cert.NotAfter && now > cert.NotBefore);
        }
        
        //

        // SEB:TODO check if we need this
        // public Task<List<IdentityCertificateDefinition>> GetIdentitiesRequiringNewCertificate(bool force)
        // {
        //     //TODO: this will scan across all domains for this identity.  for alpha = just use this domain
        //     var primaryDomainCert = GetPrimaryDomainCert();
        //     var paths = GetCertificatePaths(_tenantContext.SslRoot, primaryDomainCert.Domain);
        //
        //     if (force)
        //     {
        //         return Task.FromResult(new List<IdentityCertificateDefinition>() { primaryDomainCert });
        //     }
        //
        //     var cert = DotYouCertificateCache.LoadCertificate(paths.privateKeyPath, paths.certificatePath);
        //     if (IsCertificateExpired(cert))
        //     {
        //         return Task.FromResult(new List<IdentityCertificateDefinition>() { primaryDomainCert });
        //     }
        //
        //     var now = DateTime.Now;
        //     var expiring = cert.NotAfter;
        //     int daysBeforeExpiration = 10;
        //
        //     if (now > expiring.Subtract(TimeSpan.FromDays(daysBeforeExpiration)))
        //     {
        //         return Task.FromResult(new List<IdentityCertificateDefinition>() { primaryDomainCert });
        //     }
        //
        //     return Task.FromResult(new List<IdentityCertificateDefinition>());
        // }
        
        //

        public async Task<X509Certificate2> CreateCertificate(string domain)
        {
            var mutex = DomainSemaphores.GetOrAdd(domain, _ => new SemaphoreSlim(1, 1));
            await mutex.WaitAsync();
            try
            {
                var x509 = ResolveCertificate(domain);
                if (x509 != null)
                {
                    return x509;
                }
                return await InternalCreateCertificate(domain);
            }
            finally
            {
                mutex.Release();
            }
        }
        
        //
        
        public async Task<bool> RenewIfAboutToExpire(string domain)
        {
            var mutex = DomainSemaphores.GetOrAdd(domain, _ => new SemaphoreSlim(1, 1));
            await mutex.WaitAsync();
            try
            {
                var x509 = ResolveCertificate(domain);
                if (x509 == null || AboutToExpire(x509))
                {
                    _logger.LogDebug("Beginning background renew of {domain} certificate", domain);
                    x509 = await InternalCreateCertificate(domain);
                    _logger.LogDebug("Completed background renew of {domain} certificate", domain);
                    return x509 != null;
                }
                return false;
            }
            finally
            {
                mutex.Release();
            }
        }
        
        //

        private async Task<X509Certificate2> InternalCreateCertificate(string domain)
        {
            try
            {
                var account = await LoadAccount();
                if (account == null)
                {
                    account = await _certesAcme.CreateAccount(_accountConfig.AcmeContactEmail);
                    await SaveAccount(account);
                }
                
                var pems = await _certesAcme.CreateCertificate(account, new [] { domain });
                await SaveSslCertificate(_tenantContext.SslRoot, domain, pems);
                return X509Certificate2.CreateFromPem(pems.CertificatesPem, pems.PrivateKeyPem);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error creating certificate for {domain}: {ErrorText}", domain, e.Message);
                return null;
            }
        }

        //
        
        // SEB:TODO check if we need this
        private IdentityCertificateDefinition GetPrimaryDomainCert()
        {
            string domain = _tenantContext.HostOdinId;
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

        //
        
        private static  (string privateKeyPath, string certificatePath) GetCertificatePaths(string sslRoot, string domain)
        {
            var privateKey = Path.Combine(sslRoot, domain, "private.key");
            var certificate = Path.Combine(sslRoot, domain, "certificate.crt");
            return (privateKey, certificate);
        }

        //
        
        private string GetAcmeAccountFilePath()
        {
            return Path.Combine(_accountConfig.AcmeAccountFolder, _certesAcme.IsProduction 
                ? "letsencrypt-account-prod.pem" 
                : "letsencrypt-account-staging.pem");
        }
        
        //
        
        private async Task<AcmeAccount> LoadAccount()
        {
            var filePath = GetAcmeAccountFilePath();
            if (File.Exists(filePath))
            {
                var pem = await File.ReadAllTextAsync(filePath);
                return new AcmeAccount { AccounKeyPem = pem };
            }
            return null;
        }
        
        //
        
        private async Task SaveAccount(AcmeAccount account)
        {
            Directory.CreateDirectory(_accountConfig.AcmeAccountFolder);
            var filePath = GetAcmeAccountFilePath();
            await File.WriteAllTextAsync(filePath, account.AccounKeyPem);
        }
        
        //
        
        public static async Task SaveSslCertificate(string sslRoot, string domain, KeysAndCertificates pems)
        {
            var (privateKeyPath, certificatePath) = GetCertificatePaths(sslRoot, domain);
        
            DotYouCertificateCache.SaveToFile(pems.PrivateKeyPem, pems.CertificatesPem, privateKeyPath, certificatePath);

            await Task.CompletedTask;
        }
        
        //

        private static bool AboutToExpire(X509Certificate2 certificate)
        {
            return DateTime.Now + TimeSpan.FromDays(7) > certificate.NotAfter;
        }
        
        //
        
    }
}