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
    private static readonly TimeSpan OptionalSanSuppression = TimeSpan.FromDays(7);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _optionalSansSuppressed = new(StringComparer.OrdinalIgnoreCase);

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
    public async Task RequestIssuanceAsync(string domain)
    {
        if (IsInFailureBackoff(domain, out var remaining))
        {
            _logger.LogDebug(
                "Not requesting issuance for {domain}: a previous attempt failed, next attempt in {backoff}s",
                domain, (int)remaining.TotalSeconds);
            return;
        }

        _logger.LogDebug("Requesting out-of-band certificate issuance for {domain}", domain);
        await _issuanceNotifier.NotifyWorkAvailableAsync();
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
    /// Background path, for a domain that already has a certificate. Deliberately ignores the
    /// failure backoff: a certificate that is about to expire must be chased on every sweep, and
    /// the loop's own interval is its rate limiter. Nobody is waiting on it.
    /// </summary>
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

        if (!await NeedsRenewalAsync(domain, x509, sans, cancellationToken))
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
            await NoteFailureAsync(domain, error);
            return null;
        }

        var optionalSans = Array.Empty<string>();

        try
        {
            if (sans.Length > 0) // don't verify system domains (e.g. provisioning, admin, etc)
            {
                var (areDnsRecordsOk, dnsConfigs) = await _dnsLookupService.GetAuthoritativeDomainDnsStatusAsync(new AsciiDomainName(domain), cancellationToken: cancellationToken);
                if (!areDnsRecordsOk)
                {
                    var error = $"Cannot create certificate for {domain}. One or more DNS records are incorrect.";
                    _logger.LogWarning("{error}", error);
                    await NoteFailureAsync(domain, error);
                    return null;
                }

                // Optional SANs (mta-sts) join the certificate only when their DNS record
                // actually resolves AND the feature that needs them is on: the CA fails the
                // WHOLE order if one name cannot validate, and manual-records tenants may not
                // have created the record.
                //
                // Resolving is a weaker test than it looks - it says the name points here, not
                // that we can serve its challenge - so this gate cannot be the only protection.
                // See the fallback at the order site below.
                if (_configuration.Email.TenantMail.Enabled && !AreOptionalSansSuppressed(domain))
                {
                    optionalSans = dnsConfigs
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
            // Exactly one order per invocation - with one exception, below. There used to be a
            // retry loop here (ten attempts, two seconds apart) which read like resilience but
            // was not: every iteration calls NewOrder and creates a fresh set of authorizations,
            // and Let's Encrypt counts failed authorizations against a per-hostname hourly
            // allowance of five. Re-attempts belong to the backoff, which is spaced to respect
            // that limit.
            //
            var sw = Stopwatch.StartNew();
            KeysAndCertificates pems;
            try
            {
                pems = await OrderAsync(account, domain, sans, cancellationToken);
            }
            catch (AcmeOrderException e) when (RefusedOnlyOptionalNames(e, optionalSans, domain, sans))
            {
                //
                // The exception. A certificate order is all-or-nothing: if the CA refuses one
                // name it refuses the order. So an optional name we do not strictly need -
                // mta-sts - can deny the identity the certificate it does need for its apex,
                // capi and file names. That is not a trade worth making.
                //
                // Ordering again immediately is safe here precisely because the names that
                // remain are not the ones the CA is refusing.
                //
                var required = sans.Where(x => !optionalSans.Contains(x, StringComparer.OrdinalIgnoreCase)).ToArray();
                _logger.LogWarning(
                    "The CA refused only optional name(s) {optional} for {domain}. " +
                    "Ordering again without them so the identity still gets a certificate. Reason: {error}",
                    string.Join(',', optionalSans), domain, e.Message);

                pems = await OrderAsync(account, domain, required, cancellationToken);
                SuppressOptionalSans(domain);
            }

            var x509 = await _certificateStore.PutCertificateAsync(domain, pems.PrivateKeyPem, pems.CertificatesPem);
            _backoff.TryRemove(domain, out _);
            _logger.LogInformation("Created certificate for {domain} in {elapsed}s", domain, sw.ElapsedMilliseconds / 1000.0);
            return x509;
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
            await NoteFailureAsync(domain, error);
            throw;
        }
        catch (Exception e)
        {
            var error = $"Error creating certificate for {domain}: {e.Message.ReplaceLineEndings(". ").TrimEnd()}";
            _logger.LogError("{error}", error);

            // When the CA tells us how long it will keep saying no, believe it.
            await NoteFailureAsync(domain, error,
                (e as AcmeRateLimitedException)?.RetryAfter);
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

        remaining = state.Until - DateTimeOffset.UtcNow;
        if (remaining > TimeSpan.Zero)
        {
            return true;
        }

        // Window elapsed - allow the next attempt, but keep the failure count so the backoff
        // keeps widening if it fails again. The count is only cleared by a successful order.
        remaining = TimeSpan.Zero;
        return false;
    }

    //

    /// <param name="explicitBackoff">
    /// What the CA told us to wait, when it told us. Otherwise the exponential schedule applies.
    /// </param>
    private async Task NoteFailureAsync(string domain, string error, TimeSpan? explicitBackoff = null)
    {
        var consecutiveFailures = _backoff.TryGetValue(domain, out var previous)
            ? previous.ConsecutiveFailures + 1
            : 1;

        var backoff = explicitBackoff ?? ExponentialBackoff(consecutiveFailures);
        _backoff[domain] = new BackoffState(DateTimeOffset.UtcNow + backoff, consecutiveFailures);

        _logger.LogWarning(
            "Certificate order for {domain} failed ({consecutiveFailures} in a row); " +
            "not trying again for {backoff}s. Reason: {error}",
            domain, consecutiveFailures, (int)backoff.TotalSeconds, error);

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

    private Task<KeysAndCertificates> OrderAsync(
        AcmeAccount account, string domain, string[] sans, CancellationToken cancellationToken)
    {
        var domains = new List<string> { domain };
        if (sans.Length > 0)
        {
            domains.AddRange(sans);
        }

        return _certesAcme.CreateCertificateAsync(account, domains.ToArray(), cancellationToken);
    }

    //

    /// <summary>
    /// True when the CA's complaint names at least one optional SAN and no required name, so
    /// dropping the optional names is likely to produce a certificate rather than a second
    /// wasted order.
    /// </summary>
    // internal for testing
    internal bool RefusedOnlyOptionalNames(
        AcmeOrderException e, string[] optionalSans, string domain, string[] allSans)
    {
        if (optionalSans.Length == 0)
        {
            return false;
        }

        var required = allSans
            .Where(x => !optionalSans.Contains(x, StringComparer.OrdinalIgnoreCase))
            .Append(domain)
            .ToArray();

        // Prefer what the CA told us outright. When it says nothing structured - a top-level
        // rateLimited problem carries no sub-problems - fall back to the names it wrote into
        // the detail text, which is where Let's Encrypt puts the hostname it is refusing.
        var implicated = e.FailedIdentifiers.Count > 0
            ? e.FailedIdentifiers
            : optionalSans.Concat(required)
                .Where(name => MentionsName(e.Message, name))
                .ToArray();

        if (implicated.Count == 0)
        {
            return false;
        }

        var refusedOptional = implicated.Any(x => optionalSans.Contains(x, StringComparer.OrdinalIgnoreCase));
        var refusedRequired = implicated.Any(x => required.Contains(x, StringComparer.OrdinalIgnoreCase));

        if (refusedOptional && refusedRequired)
        {
            _logger.LogDebug(
                "Not dropping optional names for {domain}: the CA also refused a required name ({implicated})",
                domain, string.Join(',', implicated));
        }

        return refusedOptional && !refusedRequired;
    }

    //

    /// <summary>
    /// Whether <paramref name="message"/> names exactly <paramref name="name"/>, rather than
    /// merely containing it.
    /// </summary>
    /// <remarks>
    /// A plain Contains is wrong here, and wrong in the direction that matters: every SAN has
    /// the apex as a suffix, so "mta-sts.delete.n1.id.pub" contains "delete.n1.id.pub". A
    /// substring test would read a complaint about the optional name as a complaint about the
    /// apex too, conclude a required name was refused, and silently switch off the fallback in
    /// exactly the case it exists for.
    /// </remarks>
    // internal for testing
    internal static bool MentionsName(string message, string name)
    {
        if (string.IsNullOrEmpty(message) || string.IsNullOrEmpty(name))
        {
            return false;
        }

        static bool IsHostChar(char c) => char.IsLetterOrDigit(c) || c == '-';

        for (var index = 0;
             (index = message.IndexOf(name, index, StringComparison.OrdinalIgnoreCase)) >= 0;
             index += name.Length)
        {
            // A label character immediately before means we matched a longer name's tail
            // (the apex inside one of its own subdomains).
            if (index > 0)
            {
                var before = message[index - 1];
                if (IsHostChar(before) || before == '.')
                {
                    continue;
                }
            }

            var end = index + name.Length;
            if (end >= message.Length)
            {
                return true;
            }

            var after = message[end];
            if (IsHostChar(after))
            {
                continue;
            }

            // A trailing dot is fine (root label, or end of sentence) unless another label
            // follows it, which would again mean we matched a prefix of a longer name.
            if (after == '.' && end + 1 < message.Length && IsHostChar(message[end + 1]))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    //

    private void SuppressOptionalSans(string domain)
    {
        _optionalSansSuppressed[domain] = DateTimeOffset.UtcNow + OptionalSanSuppression;
        _logger.LogWarning(
            "Issued a certificate for {domain} without its optional name(s). Not asking for them " +
            "again for {days} days, to avoid renewing into the same refusal.",
            domain, OptionalSanSuppression.TotalDays);
    }

    //

    // internal for testing
    internal bool AreOptionalSansSuppressed(string domain)
    {
        if (!_optionalSansSuppressed.TryGetValue(domain, out var until))
        {
            return false;
        }

        if (until > DateTimeOffset.UtcNow)
        {
            return true;
        }

        _optionalSansSuppressed.TryRemove(domain, out _);
        return false;
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

        // The certificate is missing mta-sts because the CA refused it and we issued without
        // it. Renewing to re-add it would fail, drop it, and mint another duplicate.
        if (AreOptionalSansSuppressed(domain))
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