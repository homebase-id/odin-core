using System;
using System.Collections.Generic;
using System.Security.Cryptography;
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

    /// <summary>
    /// True when the zone that would hold the domain's DS record - the enclosing zone of
    /// the domain's parent name - is DNSSEC-signed. Robust against empty non-terminals:
    /// for john.aage.sebbarg.net (no zone cut at aage) the delegating zone is
    /// sebbarg.net, and that is the zone whose signing state matters.
    /// </summary>
    Task<bool> IsParentZoneSignedAsync(string domain, CancellationToken cancellationToken = default);

    /// <summary>
    /// The DNSKEY records the zone serves at its own authority (empty when unsigned).
    /// </summary>
    Task<List<DnsKeyRecord>> GetZoneDnsKeysAsync(string zone, CancellationToken cancellationToken = default);

    /// <summary>
    /// The CDS records (RFC 7344) the zone publishes - the DS set the zone ASKS its
    /// parent to install. Best-effort: empty when absent or unparseable.
    /// </summary>
    Task<List<DsRecordData>> GetCdsRecordsAsync(string zone, CancellationToken cancellationToken = default);
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

    //

    public async Task<bool> IsParentZoneSignedAsync(string domain, CancellationToken cancellationToken = default)
    {
        domain = domain.Trim().Trim('.').ToLowerInvariant();
        var idx = domain.IndexOf('.');
        if (idx < 0)
        {
            // A TLD's parent is the root, which is signed
            return true;
        }
        var parentName = domain[(idx + 1)..];

        // The zone that would hold the domain's DS is the ENCLOSING zone of the parent
        // name - not necessarily the parent name itself (empty non-terminals have no
        // zone of their own; the authority walk lands on the delegating zone)
        var authority = await authoritativeDnsLookup.LookupDomainAuthorityAsync(parentName, cancellationToken);
        if (string.IsNullOrEmpty(authority.AuthoritativeNameServer) ||
            string.IsNullOrEmpty(authority.AuthoritativeDomain))
        {
            return false;
        }

        var response = await dnsClient.Query(
            authority.NameServers, authority.AuthoritativeDomain, QueryType.DNSKEY, AuthoritativeQueryOptions, logger,
            cancellationToken: cancellationToken);

        var signed = response?.Answers.OfType<DnsKeyRecord>().Any() == true;
        logger.LogDebug("Parent zone signed check {domain} (zone {zone}): {signed}",
            domain, authority.AuthoritativeDomain, signed);
        return signed;
    }

    //

    public async Task<List<DnsKeyRecord>> GetZoneDnsKeysAsync(string zone, CancellationToken cancellationToken = default)
    {
        zone = zone.Trim().Trim('.').ToLowerInvariant();

        var authority = await authoritativeDnsLookup.LookupDomainAuthorityAsync(zone, cancellationToken);
        if (string.IsNullOrEmpty(authority.AuthoritativeNameServer))
        {
            return [];
        }

        var response = await dnsClient.Query(
            authority.NameServers, zone, QueryType.DNSKEY, AuthoritativeQueryOptions, logger, cancellationToken: cancellationToken);

        return (response?.Answers.OfType<DnsKeyRecord>() ?? []).ToList();
    }

    //

    private const int CdsRecordType = 59; // RFC 7344; not in DnsClient's QueryType enum

    public async Task<List<DsRecordData>> GetCdsRecordsAsync(string zone, CancellationToken cancellationToken = default)
    {
        zone = zone.Trim().Trim('.').ToLowerInvariant();
        try
        {
            var authority = await authoritativeDnsLookup.LookupDomainAuthorityAsync(zone, cancellationToken);
            if (string.IsNullOrEmpty(authority.AuthoritativeNameServer))
            {
                return [];
            }

            var response = await dnsClient.Query(
                authority.NameServers, zone, (QueryType)CdsRecordType, AuthoritativeQueryOptions, logger,
                cancellationToken: cancellationToken);

            // DnsClient has no CDS parser; the records arrive as UnknownRecord with raw
            // RDATA, which shares the DS wire format: keytag(2) algorithm(1) digesttype(1) digest
            var result = new List<DsRecordData>();
            foreach (var record in response?.Answers.OfType<UnknownRecord>() ?? [])
            {
                if (record.RecordType != (ResourceRecordType)CdsRecordType)
                {
                    continue;
                }
                var data = record.Data.ToArray();
                if (data.Length < 5)
                {
                    continue;
                }
                var keyTag = (data[0] << 8) | data[1];
                var digest = Convert.ToHexString(data[4..]).ToLowerInvariant();
                result.Add(new DsRecordData(keyTag, data[2], data[3], digest));
            }
            return result;
        }
        catch (Exception e)
        {
            // Strictly best-effort: computation from DNSKEY is the universal path
            logger.LogDebug("CDS lookup for {zone} failed: {error}", zone, e.Message);
            return [];
        }
    }

    //

    /// <summary>
    /// Derives the DS record for a DNSKEY (RFC 4034: key tag per Appendix B, digest over
    /// the canonical owner name followed by the DNSKEY RDATA). This is what makes the
    /// generic panel possible: the DS a user must publish is computable from the zone's
    /// PUBLIC key material - no privileged DNS-server API required.
    /// Supported digest types: 1 (SHA-1, legacy - only for matching existing parent
    /// records, never suggested), 2 (SHA-256), 4 (SHA-384).
    /// </summary>
    public static DsRecordData ComputeDsFromDnsKey(string ownerName, DnsKeyRecord dnsKey, byte digestType = 2)
    {
        var rdata = DnsKeyRdata(dnsKey);

        var buffer = new List<byte>();
        buffer.AddRange(CanonicalWireName(ownerName));
        buffer.AddRange(rdata);
#pragma warning disable CA5350 // legacy digest type 1 exists in the wild; matching only
        byte[] digest = digestType switch
        {
            1 => SHA1.HashData(buffer.ToArray()),
            2 => SHA256.HashData(buffer.ToArray()),
            4 => SHA384.HashData(buffer.ToArray()),
            _ => throw new ArgumentOutOfRangeException(nameof(digestType), digestType, "Unsupported DS digest type"),
        };
#pragma warning restore CA5350

        return new DsRecordData(
            ComputeKeyTag(rdata),
            (byte)dnsKey.Algorithm,
            digestType,
            Convert.ToHexString(digest).ToLowerInvariant());
    }

    // RFC 4034 Appendix B (for algorithms other than the obsolete 1)
    internal static int ComputeKeyTag(byte[] rdata)
    {
        var ac = 0;
        for (var i = 0; i < rdata.Length; i++)
        {
            ac += (i & 1) == 1 ? rdata[i] : rdata[i] << 8;
        }
        ac += (ac >> 16) & 0xFFFF;
        return ac & 0xFFFF;
    }

    private static byte[] DnsKeyRdata(DnsKeyRecord dnsKey)
    {
        var rdata = new byte[4 + dnsKey.PublicKey.Count];
        rdata[0] = (byte)(dnsKey.Flags >> 8);
        rdata[1] = (byte)(dnsKey.Flags & 0xFF);
        rdata[2] = dnsKey.Protocol;
        rdata[3] = (byte)dnsKey.Algorithm;
        for (var i = 0; i < dnsKey.PublicKey.Count; i++)
        {
            rdata[4 + i] = dnsKey.PublicKey[i];
        }
        return rdata;
    }

    // RFC 4034 6.2: canonical form - lowercase, uncompressed wire format
    internal static byte[] CanonicalWireName(string name)
    {
        var result = new List<byte>();
        foreach (var label in name.Trim().Trim('.').ToLowerInvariant().Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var bytes = System.Text.Encoding.ASCII.GetBytes(label);
            result.Add((byte)bytes.Length);
            result.AddRange(bytes);
        }
        result.Add(0);
        return result.ToArray();
    }
}
