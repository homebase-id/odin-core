using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DnsClient;
using DnsClient.Protocol;
using Microsoft.Extensions.Logging;
using Odin.Core.Dns;
using Odin.Core.Util;
using Odin.Services.Configuration;
using Odin.Services.Email.Dkim;
using Odin.Services.Email.Relay;
using Odin.Services.Registry.Registration;

namespace Odin.Services.Dns.Health;

#nullable enable

/// <summary>
/// DNSSEC state of a domain, determined from PUBLIC DNS alone - no DNS-server API.
/// Contrast <see cref="Registry.Registration.DnssecStatus"/> (provisioning-host view,
/// which may consult PowerDNS): here the PowerDNS-only states do not exist, and two
/// generic ones take their place. See docs/owner-console-dnssec-panel-plan.md.
/// </summary>
public enum DnsHealthDnssecStatus
{
    /// <summary>The identity domain is not a zone cut - DNSSEC is governed by the enclosing zone (e.g. managed domains under our apex)</summary>
    Inherited,

    /// <summary>Whoever hosts the domain's DNS does not sign the zone (no DNSKEY served)</summary>
    ZoneUnsigned,

    /// <summary>The zone is signed but the parent zone is not - a DS cannot extend the chain</summary>
    ParentUnsigned,

    /// <summary>Parent signed, no DS published - the user can add the DS we provide</summary>
    DsMissing,

    /// <summary>DS records exist at the parent but none matches the zone's keys - validating resolvers will SERVFAIL</summary>
    DsMismatch,

    /// <summary>At least one DS at the parent matches the zone's keys - the chain is anchored</summary>
    Secure,
}

public enum OptionalRecordStatus
{
    /// <summary>Points at the identity</summary>
    Success,

    /// <summary>Not set - perfectly fine, the record is optional</summary>
    NotSet,

    /// <summary>Exists but points somewhere else - fine if intentional (e.g. a separate www site)</summary>
    PointsElsewhere,
}

public sealed class OptionalRecordResult
{
    public string Name { get; init; } = "";
    public string Domain { get; init; } = "";
    public OptionalRecordStatus Status { get; init; }

    /// <summary>What the record currently resolves to (empty when not set)</summary>
    public List<string> Found { get; init; } = [];
}

public sealed class DnssecHealthResult
{
    public DnsHealthDnssecStatus Status { get; init; }

    /// <summary>The enclosing zone when Status is Inherited</summary>
    public string EnclosingZone { get; init; } = "";

    /// <summary>
    /// When Status is Inherited: how the enclosing zone itself grades. "Inherited" used to be
    /// taken on trust; this is the check. Not the owner's to fix (the enclosing zone of a
    /// managed domain is ours), so it never counts as owner attention.
    /// </summary>
    public DnsHealthDnssecStatus? EnclosingZoneStatus { get; init; }

    /// <summary>
    /// The zone's keys could not be looked up at all, so ZoneUnsigned here means "could not
    /// tell". A DNS hiccup must not land in an owner's monthly report as a security finding.
    /// </summary>
    public bool LookupFailed { get; init; }

    /// <summary>The server's verdict, shipped so clients (the DNS tab dot) do not keep their own copy of the rule</summary>
    public bool NeedsAttention => DnsHealthService.NeedsUserAttention(this);

    /// <summary>The DS record(s) the user would publish at the parent (CDS verbatim when the zone publishes them, else computed from the zone's public keys)</summary>
    public List<DsRecordData> DsToPublish { get; init; } = [];

    /// <summary>The DS records actually published at the parent</summary>
    public List<DsRecordData> ParentDsRecords { get; init; } = [];

    public bool ParentZoneSigned { get; init; }
}

public enum MailRelayHealthStatus
{
    /// <summary>Nothing to check: no relay configured, tenant mail off, or this tenant never activated email</summary>
    NotApplicable,

    /// <summary>The relay knows the domain and has verified its records</summary>
    Registered,

    /// <summary>The relay knows the domain but has not verified its records (yet) - see Problems</summary>
    Unverified,

