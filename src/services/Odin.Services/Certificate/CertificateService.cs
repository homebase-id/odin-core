using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
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
using Odin.Services.Background;
using Odin.Services.Background.BackgroundServices.System;
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
    private readonly IBackgroundServiceNotifier<UpdateCertificatesBackgroundService> _issuanceNotifier;
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
    // Exponential: 5m, 10m, 20m, 40m, then capped at an hour. A flat 5 minutes is twelve
    // attempts an hour against a Let's Encrypt allowance of five failed authorizations per
    // hostname per hour - it would breach the very limit it exists to protect.
    // A transient failure - a stale nonce, a server-side blip, a network error, a DNS record
    // that is not right yet - says nothing about the CA's opinion of this domain and never spent
    // its allowance. It gets its own, much shorter schedule: 30s doubling to a 5-minute cap.
    // It still escalates, so a CA that is down for an hour is not asked sixty times.
    private static readonly TimeSpan InitialTransientBackoff = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxTransientBackoff = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan InitialFailureBackoff = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaxFailureBackoff = TimeSpan.FromHours(1);
    private readonly ConcurrentDictionary<string, BackoffState> _backoff = new(StringComparer.OrdinalIgnoreCase);

    private sealed record BackoffState(DateTimeOffset Until, int ConsecutiveFailures);

    //
    // When the CA refuses an optional SAN we drop it and issue without it. That leaves a
    // certificate which is, by NeedsRenewalAsync's reckoning, missing a SAN it ought to have -
    // so the next sweep would renew to re-add it, fail, drop it again, and issue yet another
    // certificate. Every 12h sweep would mint a duplicate, and Let's Encrypt allows five
    // duplicate certificates (identical name set) per week. Seven days holds that to one.
    //
    // Node-local and lost on restart, like the backoff: an operator who has just fixed the
    // record and bounced the service gets an immediate retry, which is the behaviour they want.
    //
    // One pulse per domain per minute. The pulse wakes a whole-registry sweep - a registry read,
    // a Redis lock attempt per domain, and a DNS lookup per tenant still missing its mta-sts SAN
    // - and SleepAsync returns immediately when the wake event is already set, so unthrottled
    // pulses run sweeps back to back.
    //
    // NOT an attacker control: ServerCertificateSelector only reaches RequestIssuanceAsync after
    // ResolveIdentityRegistration or IsKnownSystemDomain succeeds, so the reachable set is
    // domains this host already serves that currently have no certificate. This is a bound on
    // ordinary traffic to a newly provisioned identity, not a defence.
    // A hard floor between sweeps, for the whole host. One pulse wakes a whole-registry sweep -
    // a registry read, a certificate-store read per tenant, and a DNS lookup per tenant still
    // missing its mta-sts SAN - and SleepAsync returns immediately when the wake event is
    // already set, so unthrottled pulses run sweeps back to back. A per-domain throttle was
    // tried alongside this and removed: it bounds one domain while still admitting one pulse per
    // domain per minute, so this single global bound is the one that actually holds.
    private static readonly TimeSpan GlobalPulseFloor = TimeSpan.FromSeconds(10);
    private long _lastGlobalPulseTicks;

    // A backoff window that ended this long ago is forgotten entirely, so a domain that fails
    // once every few months is not escalated to the hour cap forever, and entries for domains
    // that have gone away do not accumulate.
    private static readonly TimeSpan BackoffForgottenAfter = TimeSpan.FromHours(6);

    // How long to wait for another worker's order before giving up and letting the next sweep
    // handle it. Must be less than the lock's forced release - RedisLock's 10-minute default,
    // which comfortably outlives one order now that the authorization poll stops on a terminal
    // status. RedisLock enforces the inequality.
    private static readonly TimeSpan OrderLockTimeout = TimeSpan.FromSeconds(30);

    public CertificateService(
        ILogger<CertificateService> logger,
        INodeLock nodeLock,
        ICertificateStore certificateStore,
        ICertesAcme certesAcme,
        IDnsLookupService dnsLookupService,
        AcmeAccountConfig accountConfig,
        IServiceProvider serviceProvider,
        OdinConfiguration configuration,
        IBackgroundServiceNotifier<UpdateCertificatesBackgroundService> issuanceNotifier)
    {
        _issuanceNotifier = issuanceNotifier;
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
    /// Asks for a certificate to be issued for <paramref name="domain"/>, out of band.
    /// Returns immediately. The caller gets no certificate back from this call.
    /// </summary>
    /// <remarks>
    /// This is what the TLS handshake path wants. Placing the order there instead is what the
    /// 2026-09-08 incident was: an order runs for minutes, the handshake deadline is 60s, so the
    /// connection that won the lock stalled for 60s and was then killed mid-order - having
    /// already asked the CA to validate, and so having spent the same rate-limit allowance as a
    /// completed order while producing nothing. See docs/certificate-issuance-locking.md.
    /// </remarks>
    public Task<bool> RequestIssuanceAsync(string domain)
    {
        if (IsInFailureBackoff(domain, out var remaining))
        {
            _logger.LogDebug(
                "Not requesting issuance for {domain}: a previous attempt failed, next attempt in {backoff}s",
                domain, (int)remaining.TotalSeconds);
            return Task.FromResult(false);
        }

        if (!ShouldPulse())
        {
            _logger.LogDebug("Not requesting issuance for {domain}: the issuer was pulsed within the last {floor}s",
                domain, (int)GlobalPulseFloor.TotalSeconds);
            return Task.FromResult(false);
        }

        //
        // Dispatched and deliberately not awaited. NotifyWorkAvailableAsync waits up to 30
        // seconds for the background service to appear and then throws if it never does - which
        // is what happens when SystemBackgroundServicesEnabled is false. Awaiting it here would
        // put a 30-second stall back on the TLS handshake path: precisely the fault this method
        // exists to remove.
        //
        // NOTE there is no backstop if the pulse cannot be delivered. The background issuer is
        // the ONLY thing that orders certificates now, so when it is not running no certificate
        // is ever obtained. That is why the failure below is logged as a warning, and why
        // Startup logs a warning when system background services are disabled. Startup does not
        // refuse to start: hosts that serve pre-provisioned certificates legitimately run with
        // them off.
        //
        _logger.LogDebug("Requesting out-of-band certificate issuance for {domain}", domain);
        _ = PulseIssuerAsync(domain);
        return Task.FromResult(true);
    }

    //

    private async Task PulseIssuerAsync(string domain)
    {
        try
        {
            await _issuanceNotifier.NotifyWorkAvailableAsync();
        }
        catch (Exception e)
        {
            // Never allowed to fault: nothing observes this task. Warning, not debug - if this
            // is failing, the domain will never get a certificate at all.
            _logger.LogWarning(e,
                "Could not notify the certificate issuer for {domain}; it will not get a " +
                "certificate until the issuer is reachable: {error}", domain, e.Message);
        }
    }

    //

    private bool ShouldPulse()
    {
        var now = DateTimeOffset.UtcNow;

        var lastGlobal = Interlocked.Read(ref _lastGlobalPulseTicks);
        if (now.UtcTicks - lastGlobal < GlobalPulseFloor.Ticks)
        {
            return false;
        }

        // A benign race here costs one extra pulse, which the sweep coalesces anyway
        Interlocked.Exchange(ref _lastGlobalPulseTicks, now.UtcTicks);
        return true;
    }

    //

    /// <summary>
    /// Places an ACME order for <paramref name="domain"/> if one can be placed right now, and
    /// waits for it. Returns null if the domain is in failure backoff, if another thread or node
    /// already holds the order lock, or if the order fails.
    /// </summary>
    /// <remarks>
    /// NOT for any request path. This blocks for as long as the order takes - minutes, in the
    /// worst case - and <paramref name="cancellationToken"/> must therefore have the lifetime of
    /// the application, never of a request. An order cancelled part-way still costs the CA
    /// allowance it has already spent, so cutting one short is worse than never starting it.
    /// Request paths want <see cref="RequestIssuanceAsync"/>.
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

        await using var handle = await TryAcquireOrderLockAsync(domain, cancellationToken);
        if (handle == null)
        {
            // Contention, not a fault. Somebody else is ordering for this domain right now.
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
    /// Background path, for a domain that already has a certificate.
    /// </summary>
    /// <remarks>
    /// This respects the failure backoff, and must. It used to ignore it on the grounds that
    /// "the loop's own interval is its rate limiter" - true while the sweep only ran on its 12h
    /// timer, and false the moment the sweep became pulsable from the request path. A host with
    /// a few certificate-less domains cycling out of their backoffs pulses the sweep several
    /// times an hour, and each sweep would re-place a full ACME order for every OTHER domain
    /// whose renewal is failing: the same allowance burn this work exists to stop, arriving on a
    /// different domain than the one being pulsed.
    ///
    /// The cost is bounded: the backoff caps at an hour and renewal begins 7 days before expiry.
    /// </remarks>
    /// <remarks>
    /// First issuance is a different problem and is routed to
    /// <see cref="CreateCertificateAsync(string,string[],CancellationToken)"/>, which does
    /// respect the backoff - otherwise every inbound connection to an un-issuable domain would
    /// pulse this service into placing another order.
    /// </remarks>
    public async Task<bool> RenewIfAboutToExpireAsync(string domain, string[] sans, CancellationToken cancellationToken = default)
    {
        var x509 = await GetCertificateAsync(domain);

        if (x509 == null)
        {
            // No certificate at all: this is first issuance, not renewal.
            return await CreateCertificateAsync(domain, sans, cancellationToken) != null;
        }

        // Before NeedsRenewalAsync, not after: that call does an authoritative DNS lookup for
        // every tenant whose certificate lacks the mta-sts SAN, and sweeps are pulsable from the
        // request path. A backed-off domain must not pay for it.
        if (IsInFailureBackoff(domain, out var backoffRemaining))
        {
            _logger.LogDebug(
                "Not renewing {domain}: a previous attempt failed, next attempt in {backoff}s",
                domain, (int)backoffRemaining.TotalSeconds);
            return false;
        }

        if (!await NeedsRenewalAsync(domain, x509, sans, cancellationToken))
        {
            return false;
        }

        await using var handle = await TryAcquireOrderLockAsync(domain, cancellationToken);
        if (handle == null)
        {
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
            await NoteFailureAsync(domain, error);
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
                    // Never reached the CA, so the CA-spend schedule is the wrong one. This is the
                    // failure an operator fixes and wants noticed promptly.
                    await NoteFailureAsync(domain, error, transient: true);
                    return null;
                }

                // Optional SANs (mta-sts) join the certificate only when their DNS record
                // actually resolves AND the feature that needs them is on: the CA fails the
                // WHOLE order if one name cannot validate, and manual-records tenants may not
                // have created the record.
                if (_configuration.Email.TenantMail.Enabled)
                {
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

            //
            // Exactly one order per invocation. There used to be a
            // retry loop here (ten attempts, two seconds apart) which read like resilience but
            // was not: every iteration calls NewOrder and creates a fresh set of authorizations,
            // and Let's Encrypt counts failed authorizations against a per-hostname hourly
            // allowance of five. Re-attempts belong to the backoff, which is spaced to respect
            // that limit.
            //
            var sw = Stopwatch.StartNew();
            var domains = new List<string> { domain };
            domains.AddRange(sans);
            var pems = await _certesAcme.CreateCertificateAsync(account, domains.ToArray(), cancellationToken);

            var x509 = await _certificateStore.PutCertificateAsync(domain, pems.PrivateKeyPem, pems.CertificatesPem);
            _backoff.TryRemove(domain, out _);
            _logger.LogInformation("Created certificate for {domain} in {elapsed}s", domain, sw.ElapsedMilliseconds / 1000.0);
            return x509;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Not our cancellation. HttpClient surfaces a request timeout as
            // TaskCanceledException, so a CA connectivity problem lands here looking like a
            // shutdown. Reporting it as "cancelled" and rethrowing would hide it twice over:
            // the message names the wrong cause, and rethrowing leaves the sweep's task
            // IsCanceled, which UpdateCertificatesBackgroundService does not log at all.
            var error = $"Timed out talking to the CA for {domain}";
            _logger.LogError("{error}", error);
            await NoteFailureAsync(domain, error, transient: true);
            return null;
        }
        catch (OperationCanceledException)
        {
            //
            // An abandoned order is not a free abort. By this point the CA has usually been asked
            // to validate, so it has cost us the same rate-limit allowance as a completed order
            // while producing nothing. Treating cancellation as a non-event is what let the
            // 60s handshake deadline re-order this domain on every inbound connection.
            //
            // If this cancellation is process shutdown, recording it costs nothing: the backoff
            // map is in-memory and dies with the process.
            //
            var error = $"Certificate order for {domain} was cancelled before it completed";
            _logger.LogWarning("{error}", error);

            // In-memory only. Persisting would put "cancelled before it completed" into the
            // certificates table as the domain's last error on every restart during an
            // in-flight order - overwriting the real reason an operator is looking for, and
            // doing a scoped DB write while the host is tearing down.
            NoteFailure(domain, error);
            throw;
        }
        catch (Exception e)
        {
            var error = $"Error creating certificate for {domain}: {e.Message.ReplaceLineEndings(". ").TrimEnd()}";
            _logger.LogError("{error}", error);

            // When the CA tells us how long it will keep saying no, believe it. A transient
            // error says nothing about this order and RFC 8555 expects it to be retried, so it
            // goes on the short schedule instead of the one sized for CA allowance spend.
            //
            // Transport failures count as transient too. Removing the in-call retry loop was
            // right - every iteration placed a fresh order - but it also means a network blip
            // reaching the CA no longer costs 2 seconds, it costs the whole backoff window.
            // Nothing about a failed TCP connection says this domain is unhealthy.
            //
            var isTransient = e is AcmeTransientException or HttpRequestException ||
                              e.InnerException is HttpRequestException;

            await NoteFailureAsync(domain, error,
                explicitBackoff: (e as AcmeRateLimitedException)?.RetryAfter,
                transient: isTransient);
            return null;
        }
    }

    //

    private bool IsInFailureBackoff(string domain, out TimeSpan remaining)
    {
        remaining = TimeSpan.Zero;

        if (!_backoff.TryGetValue(domain, out var state))
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        remaining = state.Until - now;
        if (remaining > TimeSpan.Zero)
        {
            return true;
        }

        remaining = TimeSpan.Zero;

        // Window elapsed. Keep the failure count so the backoff keeps widening if the next
        // attempt fails too - unless the window ended so long ago that this is a fresh problem
        // rather than a continuing one, in which case forget the domain entirely.
        if (now - state.Until > BackoffForgottenAfter)
        {
            _backoff.TryRemove(domain, out _);
        }

        return false;
    }

    //

    /// <param name="explicitBackoff">
    /// What the CA told us to wait, when it told us. Otherwise the exponential schedule applies.
    /// </param>
    private void NoteFailure(string domain, string error, TimeSpan? explicitBackoff = null, bool transient = false)
    {
        // One counter for both kinds of failure: a domain that keeps failing, whatever the cause,
        // deserves more caution. What differs is the schedule. A transient failure never spent CA
        // allowance, so it waits seconds not minutes - but it still doubles, so a CA outage does
        // not get asked once a minute for as long as it lasts.
        var consecutiveFailures = (_backoff.TryGetValue(domain, out var previous) ? previous.ConsecutiveFailures : 0) + 1;

        var backoff = explicitBackoff ??
                      (transient ? TransientBackoff(consecutiveFailures) : ExponentialBackoff(consecutiveFailures));
        _backoff[domain] = new BackoffState(DateTimeOffset.UtcNow + backoff, consecutiveFailures);

        _logger.LogWarning(
            "Certificate order for {domain} failed ({consecutiveFailures} in a row); " +
            "not trying again for {backoff}s. Reason: {error}",
            domain, consecutiveFailures, (int)backoff.TotalSeconds, error);
    }

    //

    private async Task NoteFailureAsync(
        string domain, string error, TimeSpan? explicitBackoff = null, bool transient = false)
    {
        NoteFailure(domain, error, explicitBackoff, transient);

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

    //
    // Waits for the lock, and treats "could not get it" as "somebody else is already ordering
    // for this domain", which is what it means. Both callers are background work, so waiting is
    // free - nothing is on a request path any more.
    //
    private async Task<IAsyncDisposable?> TryAcquireOrderLockAsync(string domain, CancellationToken cancellationToken)
    {
        try
        {
            return await _nodeLock.LockAsync(LockKey(domain), OrderLockTimeout, cancellationToken: cancellationToken);
        }
        catch (RedisLockException e)
        {
            _logger.LogDebug(
                "Not ordering for {domain}: an order is already in progress elsewhere. {error}", domain, e.Message);
            return null;
        }
    }

    //

    // internal for testing
    internal static TimeSpan TransientBackoff(int consecutiveFailures)
    {
        var doublings = Math.Min(consecutiveFailures - 1, 8);
        var backoff = InitialTransientBackoff * (1 << doublings);
        return backoff > MaxTransientBackoff ? MaxTransientBackoff : backoff;
    }

    //

    // internal for testing
    internal static TimeSpan ExponentialBackoff(int consecutiveFailures)
    {
        // 5m, 10m, 20m, 40m, then capped. Shift rather than Math.Pow so a long-failing domain
        // cannot overflow its way back to a short wait.
        var doublings = Math.Min(consecutiveFailures - 1, 8);
        var backoff = InitialFailureBackoff * (1 << doublings);
        return backoff > MaxFailureBackoff ? MaxFailureBackoff : backoff;
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