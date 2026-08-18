using System;
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

    /// <summary>The DS record(s) the user would publish at the parent (CDS verbatim when the zone publishes them, else computed from the zone's public keys)</summary>
    public List<DsRecordData> DsToPublish { get; init; } = [];

    /// <summary>The DS records actually published at the parent</summary>
    public List<DsRecordData> ParentDsRecords { get; init; } = [];

    public bool ParentZoneSigned { get; init; }
}

public sealed class DnsHealthResult
{
    /// <summary>Required records with live status (same shape the provisioning screens consume)</summary>
    public List<DnsConfig> Records { get; init; } = [];

    /// <summary>The server-side success rule over Records (delegation OR record rule)</summary>
    public bool RecordsAreValid { get; init; }

    /// <summary>Optional records (www) - informational, never errors</summary>
    public List<OptionalRecordResult> OptionalRecords { get; init; } = [];

    public DnssecHealthResult Dnssec { get; init; } = new();
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
    IDnsLookupService dnsLookupService)
{
    private static readonly DnsQueryOptions AuthoritativeQueryOptions = new()
    {
        Recursion = false,
        UseCache = false,
    };

    //

    public async Task<DnsHealthResult> GetDnsHealthAsync(AsciiDomainName domain, CancellationToken cancellationToken = default)
    {
        var (recordsAreValid, records) = await dnsLookupService.GetAuthoritativeDomainDnsStatusAsync(domain, cancellationToken);
        var optionalRecords = await CheckOptionalWwwAsync(domain, cancellationToken);
        var dnssec = await GetDnssecHealthAsync(domain, cancellationToken);

        return new DnsHealthResult
        {
            Records = records,
            RecordsAreValid = recordsAreValid,
            OptionalRecords = optionalRecords,
            Dnssec = dnssec,
        };
    }

    //

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

    internal async Task<DnssecHealthResult> GetDnssecHealthAsync(AsciiDomainName domain, CancellationToken cancellationToken)
    {
        var domainName = domain.DomainName;

        // Not a zone cut? (e.g. a managed domain living inside our apex zone) Then
        // DNSSEC is the enclosing zone's business and there is nothing to configure here
        var zoneApex = await authoritativeDnsLookup.LookupZoneApexAsync(domainName, cancellationToken);
        if (zoneApex != domainName)
        {
            return new DnssecHealthResult
            {
                Status = DnsHealthDnssecStatus.Inherited,
                EnclosingZone = zoneApex,
            };
        }

        var dnsKeys = await dnssecLookup.GetZoneDnsKeysAsync(domainName, cancellationToken);
        if (dnsKeys.Count == 0)
        {
            return new DnssecHealthResult { Status = DnsHealthDnssecStatus.ZoneUnsigned };
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