    /// <summary>The relay does not know the domain: outbound mail from it cannot be relayed. See LastError</summary>
    NotRegistered,

    /// <summary>We could not ask the relay. Says nothing about the domain, so it is not the owner's problem</summary>
    Unreachable,
}

/// <summary>
/// The outbound relay's verdict on this tenant. Before it existed, a domain the relay had
/// refused simply produced no rows - nothing to grade, so every health surface said OK while
/// the tenant's mail could not leave (2026-10-07).
/// </summary>
public sealed class MailRelayHealthResult
{
    public MailRelayHealthStatus Status { get; init; }

    /// <summary>The relay's own per-record diagnostics, verbatim</summary>
    public List<string> Problems { get; init; } = [];

    /// <summary>Why the relay last refused the domain, in its own words; null when it has not</summary>
    public string? LastError { get; init; }

    /// <summary>
    /// Some of the relay's own records are not live yet. Those rows already show as broken, and
    /// the relay's "not verified" would only say the same thing again - so while this is true,
    /// Unverified is not a problem of its own. It becomes one when every row is live and the
    /// relay still has not verified: that is the case nothing else would show.
    /// </summary>
    public bool RecordsNotLiveYet { get; init; }

    /// <summary>
    /// The verdict described for a human, or null when it needs no attention. The one wording
    /// every surface shows - the Email tab, the monthly email, the app's health check.
    /// </summary>
    public string? Problem => Status switch
    {
        MailRelayHealthStatus.NotRegistered => LastError == null
            ? "Outbound sending is not set up: the mail relay has not registered this domain"
            : $"Outbound sending is not set up: the mail relay refused this domain ({LastError})",
        MailRelayHealthStatus.Unverified when RecordsNotLiveYet => null,
        MailRelayHealthStatus.Unverified => Problems.Count == 0
            ? "Outbound sending is not verified yet by the mail relay"
            : $"Outbound sending is not verified yet by the mail relay ({string.Join("; ", Problems)})",
        _ => null,
    };

    public bool NeedsAttention => Problem != null;
}

public sealed class DnsHealthResult
{
    /// <summary>Required records with live status (same shape the provisioning screens consume)</summary>
    public List<DnsConfig> Records { get; init; } = [];

    /// <summary>
    /// The Optional-flagged record set (today: the email records, present when
    /// Email:TenantMail is enabled) with live status. Kept out of Records so
    /// clients never render them as failed REQUIRED records - they are optional
    /// until the tenant's mail is actually live.
    /// </summary>
    public List<DnsConfig> MailRecords { get; init; } = [];

    /// <summary>The server-side success rule over Records (delegation OR record rule)</summary>
    public bool RecordsAreValid { get; init; }

    /// <summary>
    /// Whether this HOST serves tenant mail at all (Email:TenantMail:Enabled).
    ///
    /// Without it an empty <see cref="MailRecords"/> is ambiguous, and the two cases call for
    /// opposite messages: false means the server does not do email and the owner cannot act;
    /// true with no records means the owner has not set it up yet and can. Additive field -
    /// old frontends ignore it.
    /// </summary>
    public bool TenantMailEnabled { get; init; }

    /// <summary>Optional records (www) - informational, never errors</summary>
    public List<OptionalRecordResult> OptionalRecords { get; init; } = [];

    public DnssecHealthResult Dnssec { get; init; } = new();

    public MailRelayHealthResult Relay { get; init; } = new();
}

