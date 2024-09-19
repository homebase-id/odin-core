using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Services.Registry;
using Odin.Services.Registry.Registration;

namespace Odin.Services.Certificate
{
    // You can create me using ICertificateServiceFactory, if you prefer
    public class CertificateService : ICertificateService
    {
        private readonly ILogger<CertificateService> _logger;
        private readonly ICertificateCache _certificateCache;
        private readonly ICertesAcme _certesAcme;
        private readonly IDnsLookupService _dnsLookupService;
        private readonly AcmeAccountConfig _accountConfig;
        private readonly string _sslRootPath;

        private static readonly ConcurrentDictionary<string, SemaphoreSlim> DomainSemaphores = new();
        
        public CertificateService(
            ILogger<CertificateService> logger,
            ICertificateCache certificateCache,
            ICertesAcme certesAcme,
            IDnsLookupService dnsLookupService,
            AcmeAccountConfig accountConfig,
            string sslRootPath)
        {
            _logger = logger;
            _certificateCache = certificateCache;
            _certesAcme = certesAcme;
            _dnsLookupService = dnsLookupService;
            _accountConfig = accountConfig;
            _sslRootPath = sslRootPath;
        }
        
        //

        public X509Certificate2 GetSslCertificate(string domain)
        {
            // Load from cache
            var cert = _certificateCache.LookupCertificate(domain);
            if (cert != null)
            {
                return cert;
            }
                
            // Not found? Load from disk, put in cache
            var (privateKeyPath, certificatePath) = GetCertificatePaths(_sslRootPath, domain);
            cert = _certificateCache.LoadCertificate(domain, privateKeyPath, certificatePath);

            return cert;
            
            // DO NOT TRY TO CREATE THE CERTIFICATE HERE!
        }
        
        //

        public X509Certificate2 ResolveCertificate(string domain)
        {
            return GetSslCertificate(domain);
        }
        
        //

        public async Task<X509Certificate2> CreateCertificate(string domain, string[] sans = null)
        {
            var mutex = DomainSemaphores.GetOrAdd(domain, _ => new SemaphoreSlim(1, 1));
            await mutex.WaitAsync();
            try
            {
                var x509 = ResolveCertificate(domain);
                if (x509 != null)
                {
                    _logger.LogDebug("Create certificate: {domain} completed on another thread", domain);
                    return x509;
                }
                return await InternalCreateCertificate(domain, sans);
            }
            finally
            {
                mutex.Release();
            }
        }
        
        //
        
        public async Task<bool> RenewIfAboutToExpire(IdentityRegistration idReg)
        {
            var domain = idReg.PrimaryDomainName;
            var mutex = DomainSemaphores.GetOrAdd(domain, _ => new SemaphoreSlim(1, 1));
            await mutex.WaitAsync();
            try
            {
                var x509 = ResolveCertificate(domain);
                if (x509 == null || AboutToExpire(x509))
                {
                    _logger.LogDebug("Beginning background renew of {domain} certificate", domain);
                    x509 = await InternalCreateCertificate(idReg.PrimaryDomainName, idReg.GetSans());
                    if (x509 != null)
                    {
                        _logger.LogDebug("Completed background renew of {domain} certificate", domain);
                        return true;
                    }
                    else
                    {
                        _logger.LogWarning("Could not renew {domain} certificate. See previous messages.", domain);
                        return false;
                    }
                }
                return false;
            }
            finally
            {
                mutex.Release();
            }
        }
        
        //

        private async Task<X509Certificate2> InternalCreateCertificate(string domain, string[] sans = null)
        {
            // Sanity
            if (domain.EndsWith(".dotyou.cloud"))
            {
                _logger.LogError(
                    "Can't create certificate for {domain} because it resolves to 127.0.0.1. Did it expire?", domain);
                return null;
            }

            try
            {
                if (sans != null) // don't verify system domains (e.g. provisioning, admin, etc)
                {
                    var (areDnsRecordsOk, _) = await _dnsLookupService.GetAuthoritativeDomainDnsStatus(domain);
                    if (!areDnsRecordsOk)
                    {
                        _logger.LogWarning(
                            "Cannot create certificate for {domain}. One or more DNS records are no longer correct.", domain);
                        return null;
                    }
                }

                var account = await LoadAccount();
                if (account == null)
                {
                    account = await _certesAcme.CreateAccount(_accountConfig.AcmeContactEmail);
                    await SaveAccount(account);
                }

                var domains = new List<string> { domain };
                if (sans != null)
                {
                    domains.AddRange(sans);
                }

                KeysAndCertificates pems = null;
                var maxTries = 10;
                while (pems == null)
                {
                    try
                    {
                        pems = await _certesAcme.CreateCertificate(account, domains.ToArray());
                    }
                    catch (OdinSystemException e)
                    {
                        if (--maxTries > 0)
                        {
                            _logger.LogWarning("{domain}: {error} (will retry)", domain, e.Message);
                            await Task.Delay(TimeSpan.FromSeconds(2));
                        }
                        else
                        {
                            throw;
                        }
                    }
                }

                await SaveSslCertificate(domain, pems);
                var x509 = ResolveCertificate(domain);
                if (x509 != null)
                {
                    return x509;
                }

                // Sanity - this should never happen
                throw new OdinSystemException(
                    "Certificate created and saved to disk. But I failed to load it. This makes no sense.");
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error creating certificate for {domain}: {ErrorText}", domain, e.Message);
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
        
        public async Task SaveSslCertificate(string domain, KeysAndCertificates pems)
        {
            var (privateKeyPath, certificatePath) = GetCertificatePaths(_sslRootPath, domain);
        
            _certificateCache.SaveToFile(domain, pems.PrivateKeyPem, pems.CertificatesPem, privateKeyPath, certificatePath);

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