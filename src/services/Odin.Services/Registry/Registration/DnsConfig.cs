using System.Collections.Generic;

namespace Odin.Services.Registry.Registration;

// NOTE: Frontend depends on this class layout, so be careful when changing it
public class DnsConfig
{
    public string Type { get; init; } = ""; // e.g. "CNAME"
    public string Name { get; init; } = ""; // e.g. "file" or ""
    public string Domain { get; init; } = ""; // e.g. "file.example.com" or "example.com"
    public string Value { get; init; } = ""; // e.g. "example.com" or "127.0.0.1"
    public string AltValue { get; init; } = ""; // For backwards compatibility using CNAME => CNAME => A
    public string Description { get; init; } = "";

    /// <summary>
    /// True for records that are NOT part of <see cref="DnsLookupService.IsDomainDnsReady"/> -
    /// the "is this domain wired to this server" verdict that gates certificate issuance,
    /// zone creation and provisioning readiness.
    ///
    /// Set on www-style extras and on the whole email set (MX, SPF, DKIM, DMARC, MTA-STS,
    /// TLS-RPT). Those records say nothing about whether the domain points here, so a
    /// failing one must never block a certificate - a DKIM typo would otherwise take the
    /// identity's HTTPS down. It does NOT mean the record is unimportant: email records are
    /// enforced in the owner console's Email tab and the monthly security health report.
    ///
    /// NAMING: "Optional" reads as "does not matter", which has already misled readers -
    /// it means "not a prerequisite for certificates". A better name would be
    /// ExcludedFromDnsReadiness. NOT renamed because this field is serialized to clients
    /// (owner console, provisioning app), so the wire name cannot change without a
    /// coordinated frontend release. Rename both sides together if you touch this.
    ///
    /// Additive field - old frontends ignore it.
    /// </summary>
    public bool Optional { get; init; }

    public DnsLookupRecordStatus Status { get; set; } = DnsLookupRecordStatus.Unknown;

    public Dictionary<string, DnsLookupRecordStatus> QueryResults { get; } = new (); // query results per DNS ip address
    public Dictionary<string, string[]> Records { get; } = new (); // parsed records per DNS ip address
}