/// <summary>
/// The owner-console DNS health check: required-record status, optional www, and the
/// DNSSEC chain of trust - all from generic public-DNS lookups (authoritative-only,
/// cache-safe), so it behaves identically whether the zone is hosted by us, a third
/// party, or self-hosted. Per the architectural boundary (docs/byod-dnssec-plan.md)
/// this service must never touch the PowerDNS API: it runs on identity hosts.
/// </summary>
public class DnsHealthService(
    ILogger<DnsHealthService> logger,
    OdinConfiguration configuration,
    ILookupClient dnsClient,
    IAuthoritativeDnsLookup authoritativeDnsLookup,
    IDnssecLookup dnssecLookup,
    IDnsLookupService dnsLookupService,
    IDkimStore dkimStore,
    IMailRelayProvider relayProvider,
    IMailRelayFailureStore relayFailureStore)
{
    // The enclosing zone of a managed domain is one shared apex for thousands of tenants, and
    // grading it is a chain of uncached authority walks. Graded once per TTL per node instead
    // of per tenant per request - which also keeps a broken apex to one error line per TTL.
    private static readonly TimeSpan EnclosingZoneGradeTtl = TimeSpan.FromMinutes(15);
    private readonly ConcurrentDictionary<string, (DnsHealthDnssecStatus status, DateTimeOffset gradedAt)> _enclosingZoneGrades = new();

    // Re-verification asks the relay to do DNS work on its side; once per domain per interval
    // per node is plenty, and keeps a tab left open (or the monthly sweep) from hammering it
    private static readonly TimeSpan RelayReverifyInterval = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _relayReverifiedAt = new();

    private static readonly DnsQueryOptions AuthoritativeQueryOptions = new()
    {
        Recursion = false,
        UseCache = false,
    };

    //

    public async Task<DnsHealthResult> GetDnsHealthAsync(AsciiDomainName domain, CancellationToken cancellationToken = default)
    {
        // Per-tenant records - values that exist only after activation, so they cannot come
        // from the config-only GetDnsConfiguration. Both sets ride the same extraRecords seam,
        // which means the status lookups, the Optional/mail split and every client's notion of
        // "broken" pick them up without knowing they are different in origin.
        var dkimRecords = await GetDkimRecordsAsync(domain);
        var extraRecords = dkimRecords ?? [];

        // DKIM keys exist exactly when the tenant activated email - the only case in which the
        // relay is supposed to know the domain at all. An unreadable DKIM store (null) proves
        // nothing either way, so the relay is asked rather than the tenant assumed inactive -
        // otherwise a store hiccup would hide a refused domain behind "nothing to check".
        var (relayRecords, relay) = await GetRelayHealthAsync(domain, mailActivated: dkimRecords is not { Count: 0 });
        extraRecords.AddRange(relayRecords);

        var (recordsAreValid, records) = await dnsLookupService.GetAuthoritativeDomainDnsStatusAsync(
            domain, extraRecords, cancellationToken);
        var optionalRecords = await CheckOptionalWwwAsync(domain, cancellationToken);
        var dnssec = await GetDnssecHealthAsync(domain, cancellationToken);

        return new DnsHealthResult
        {
            // Optional-flagged rows (the email record set) split out: they must never
            // show up as failed required records in a client
            Records = records.Where(x => !x.Optional).ToList(),
            MailRecords = records.Where(x => x.Optional).ToList(),
            TenantMailEnabled = configuration.Email.TenantMail.Enabled,
            RecordsAreValid = recordsAreValid,
            OptionalRecords = optionalRecords,
            Dnssec = dnssec,
            Relay = await SettleUnverifiedRelayAsync(domain, relay, relayRecords, records),
        };
    }

    /// <summary>
    /// "Unverified" is the relay's own stored verdict, and it only changes when the relay is
    /// asked to look again. Our authoritative lookup just told us whether its records are live,
    /// so the two can be reconciled here instead of shown side by side contradicting each other:
    ///
    /// - rows not live yet: the broken rows already say so; the relay's verdict adds nothing.
    /// - rows live: the verdict is stale (the owner added them after the relay last looked, or
    ///   after onboarding stopped retrying). Ask it to look again - whoever published them,
    ///   us or the owner by hand - at most once per domain per interval.
    /// </summary>
    private async Task<MailRelayHealthResult> SettleUnverifiedRelayAsync(
        AsciiDomainName domain, MailRelayHealthResult relay, List<DnsConfig> relayRecords, List<DnsConfig> graded)
    {
        if (relay.Status != MailRelayHealthStatus.Unverified)
        {
            return relay;
        }

        if (AnyNotLive(relayRecords, graded))
        {
            return new MailRelayHealthResult
            {
                Status = relay.Status,
                Problems = relay.Problems,
                LastError = relay.LastError,
                RecordsNotLiveYet = true,
            };
        }

        var now = DateTimeOffset.UtcNow;
        var key = domain.DomainName.ToLowerInvariant();
        if (_relayReverifiedAt.TryGetValue(key, out var last) && now - last < RelayReverifyInterval)
        {
            return relay;
        }
        _relayReverifiedAt[key] = now;

        try
        {
            var state = await relayProvider.VerifyDomainAsync(domain);
            logger.LogInformation("Relay: re-verified {domain} from the health check -> {verified}", domain, state.Verified);
            return new MailRelayHealthResult
            {
                Status = state.Verified ? MailRelayHealthStatus.Registered : MailRelayHealthStatus.Unverified,
                Problems = state.Problems,
            };
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Relay: could not re-verify {domain} from the health check", domain);
            return relay;
        }
    }

    //

    /// <summary>
    /// The outbound relay's per-tenant CNAMEs and its verdict, when a relay is configured and
    /// the tenant activated email. The CNAME names are allocated by the relay, so they are read
    /// from it rather than derived.
    ///
    /// Three answers that used to look identical (no rows) are now told apart: the relay does
    /// not know the domain (NotRegistered - the owner's problem, with the relay's last refusal
    /// attached), we could not ask (Unreachable - not the owner's problem), and nothing to ask
    /// about (NotApplicable).
    /// </summary>
    private async Task<(List<DnsConfig> records, MailRelayHealthResult verdict)> GetRelayHealthAsync(
        AsciiDomainName domain, bool mailActivated)
    {
        if (!configuration.Email.TenantMail.Enabled || !relayProvider.IsConfigured || !mailActivated)
        {
            return ([], new MailRelayHealthResult { Status = MailRelayHealthStatus.NotApplicable });
        }

        MailRelayDomainState? state;
        try
        {
            state = await relayProvider.GetDomainAsync(domain);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not read relay records for {domain}; omitted from the health check",
                domain.DomainName);
            return ([], new MailRelayHealthResult { Status = MailRelayHealthStatus.Unreachable });
        }

        if (state == null)
        {
            return ([], new MailRelayHealthResult
            {
                Status = MailRelayHealthStatus.NotRegistered,
                LastError = await TryGetRelayFailureAsync(domain),
            });
        }

        return (state.Records, new MailRelayHealthResult
        {
            Status = state.Verified ? MailRelayHealthStatus.Registered : MailRelayHealthStatus.Unverified,
            Problems = state.Problems,
        });
    }

    // The relay's rows, as the authoritative lookup graded them
    private static bool AnyNotLive(List<DnsConfig> relayRecords, IEnumerable<DnsConfig> graded) =>
        graded.Any(g => g.Status != DnsLookupRecordStatus.Success && relayRecords.Any(r =>
            string.Equals(r.Type, g.Type, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(r.Domain, g.Domain, StringComparison.OrdinalIgnoreCase)));

    private async Task<string?> TryGetRelayFailureAsync(AsciiDomainName domain)
    {
        try
        {
            return await relayFailureStore.GetAsync(domain.DomainName);
        }
        catch (Exception e)
        {
            // The verdict stands without the reason; losing the reason must not lose the verdict
            logger.LogWarning(e, "Could not read the last relay failure for {domain}", domain.DomainName);
            return null;
        }
    }

    /// <summary>
    /// The tenant's DKIM records, so the owner console can check them like any other.
    ///
    /// They cannot come from GetDnsConfiguration: that list is built from configuration,
    /// while DKIM values are per-tenant key material that exists only after email
    /// activation. Returns empty whenever there is nothing to check - tenant mail off,
    /// no storage key configured, or the tenant never activated email.
    ///
    /// A read failure is logged and returns null: DKIM is one block of a health panel, and
    /// a store hiccup should not take the whole panel down with it - but it must not pass
    /// for "never activated" either.
    /// </summary>
    private async Task<List<DnsConfig>?> GetDkimRecordsAsync(AsciiDomainName domain)
    {
        if (!configuration.Email.TenantMail.Enabled || !dkimStore.IsConfigured)
        {
            return [];
        }

        try
        {
            var keys = await dkimStore.GetKeysAsync(domain.DomainName);
            return keys.Count == 0 ? [] : DkimDnsRecords.ToDnsConfigs(domain.DomainName, keys);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not read DKIM keys for {domain}; DKIM records omitted from the health check",
                domain.DomainName);
            return null;
        }
    }

    /// <summary>
    /// The optional www record. Deliberately NOT part of GetDnsConfiguration: that list
    /// feeds the signup success rule and certificate checks, and a missing optional
    /// record must never fail either. None of the states here is an error.
    /// </summary>
    internal async Task<List<OptionalRecordResult>> CheckOptionalWwwAsync(AsciiDomainName domain, CancellationToken cancellationToken)
    {
        var wwwDomain = "www." + domain.DomainName;
        var dns = configuration.Registry.DnsConfigurationSet;

        var authority = await authoritativeDnsLookup.LookupDomainAuthorityAsync(domain.DomainName, cancellationToken);
        if (string.IsNullOrEmpty(authority.AuthoritativeNameServer))
        {
            // Cannot tell; report as not set rather than inventing an error state
            return [new OptionalRecordResult { Name = "www", Domain = wwwDomain, Status = OptionalRecordStatus.NotSet }];
        }

        var cnameResponse = await dnsClient.Query(
            authority.NameServers, wwwDomain, QueryType.CNAME, AuthoritativeQueryOptions, logger, cancellationToken: cancellationToken);
        var cnames = (cnameResponse?.Answers.CnameRecords() ?? [])
            .Select(x => x.CanonicalName.ToString()!.TrimEnd('.').ToLowerInvariant())
            .ToList();

        List<string> found;
        bool pointsAtIdentity;
        if (cnames.Count > 0)
        {
            found = cnames;
            // Healthy targets: the identity domain itself, or the same alias the apex uses
            pointsAtIdentity = cnames.Any(x => x == domain.DomainName || x == dns.ApexAliasRecord.ToLowerInvariant());
        }
        else
        {
            var aResponse = await dnsClient.Query(
                authority.NameServers, wwwDomain, QueryType.A, AuthoritativeQueryOptions, logger, cancellationToken: cancellationToken);
            found = (aResponse?.Answers.ARecords() ?? []).Select(x => x.Address.ToString()).ToList();
            pointsAtIdentity = found.Any(x => x == dns.ApexARecord);
        }

        var status = found.Count == 0
            ? OptionalRecordStatus.NotSet
            : pointsAtIdentity
                ? OptionalRecordStatus.Success
                : OptionalRecordStatus.PointsElsewhere;

        return [new OptionalRecordResult { Name = "www", Domain = wwwDomain, Status = status, Found = found }];
    }

    //

    public async Task<DnssecHealthResult> GetDnssecHealthAsync(AsciiDomainName domain, CancellationToken cancellationToken = default)
    {
        var domainName = domain.DomainName;

        // Not a zone cut? (e.g. a managed domain living inside our apex zone) Then
        // DNSSEC is the enclosing zone's business and there is nothing to configure here
        var zoneApex = await authoritativeDnsLookup.LookupZoneApexAsync(domainName, cancellationToken);
        if (zoneApex != domainName)
        {
            // ...but "governed by" is not "secured by": grade the enclosing zone with the same
            // rules rather than taking it on trust
            return new DnssecHealthResult
            {
                Status = DnsHealthDnssecStatus.Inherited,
                EnclosingZone = zoneApex,
                EnclosingZoneStatus = IsOurManagedApex(zoneApex)
                    ? await GradeEnclosingZoneAsync(zoneApex, cancellationToken)
                    : null,
            };
        }

        return await GradeZoneAsync(domainName, cancellationToken);
    }

    // Only our own apexes are graded: an identity can also sit inside an owner's own zone
    // (home.example.com in example.com), which is neither ours to fix nor ours to log about
    private bool IsOurManagedApex(string zone) => configuration.Registry.ManagedDomainApexes.Any(
        x => string.Equals(x.Apex.Trim().TrimEnd('.'), zone, StringComparison.OrdinalIgnoreCase));

    private async Task<DnsHealthDnssecStatus?> GradeEnclosingZoneAsync(string zoneApex, CancellationToken cancellationToken)
    {
        if (_enclosingZoneGrades.TryGetValue(zoneApex, out var cached) &&
            DateTimeOffset.UtcNow - cached.gradedAt < EnclosingZoneGradeTtl)
        {
            return cached.status;
        }

        DnssecHealthResult grade;
        try
        {
            grade = await GradeZoneAsync(zoneApex, cancellationToken);
        }
        catch (Exception e)
        {
            // An extra on the tenant's panel; failing to compute it must not fail the panel
            logger.LogWarning(e, "DNSSEC: could not grade enclosing zone {zone}", zoneApex);
            return null;
        }

        if (grade.LookupFailed)
        {
            return null;
        }

        var status = grade.Status;
        _enclosingZoneGrades[zoneApex] = (status, DateTimeOffset.UtcNow);

        // Ours, not the owner's: an ops fault, logged once per fresh grade
        if (status != DnsHealthDnssecStatus.Secure)
        {
            logger.LogError("DNSSEC: enclosing zone {zone} of managed domains is {status}, not Secure", zoneApex, status);
        }

        return status;
    }

    // The verdict for a zone cut: signed at all, then anchored at the parent
    private async Task<DnssecHealthResult> GradeZoneAsync(string domainName, CancellationToken cancellationToken)
    {
        var dnsKeys = await dnssecLookup.TryGetZoneDnsKeysAsync(domainName, cancellationToken);
        if (dnsKeys == null || dnsKeys.Count == 0)
        {
            return new DnssecHealthResult { Status = DnsHealthDnssecStatus.ZoneUnsigned, LookupFailed = dnsKeys == null };
        }

        var parentZoneSigned = await dnssecLookup.IsParentZoneSignedAsync(domainName, cancellationToken);
        var parentDsRecords = await dnssecLookup.GetParentDsRecordsAsync(domainName, cancellationToken);

        // What to publish: the zone's own CDS wish-list when present, else computed
        // from the public signing keys (SEP-flagged keys; all keys if none carry SEP)
        var cdsRecords = await dnssecLookup.GetCdsRecordsAsync(domainName, cancellationToken);
        var dsToPublish = cdsRecords.Count > 0
            ? cdsRecords
            : SigningKeys(dnsKeys).Select(key => DnssecLookup.ComputeDsFromDnsKey(domainName, key)).ToList();

        var anyDsMatches = AnyPublishedDsMatchesZoneKeys(domainName, dnsKeys, parentDsRecords);
        var status = ComputeVerdict(parentZoneSigned, parentDsRecords.Count, anyDsMatches);

        return new DnssecHealthResult
        {
            Status = status,
            DsToPublish = dsToPublish,
            ParentDsRecords = parentDsRecords,
            ParentZoneSigned = parentZoneSigned,
        };
    }

    //

    /// <summary>
    /// The DNSSEC state when it needs the owner, for the monthly security health email:
    /// DsMismatch (SERVFAIL risk), DsMissing (one record away), and - since 2026-10-07, by
    /// decision - ParentUnsigned and ZoneUnsigned too: an unanchored zone weakens both the
    /// identity's security and its mail deliverability, even where the fix lies with the
    /// registrar or the parent zone's owner rather than with us. (This reverses the original
    /// "no nagging about states the user cannot change through us" rule in
    /// docs/owner-console-dnssec-panel-plan.md.) Secure and Inherited return null; an
    /// enclosing zone that grades badly is ours to fix and is logged instead. Never throws;
    /// a lookup failure returns null and must not count as attention.
    /// </summary>
    public async Task<DnssecHealthResult?> GetDnssecAttentionAsync(AsciiDomainName domain, CancellationToken cancellationToken = default)
    {
        try
        {
            var health = await GetDnssecHealthAsync(domain, cancellationToken);
            return NeedsUserAttention(health) ? health : null;
        }
        catch (System.Exception e)
        {
            logger.LogWarning("DNSSEC attention check for {domain} failed: {error}", domain, e.Message);
            return null;
        }
    }

    /// <summary>
    /// The tenant's broken mail DNS records, described for a human, or an empty list when
    /// there is nothing to report. Feeds the monthly security health report - a broken SPF
    /// or DKIM record is silent otherwise: mail is refused or spam-foldered and the owner
    /// finds out from the people who stopped receiving it.
    ///
    /// Best-effort like the DNSSEC equivalent: a DNS hiccup must not block the report or
    /// count as attention.
    /// </summary>
    public async Task<List<string>> GetMailRecordAttentionAsync(
        AsciiDomainName domain,
        CancellationToken cancellationToken = default)
    {
        if (!configuration.Email.TenantMail.Enabled)
        {
            return [];
        }

        try
        {
            var health = await GetDnsHealthAsync(domain, cancellationToken);
            var attention = health.MailRecords
                .Where(x => x.Status != DnsLookupRecordStatus.Success)
                .Select(DescribeBrokenRecord)
                .ToList();
            if (health.Relay.Problem is { } relayProblem)
            {
                attention.Add(relayProblem);
            }
            return attention;
        }
        catch (System.Exception e)
        {
            logger.LogWarning("Mail record attention check for {domain} failed: {error}", domain, e.Message);
            return [];
        }
    }

    // internal for testing
    internal static string DescribeBrokenRecord(DnsConfig record)
    {
        var what = record.Status == DnsLookupRecordStatus.IncorrectValue ? "has the wrong value" : "is missing";
        return $"{record.Description} ({record.Type} record on {record.Domain}) {what}";
    }

    // Pure trigger rule, data-level testable
    internal static bool NeedsUserAttention(DnssecHealthResult health)
    {
        return !health.LookupFailed && health.Status is DnsHealthDnssecStatus.DsMismatch
            or DnsHealthDnssecStatus.DsMissing
            or DnsHealthDnssecStatus.ParentUnsigned
            or DnsHealthDnssecStatus.ZoneUnsigned;
    }

    //

    // Zone signing established beforehand (keys exist); pure so it is data-level testable
    internal static DnsHealthDnssecStatus ComputeVerdict(bool parentZoneSigned, int parentDsCount, bool anyDsMatches)
    {
        if (!parentZoneSigned)
        {
            return DnsHealthDnssecStatus.ParentUnsigned;
        }
        if (parentDsCount == 0)
        {
            return DnsHealthDnssecStatus.DsMissing;
        }
        return anyDsMatches ? DnsHealthDnssecStatus.Secure : DnsHealthDnssecStatus.DsMismatch;
    }

    //

    /// <summary>
    /// A published DS matches when recomputing it from one of the zone's keys - in the
    /// DIGEST TYPE the parent chose - yields the same record. Recomputing per parent
    /// digest type avoids false mismatches when the parent holds e.g. a SHA-384 DS
    /// while we would default to SHA-256.
    /// </summary>
    internal static bool AnyPublishedDsMatchesZoneKeys(
        string domainName,
        IReadOnlyCollection<DnsKeyRecord> dnsKeys,
        IReadOnlyCollection<DsRecordData> parentDsRecords)
    {
        foreach (var parentDs in parentDsRecords)
        {
            foreach (var key in dnsKeys)
            {
                DsRecordData computed;
                try
                {
                    computed = DnssecLookup.ComputeDsFromDnsKey(domainName, key, parentDs.DigestType);
                }
                catch (ArgumentOutOfRangeException)
                {
                    continue; // digest type we cannot compute (e.g. obsolete SHA-1/GOST)
                }
                if (computed.Matches(parentDs))
                {
                    return true;
                }
            }
        }
        return false;
    }

    //

    // Keys with the SEP flag (KSK/CSK) are the ones a DS should anchor; fall back to
    // all keys for zones that do not set SEP
    private static List<DnsKeyRecord> SigningKeys(List<DnsKeyRecord> dnsKeys)
    {
        var sepKeys = dnsKeys.Where(x => (x.Flags & 0x0001) != 0).ToList();
        return sepKeys.Count > 0 ? sepKeys : dnsKeys;
    }
}
