using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DnsClient;
using DnsClient.Protocol;
using Microsoft.Extensions.Logging;

namespace Odin.Core.Dns;
#nullable enable

/// <summary>
/// A DS record as the parent-side DNSSEC anchor for a zone: exactly the tuple a user
/// enters at a registrar (apex) or as a DS record at their DNS host (subdomain).
/// </summary>
public sealed record DsRecordData(int KeyTag, byte Algorithm, byte DigestType, string Digest)
{
    /// <summary>
    /// Parses PowerDNS/BIND presentation format: "&lt;keytag&gt; &lt;algorithm&gt; &lt;digesttype&gt; &lt;digest&gt;"
    /// (the digest may contain spaces in some outputs; they are stripped). Returns null when malformed.
    /// </summary>
    public static DsRecordData? TryParse(string presentation)
    {
        var parts = presentation.Trim().Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4)
        {
            return null;
        }
        if (!int.TryParse(parts[0], out var keyTag) ||
            !byte.TryParse(parts[1], out var algorithm) ||
            !byte.TryParse(parts[2], out var digestType))
        {
            return null;
        }
        var digest = string.Concat(parts[3..]).ToLowerInvariant();
        return digest.Length == 0 ? null : new DsRecordData(keyTag, algorithm, digestType, digest);
    }

    /// <summary>
    /// Same key material? Digest comparison is case-insensitive (hex).
    /// </summary>
    public bool Matches(DsRecordData other)
    {
        return KeyTag == other.KeyTag
               && Algorithm == other.Algorithm
               && DigestType == other.DigestType
               && string.Equals(Digest, other.Digest, System.StringComparison.OrdinalIgnoreCase);
    }
}

public interface IDnssecLookup
{
    /// <summary>
    /// The DS records the PARENT zone publishes for the domain (empty when none).
    /// Queried at the parent's authoritative servers with recursion and caching off -
    /// DS records live at the parent, next to the delegation NS records, so this works
    /// no matter who hosts the child zone.
    /// </summary>
    Task<List<DsRecordData>> GetParentDsRecordsAsync(string domain, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when the zone publishes DNSKEY records at its own authority, i.e. it is
    /// DNSSEC-signed. For the chain of trust to extend BELOW a zone, that zone must be
    /// signed - an unsigned parent means a child's DS cannot chain at all.
    /// </summary>
    Task<bool> IsZoneSignedAsync(string zone, CancellationToken cancellationToken = default);
}

/// <summary>
/// Generic, config-free DNSSEC lookups against public DNS - deliberately independent of
/// any specific DNS server product. Shared by the provisioning-host status service and
/// the (future) owner-console panel; see docs/owner-console-dnssec-panel-plan.md.
/// All queries are authoritative-only (no recursion, no cache) - cache-safe by construction.
/// </summary>
public class DnssecLookup(ILogger<DnssecLookup> logger, ILookupClient dnsClient, IAuthoritativeDnsLookup authoritativeDnsLookup)
    : IDnssecLookup
{
    private static readonly DnsQueryOptions AuthoritativeQueryOptions = new()
    {
        Recursion = false,
        UseCache = false,
    };

    //

    public async Task<List<DsRecordData>> GetParentDsRecordsAsync(string domain, CancellationToken cancellationToken = default)
    {
        domain = domain.Trim().Trim('.').ToLowerInvariant();
        var idx = domain.IndexOf('.');
        if (idx < 0)
        {
            // A TLD's DS lives at the root; not our use case
            return [];
        }
        var parent = domain[(idx + 1)..];

        var authority = await authoritativeDnsLookup.LookupDomainAuthorityAsync(parent, cancellationToken);
        if (string.IsNullOrEmpty(authority.AuthoritativeNameServer))
        {
            return [];
        }

        var response = await dnsClient.Query(
            authority.NameServers, domain, QueryType.DS, AuthoritativeQueryOptions, logger, cancellationToken: cancellationToken);

        // DS records are authoritative data AT the parent, so they arrive as answers;
        // include the authority section for servers that put them in a referral
        var records = (response?.Answers.OfType<DsRecord>() ?? [])
            .Concat(response?.Authorities.OfType<DsRecord>() ?? [])
            .Select(x => new DsRecordData(x.KeyTag, (byte)x.Algorithm, (byte)x.DigestType, x.DigestAsString.ToLowerInvariant()))
            .Distinct()
            .ToList();

        logger.LogDebug("Parent DS lookup {domain}: [{ds}]", domain, string.Join(';', records));
        return records;
    }

    //

    public async Task<bool> IsZoneSignedAsync(string zone, CancellationToken cancellationToken = default)
    {
        zone = zone.Trim().Trim('.').ToLowerInvariant();

        var authority = await authoritativeDnsLookup.LookupDomainAuthorityAsync(zone, cancellationToken);
        if (string.IsNullOrEmpty(authority.AuthoritativeNameServer))
        {
            return false;
        }

        var response = await dnsClient.Query(
            authority.NameServers, zone, QueryType.DNSKEY, AuthoritativeQueryOptions, logger, cancellationToken: cancellationToken);

        var signed = response?.Answers.OfType<DnsKeyRecord>().Any() == true;
        logger.LogDebug("Zone signed check {zone}: {signed}", zone, signed);
        return signed;
    }
}
