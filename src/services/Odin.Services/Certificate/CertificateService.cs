using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Concurrency;
using Odin.Core.Storage.Database.System.Table;
using Odin.Services.Registry.Registration;

namespace Odin.Services.Certificate;

#nullable enable

// You can create me using ICertificateServiceFactory, if you prefer
public class CertificateService : ICertificateService
{
    private readonly ILogger<CertificateService> _logger;
    private readonly INodeLock _nodeLock;
    private readonly ICertificateStore _certificateStore;
    private readonly ICertesAcme _certesAcme;
    private readonly IDnsLookupService _dnsLookupService;
    private readonly AcmeAccountConfig _accountConfig;
    private readonly IServiceProvider _serviceProvider;
    private readonly string _accountKey;

    public CertificateService(
        ILogger<CertificateService> logger,
        INodeLock nodeLock,
        ICertificateStore certificateStore,
        ICertesAcme certesAcme,
        IDnsLookupService dnsLookupService,
        AcmeAccountConfig accountConfig,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _nodeLock = nodeLock;
        _certificateStore = certificateStore;
        _certesAcme = certesAcme;
        _dnsLookupService = dnsLookupService;
        _accountConfig = accountConfig;
        _serviceProvider = serviceProvider;

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
        await _nodeLock.LockAsync(LockKey(domain), cancellationToken: cancellationToken);

        var x509 = await GetCertificateAsync(domain);
        if (x509 != null)
        {
            _logger.LogDebug("Create certificate: {domain} completed on another thread", domain);
            return x509;
        }
        return await InternalCreateCertificateAsync(domain, sans, cancellationToken);
    }

    //

    public Task<bool> RenewIfAboutToExpireAsync(string domain, CancellationToken cancellationToken = default)
    {
        return RenewIfAboutToExpireAsync(domain, [], cancellationToken);
    }

    //

    public async Task<bool> RenewIfAboutToExpireAsync(string domain, string[] sans, CancellationToken cancellationToken = default)
    {
        var x509 = await GetCertificateAsync(domain);

        if (x509 != null && !AboutToExpire(x509))
        {
            return false;
        }

        await _nodeLock.LockAsync(LockKey(domain), cancellationToken: cancellationToken);

        x509 = await GetCertificateAsync(domain);

        if (x509 != null && !AboutToExpire(x509))
        {
            _logger.LogDebug("Background renew of certificate {domain} completed on another thread", domain);
            return false;
        }

        _logger.LogDebug("Beginning background renew of {domain} certificate", domain);
        x509 = await InternalCreateCertificateAsync(domain, sans, cancellationToken);
        if (x509 != null)
        {
            _logger.LogDebug("Completed background renew of {domain} certificate", domain);
            return true;
        }

        _logger.LogWarning("Could not renew {domain} certificate. See previous messages.", domain);
        return false;
    }

    //

    private async Task<X509Certificate2?> InternalCreateCertificateAsync(string domain, string[] sans, CancellationToken cancellationToken = default)
    {
        // Sanity
        if (domain.EndsWith(".dotyou.cloud"))
        {
            var error = $"Can't create certificate for {domain} because dotyou.cloud domains (should) resolve to 127.0.0.1. Did it expire?";
            _logger.LogError("{error}", error);
            await _certificateStore.StoreFailedCertificateUpdateAsync(domain, error);
            return null;
        }

        try
        {
            if (sans.Length > 0) // don't verify system domains (e.g. provisioning, admin, etc)
            {
                var (areDnsRecordsOk, _) = await _dnsLookupService.GetAuthoritativeDomainDnsStatusAsync(domain, cancellationToken);
                if (!areDnsRecordsOk)
                {
                    var error = $"Cannot create certificate for {domain}. One or more DNS records are no longer correct.";
                    _logger.LogWarning("{error}", error);
                    await _certificateStore.StoreFailedCertificateUpdateAsync(domain, error);
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
            var error = $"Error creating certificate for {domain}: {e.Message}";
            _logger.LogError("{error}", error);
            await _certificateStore.StoreFailedCertificateUpdateAsync(domain, error);
            return null;
        }
    }

    //

    private async Task<AcmeAccount?> LoadAccountAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var tableSettings = scope.ServiceProvider.GetRequiredService<TableSettings>();

        var settings = await tableSettings.GetAsync(_accountKey);
        return settings == null || string.IsNullOrEmpty(settings.value)
            ? null
            : new AcmeAccount { AccounKeyPem = settings.value };
    }

    //

    private async Task SaveAccountAsync(AcmeAccount account)
    {
        using var scope = _serviceProvider.CreateScope();
        var tableSettings = scope.ServiceProvider.GetRequiredService<TableSettings>();

        await tableSettings.UpsertAsync(new SettingsRecord
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

    private static NodeLockKey LockKey(string domain) => NodeLockKey.Create("CertificateServiceLock:" + domain);

    //

}