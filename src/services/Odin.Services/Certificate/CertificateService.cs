using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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

    //
    // A domain that just failed to issue is not going to succeed on the next TLS handshake a
    // few milliseconds later. Without this, every inbound connection to a domain that cannot
    // get a certificate starts its own ACME order - which both queues behind the node lock and
    // spends the CA's per-hostname failed-authorization allowance, turning a recoverable
    // condition into an hour-long outage.
    //
    // Node-local on purpose: the node lock already stops two nodes ordering at once, so the
    // worst case is one wasted attempt per node per window, and losing the state on restart is
    // the right behaviour for an operator who has just fixed DNS and bounced the service.
    //
    private static readonly TimeSpan FailureBackoff = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _backoffUntil = new(StringComparer.OrdinalIgnoreCase);

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

    /// <summary>
    /// Issues a certificate for <paramref name="domain"/> if one can be issued right now.
    /// Returns null - promptly - if it cannot.
    /// </summary>
    /// <remarks>
    /// This runs on the TLS handshake path, so it never waits: not for the node lock, and not
    /// for a domain that is in failure backoff. Waiting would buy nothing. If another thread or
    /// node holds the lock, the order is already being placed and this connection still has no
    /// certificate to serve; blocking here only converts one un-issuable domain into a
    /// handshake stall for every connection to it.
    /// </remarks>
    public async Task<X509Certificate2?> CreateCertificateAsync(
        string domain,
        string[] sans,
        CancellationToken cancellationToken = default)
    {
        if (IsInFailureBackoff(domain, out var backoffRemaining))
        {
            _logger.LogDebug(
                "Not creating certificate for {domain}: a previous attempt failed, next attempt in {backoff}s",
                domain, (int)backoffRemaining.TotalSeconds);
            return null;
        }

        await using var handle = await _nodeLock.TryLockAsync(LockKey(domain), cancellationToken: cancellationToken);
        if (handle == null)
        {
            // Contention, not a fault. Somebody else is ordering for this domain right now.
            _logger.LogDebug(
                "Not creating certificate for {domain}: an order is already in progress on another thread or node",
                domain);
            return null;
        }

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

    /// <summary>
    /// Background path. Unlike <see cref="CreateCertificateAsync(string,string[],CancellationToken)"/>
    /// this deliberately ignores the failure backoff: the background loop's own interval is its
    /// rate limiter, and it is the thing that eventually heals a domain whose DNS has since been
    /// fixed. It is not on any request path, so nobody is waiting on it.
    /// </summary>
    public async Task<bool> RenewIfAboutToExpireAsync(string domain, string[] sans, CancellationToken cancellationToken = default)
    {
        var x509 = await GetCertificateAsync(domain);

        if (x509 != null && !await NeedsRenewalAsync(domain, x509, sans, cancellationToken))
        {
            return false;
        }

        // Also non-blocking, for a different reason than the create path: whoever holds this
        // lock is placing an order for this same domain, so waiting for them only risks a
        // lock timeout and an alarming-looking error. This loop comes around again.
        await using var handle = await _nodeLock.TryLockAsync(LockKey(domain), cancellationToken: cancellationToken);
        if (handle == null)
        {
            _logger.LogDebug(
                "Skipping renew of {domain} certificate: an order is already in progress on another thread or node",
                domain);
            return false;
        }

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

    //

    private async Task<X509Certificate2?> InternalCreateCertificateAsync(string domain, string[] sans, CancellationToken cancellationToken = default)
    {
        // Sanity
        if (domain.EndsWith(".dotyou.cloud"))
        {
            var error = $"Can't create certificate for {domain} because dotyou.cloud domains (should) resolve to 127.0.0.1. Did it expire?";
            _logger.LogError("{error}", error);
            await NoteFailureAsync(domain, error, FailureBackoff);
            return null;
        }

        try
        {
            if (sans.Length > 0) // don't verify system domains (e.g. provisioning, admin, etc)
            {
                var (areDnsRecordsOk, dnsConfigs) = await _dnsLookupService.GetAuthoritativeDomainDnsStatusAsync(new AsciiDomainName(domain), cancellationToken: cancellationToken);
                if (!areDnsRecordsOk)
                {
                    var error = $"Cannot create certificate for {domain}. One or more DNS records are incorrect.";
                    _logger.LogWarning("{error}", error);
                    await NoteFailureAsync(domain, error, FailureBackoff);
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
            var sw = Stopwatch.StartNew();
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
                catch (AcmeOrderException)
                {
                    // The CA has already made up its mind about this order. Every retry here is a
                    // brand new order with brand new authorizations, and Let's Encrypt counts
                    // failed authorizations against a per-hostname hourly allowance - so retrying
                    // in a tight loop is exactly how a transient DNS lag becomes an hour of
                    // rate-limited failures. Give up now and let the backoff hold us off.
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
            _backoffUntil.TryRemove(domain, out _);
            _logger.LogInformation("Created certificate for {domain} in {elapsed}s", domain, sw.ElapsedMilliseconds / 1000.0);
            return x509;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            var error = $"Error creating certificate for {domain}: {e.Message.ReplaceLineEndings(". ").TrimEnd()}";
            _logger.LogError("{error}", error);

            // When the CA tells us how long it will keep saying no, believe it.
            var backoff = e is AcmeRateLimitedException rateLimited ? rateLimited.RetryAfter : FailureBackoff;
            await NoteFailureAsync(domain, error, backoff);
            return null;
        }
    }

    //

    private bool IsInFailureBackoff(string domain, out TimeSpan remaining)
    {
        remaining = TimeSpan.Zero;

        if (!_backoffUntil.TryGetValue(domain, out var until))
        {
            return false;
        }

        remaining = until - DateTimeOffset.UtcNow;
        if (remaining > TimeSpan.Zero)
        {
            return true;
        }

        _backoffUntil.TryRemove(domain, out _);
        remaining = TimeSpan.Zero;
        return false;
    }

    //

    private async Task NoteFailureAsync(string domain, string error, TimeSpan backoff)
    {
        _backoffUntil[domain] = DateTimeOffset.UtcNow + backoff;
        _logger.LogWarning(
            "Certificate order for {domain} failed; not trying again for {backoff}s. Reason: {error}",
            domain, (int)backoff.TotalSeconds, error);

        try
        {
            await _certificateStore.StoreFailedCertificateUpdateAsync(domain, error);
        }
        catch (Exception e)
        {
            // Recording the failure must not itself become a failure on the handshake path
            _logger.LogError(e, "Could not record failed certificate update for {domain}: {error}", domain, e.Message);
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
            new AsciiDomainName(domain), cancellationToken: cancellationToken);
        return dnsConfigs.Any(x =>
            x.Optional && x.Name == DnsConfigurationSet.PrefixMtaSts && x.Status == DnsLookupRecordStatus.Success);
    }

    //

    private static NodeLockKey LockKey(string domain) => NodeLockKey.Create("CertificateServiceLock:" + domain);

    //

}