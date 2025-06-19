using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.Database.System.Table;
using Odin.Services.Registry;
using Odin.Services.Registry.Registration;

namespace Odin.Services.Certificate;

#nullable enable

// You can create me using ICertificateServiceFactory, if you prefer
public class CertificateService : ICertificateService
{
    private readonly ILogger<CertificateService> _logger;
    private readonly ICertificateStore _certificateStore;
    private readonly ICertesAcme _certesAcme;
    private readonly IDnsLookupService _dnsLookupService;
    private readonly AcmeAccountConfig _accountConfig;
    private readonly SystemDatabase _systemDatabase;
    private readonly string _accountKey;

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> DomainSemaphores = new();

    public CertificateService(
        ILogger<CertificateService> logger,
        ICertificateStore certificateStore,
        ICertesAcme certesAcme,
        IDnsLookupService dnsLookupService,
        AcmeAccountConfig accountConfig,
        SystemDatabase systemDatabase)
    {
        _logger = logger;
        _certificateStore = certificateStore;
        _certesAcme = certesAcme;
        _dnsLookupService = dnsLookupService;
        _accountConfig = accountConfig;
        _systemDatabase = systemDatabase;

        _accountKey = _certesAcme.IsProduction ?
            "acme-account-prod-pem" :
            "acme-account-staging-pem";
    }

    //

    public Task<X509Certificate2?> GetCertificateAsync(string domain)
    {
        return _certificateStore.GetCertificateAsync(domain);
    }

    //

    public Task<X509Certificate2> PutCertificateAsync(string domain, KeysAndCertificates pems)
    {
        return _certificateStore.PutCertificateAsync(domain, pems.PrivateKeyPem, pems.CertificatesPem);
    }

    //

    public Task<X509Certificate2?> CreateCertificateAsync(string domain, CancellationToken cancellationToken = default)
    {
        return CreateCertificateAsync(domain, [], cancellationToken);
    }

    //

    public async Task<X509Certificate2?> CreateCertificateAsync(
        string domain,
        string[] sans,
        CancellationToken cancellationToken = default)
    {
        var mutex = DomainSemaphores.GetOrAdd(domain, _ => new SemaphoreSlim(1, 1));
        await mutex.WaitAsync(cancellationToken);
        try
        {
            var x509 = await GetCertificateAsync(domain);
            if (x509 != null)
            {
                _logger.LogDebug("Create certificate: {domain} completed on another thread", domain);
                return x509;
            }
            return await InternalCreateCertificate(domain, sans, cancellationToken);
        }
        finally
        {
            mutex.Release();
        }
    }


    //

    public async Task<bool> RenewIfAboutToExpireAsync(IdentityRegistration idReg, CancellationToken cancellationToken = default)
    {
        var domain = idReg.PrimaryDomainName;
        var mutex = DomainSemaphores.GetOrAdd(domain, _ => new SemaphoreSlim(1, 1));
        await mutex.WaitAsync(cancellationToken);
        try
        {
            var x509 = await GetCertificateAsync(domain);
            if (x509 == null || AboutToExpire(x509))
            {
                _logger.LogDebug("Beginning background renew of {domain} certificate", domain);
                x509 = await InternalCreateCertificate(idReg.PrimaryDomainName, idReg.GetSans(), cancellationToken);
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

    private async Task<X509Certificate2?> InternalCreateCertificate(string domain, string[] sans, CancellationToken cancellationToken = default)
    {
        // Sanity
        if (domain.EndsWith(".dotyou.cloud"))
        {
            _logger.LogError(
                "Can't create certificate for {domain} because dotyou.cloud domains (should) resolve to 127.0.0.1. Did it expire?", domain);
            return null;
        }

        try
        {
            if (sans.Length > 0) // don't verify system domains (e.g. provisioning, admin, etc)
            {
                var (areDnsRecordsOk, _) = await _dnsLookupService.GetAuthoritativeDomainDnsStatusAsync(domain, cancellationToken);
                if (!areDnsRecordsOk)
                {
                    _logger.LogWarning(
                        "Cannot create certificate for {domain}. One or more DNS records are no longer correct.", domain);
                    return null;
                }
            }

            var account = await LoadAccountAsync();
            if (account == null)
            {
                account = await _certesAcme.CreateAccountAsync(_accountConfig.AcmeContactEmail, cancellationToken);
                await SaveAccountAsync(account);
            }

            var domains = new List<string> { domain };
            if (sans.Length > 0)
            {
                domains.AddRange(sans);
            }

            KeysAndCertificates? pems = null;
            var maxTries = 10;
            while (pems == null)
            {
                try
                {
                    pems = await _certesAcme.CreateCertificateAsync(account, domains.ToArray(), cancellationToken);
                }
                catch (OdinSystemException e)
                {
                    if (--maxTries > 0)
                    {
                        _logger.LogWarning("{domain}: {error} (will retry)", domain, e.Message);
                        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    }
                    else
                    {
                        throw;
                    }
                }
            }

            var x509 = await _certificateStore.PutCertificateAsync(domain, pems.PrivateKeyPem, pems.CertificatesPem);
            return x509;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error creating certificate for {domain}: {ErrorText}", domain, e.Message);
            return null;
        }
    }

    //

    private async Task<AcmeAccount?> LoadAccountAsync()
    {
        var settings = await _systemDatabase.Settings.GetAsync(_accountKey);
        return settings == null || string.IsNullOrEmpty(settings.value)
            ? null
            : new AcmeAccount { AccounKeyPem = settings.value };
    }

    //

    private async Task SaveAccountAsync(AcmeAccount account)
    {
        await _systemDatabase.Settings.UpsertAsync(new SettingsRecord
        {
            key = _accountKey,
            value = account.AccounKeyPem,
        });
    }

    //

    private static bool AboutToExpire(X509Certificate2 certificate)
    {
        return DateTime.Now + TimeSpan.FromDays(7) > certificate.NotAfter;
    }

    //

}