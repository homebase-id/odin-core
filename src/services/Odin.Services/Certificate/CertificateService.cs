using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Odin.Core.Storage.Concurrency;
using Odin.Core.Util;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.X509;
using Odin.Services.Configuration;
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
    private readonly OdinConfiguration _configuration;
    private readonly string _accountKey;

    public CertificateService(
        ILogger<CertificateService> logger,
        INodeLock nodeLock,
        ICertificateStore certificateStore,
        ICertesAcme certesAcme,
        IDnsLookupService dnsLookupService,
        AcmeAccountConfig accountConfig,
        IServiceProvider serviceProvider,
        OdinConfiguration configuration)
    {
        _logger = logger;
        _nodeLock = nodeLock;
        _certificateStore = certificateStore;
        _certesAcme = certesAcme;
        _dnsLookupService = dnsLookupService;
        _accountConfig = accountConfig;
        _serviceProvider = serviceProvider;
        _configuration = configuration;

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
        await using (await _nodeLock.LockAsync(LockKey(domain), cancellationToken: cancellationToken))
        {
            var x509 = await GetCertificateAsync(domain);
            if (x509 != null)
            {
                _logger.LogDebug("Create certificate: {domain} completed on another thread", domain);
                return x509;
            }
            return await InternalCreateCertificateAsync(domain, sans, cancellationToken);
        }
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

        if (x509 != null && !await NeedsRenewalAsync(domain, x509, sans, cancellationToken))
        {
            return false;
        }

        await using (await _nodeLock.LockAsync(LockKey(domain), cancellationToken: cancellationToken))
        {
            x509 = await GetCertificateAsync(domain);

            if (x509 != null && !await NeedsRenewalAsync(domain, x509, sans, cancellationToken))
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

            _logger.LogWarning("Could not RENEW {domain} certificate. See previous messages.", domain);
            return false;
        }
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
                var (areDnsRecordsOk, dnsConfigs) = await _dnsLookupService.GetAuthoritativeDomainDnsStatusAsync(new AsciiDomainName(domain), cancellationToken);
                if (!areDnsRecordsOk)
                {
                    var error = $"Cannot create certificate for {domain}. One or more DNS records are incorrect.";
                    _logger.LogWarning("{error}", error);
                    await _certificateStore.StoreFailedCertificateUpdateAsync(domain, error);
                    return null;
                }

                // Optional SANs (mta-sts) join the certificate only when their DNS record
                // actually resolves: the CA fails the WHOLE order if one name cannot
                // validate, and manual-records tenants may not have created the record
                var optionalSans = dnsConfigs
                    .Where(x => x.Optional && x.Type == "CNAME" && x.Status == DnsLookupRecordStatus.Success)
                    .Select(x => x.Domain)
                    .Where(x => !sans.Contains(x, StringComparer.OrdinalIgnoreCase))
                    .ToArray();
                if (optionalSans.Length > 0)
                {
                    sans = [..sans, ..optionalSans];
                }
            }

            var account = await LoadAccountAsync();
            if (account == null)
            {
                await using (await _nodeLock.LockAsync(LockKey("CertesAccountLock"), cancellationToken: cancellationToken))
                {
                    account = await LoadAccountAsync();
                    if (account == null)
                    {
                        account = await _certesAcme.CreateAccountAsync(_accountConfig.AcmeContactEmail, cancellationToken);
                    }
                    await SaveAccountAsync(account);
                }
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
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
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
            var error = $"Error creating certificate for {domain}: {e.Message.ReplaceLineEndings(". ").TrimEnd()}";
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

    /// <summary>
    /// Expiry is the normal trigger. Additionally, a tenant certificate missing the
    /// mta-sts SAN renews early once the record resolves, so existing certificates pick
    /// the SAN up promptly after the email era begins instead of waiting out their 90
    /// days. Steady-state cost is zero: once the SAN is on the certificate (or while
    /// tenant mail is disabled) no DNS is touched, and the DNS-resolves gate prevents a
    /// renew loop for manual-records tenants that never created the record.
    /// </summary>
    // internal for testing
    internal async Task<bool> NeedsRenewalAsync(
        string domain, X509Certificate2 certificate, string[] sans, CancellationToken cancellationToken)
    {
        if (AboutToExpire(certificate))
        {
            return true;
        }

        // sans.Length == 0 = system domain (provisioning, admin): never carries optional SANs
        if (sans.Length == 0 || !_configuration.Email.TenantMail.Enabled)
        {
            return false;
        }

        var mtaStsDomain = $"{DnsConfigurationSet.PrefixMtaSts}.{domain}";
        if (certificate.GetSubjectAlternativeNames().Contains(mtaStsDomain, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var (_, dnsConfigs) = await _dnsLookupService.GetAuthoritativeDomainDnsStatusAsync(
            new AsciiDomainName(domain), cancellationToken);
        return dnsConfigs.Any(x =>
            x.Optional && x.Name == DnsConfigurationSet.PrefixMtaSts && x.Status == DnsLookupRecordStatus.Success);
    }

    //

    private static NodeLockKey LockKey(string domain) => NodeLockKey.Create("CertificateServiceLock:" + domain);

    //

}