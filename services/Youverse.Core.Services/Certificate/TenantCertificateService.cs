using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Registry;

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

        public X509Certificate2 GetSslCertificate(string domain)
        {
            // Load from cache
            var cert = DotYouCertificateCache.LookupCertificate(domain);
            if (cert != null)
            {
                return cert;
            }
                
            // Not found? Load from disk, put in cache
            var (privateKeyPath, certificatePath) = GetCertificatePaths(_tenantContext.SslRoot, domain);
            cert = DotYouCertificateCache.LoadCertificate(domain, privateKeyPath, certificatePath);

            return cert;
            
            // DO NOT TRY TO CREATE THE CERTIFICATE HERE!
        }
        
        //

        public X509Certificate2 ResolveCertificate(IdentityRegistration idReg)
        {
            return GetSslCertificate(idReg.PrimaryDomainName);
        }
        
        //

        public async Task<X509Certificate2> CreateCertificate(IdentityRegistration idReg)
        {
            var mutex = DomainSemaphores.GetOrAdd(idReg.PrimaryDomainName, _ => new SemaphoreSlim(1, 1));
            await mutex.WaitAsync();
            try
            {
                var x509 = ResolveCertificate(idReg);
                if (x509 != null)
                {
                    return x509;
                }
                return await InternalCreateCertificate(idReg);
            }
            finally
            {
                mutex.Release();
            }
        }
        
        //
        
        public async Task<bool> RenewIfAboutToExpire(IdentityRegistration idReg)
        {
            var mutex = DomainSemaphores.GetOrAdd(idReg.PrimaryDomainName, _ => new SemaphoreSlim(1, 1));
            await mutex.WaitAsync();
            try
            {
                var x509 = ResolveCertificate(idReg);
                if (x509 == null || AboutToExpire(x509))
                {
                    _logger.LogDebug("Beginning background renew of {domain} certificate", idReg.PrimaryDomainName);
                    x509 = await InternalCreateCertificate(idReg);
                    _logger.LogDebug("Completed background renew of {domain} certificate", idReg.PrimaryDomainName);
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

        private async Task<X509Certificate2> InternalCreateCertificate(IdentityRegistration idReg)
        {
            try
            {
                var account = await LoadAccount();
                if (account == null)
                {
                    account = await _certesAcme.CreateAccount(_accountConfig.AcmeContactEmail);
                    await SaveAccount(account);
                }

                var pems = await _certesAcme.CreateCertificate(account, idReg.GetDomains());
                await SaveSslCertificate(_tenantContext.SslRoot, idReg.PrimaryDomainName, pems);
                
                var x509 = ResolveCertificate(idReg);
                if (x509 != null)
                {
                    return x509;
                }

                // Sanity - this should never happen
                throw new YouverseSystemException(
                    "Certificate created and saved to disk. But I failed to load it. This makes no sense.");
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error creating certificate for {domain}: {ErrorText}", idReg.PrimaryDomainName, e.Message);
                return null;
            }
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
        
            DotYouCertificateCache.SaveToFile(domain, pems.PrivateKeyPem, pems.CertificatesPem, privateKeyPath, certificatePath);

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