#nullable enable
using System.Collections.Generic;
using System.Linq;
using Odin.Core.Util;

namespace Odin.Services.Registry.Registration;

public class DnsConfigurationSet
{
    public const string PrefixCertApi = "capi";
    public const string PrefixFile = "file";
    public static readonly string[] WellknownPrefixes = { PrefixCertApi, PrefixFile };

    // Optional prefixes are host names an identity answers on only when the corresponding
    // feature is enabled (mta-sts: Email:TenantMail). Deliberately NOT in WellknownPrefixes:
    // that list drives the required-CNAME records and the certificate SAN baseline, and an
    // optional feature must never make those mandatory.
    public const string PrefixMtaSts = "mta-sts";
    public static readonly string[] OptionalPrefixes = { PrefixMtaSts };

    public string ApexARecord { get; } // SEB:NOTE we currently only allow one A record
    public string ApexAliasRecord { get; }

    /// <summary>
    /// Our authoritative nameserver hostnames (e.g. ns1.example.com, ns2.example.com).
    /// When configured, BYOD identities get a pre-provisioned zone and can delegate
    /// their domain to us with NS records instead of creating individual records.
    /// Empty list disables all zone provisioning and NS instructions.
    /// </summary>
    public List<string> NameServers { get; }

    /// <summary>
    /// Email used in the SOA record of zones we create. Only relevant when NameServers is set.
    /// </summary>
    public string SoaAdminEmail { get; }

    //

    public DnsConfigurationSet(
        string apexARecord,
        string apexAliasRecord,
        IEnumerable<string>? nameServers = null,
        string soaAdminEmail = "")
    {
        ApexARecord = apexARecord;
        ApexAliasRecord = apexAliasRecord;
        AsciiDomainNameValidator.AssertValidDomain(ApexAliasRecord);

        NameServers = (nameServers ?? [])
            .Select(x => x.Trim().TrimEnd('.').ToLower())
            .Where(x => x != "")
            .ToList();
        foreach (var nameServer in NameServers)
        {
            AsciiDomainNameValidator.AssertValidDomain(nameServer);
        }

        SoaAdminEmail = soaAdminEmail.Trim();
    }

    //

}
