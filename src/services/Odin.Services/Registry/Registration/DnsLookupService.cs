using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DnsClient;
using Microsoft.Extensions.Logging;
using Odin.Core.Dns;
using Odin.Core.Exceptions;
using Odin.Core.Util;
using Odin.Services.Configuration;
using Odin.Services.Email;

namespace Odin.Services.Registry.Registration;

#nullable enable
public class DnsLookupService : IDnsLookupService
{
    private readonly ILogger<DnsLookupService> _logger;
    private readonly OdinConfiguration _configuration;
    private readonly ILookupClient _dnsClient;
    private readonly IAuthoritativeDnsLookup _authoritativeDnsLookup;
    private readonly IDnssecLookup _dnssecLookup;

    public DnsLookupService(
        ILogger<DnsLookupService> logger,
        OdinConfiguration configuration,
        ILookupClient dnsClient,
        IAuthoritativeDnsLookup authoritativeDnsLookup,
        IDnssecLookup dnssecLookup)
    {
        _logger = logger;
        _configuration = configuration;
        _dnsClient = dnsClient;
        _authoritativeDnsLookup = authoritativeDnsLookup;
        _dnssecLookup = dnssecLookup;
    }

    //

    public async Task<string> LookupZoneApexAsync(AsciiDomainName domain, CancellationToken cancellationToken = default)
    {
        return await _authoritativeDnsLookup.LookupZoneApexAsync(domain.DomainName, cancellationToken);
    }

    //

    public List<DnsConfig> GetDnsConfiguration(AsciiDomainName domain)
    {
        var domainName = domain.DomainName;
        var dns = _configuration.Registry.DnsConfigurationSet;

        var result = new List<DnsConfig>();

        // Sanity #1
        if (string.IsNullOrWhiteSpace(dns.ApexARecord))
        {
            throw new OdinSystemException("Missing apex A record. Check config.");
        }

        // Sanity #2
        if (string.IsNullOrWhiteSpace(dns.ApexAliasRecord))
        {
            throw new OdinSystemException("Missing apex alias record. Check config.");
        }
        AsciiDomainNameValidator.AssertValidDomain(dns.ApexAliasRecord);

        // Apex A records
        result.Add(new DnsConfig
        {
            Type = "A",
            Name = "",
            Domain = domainName,
            Value = dns.ApexARecord,
            AltValue = dns.ApexARecord,
            Description = "A Record"
        });

        // ALIAS Apex (if DNS provider supports ALIAS/ANAME/CNAME flattening, e.g. clouldflare)
        result.Add(new DnsConfig
        {
            Type = "ALIAS",
            Name = "",
            Domain = domainName,
            Value = dns.ApexAliasRecord,
            AltValue = dns.ApexAliasRecord,
            Description = "Apex flattened CNAME / ALIAS / ANAME"
        });

        // CNAME CAPI
        result.Add(new DnsConfig
        {
            Type = "CNAME",
            Name = DnsConfigurationSet.PrefixCertApi,
            Domain = $"{DnsConfigurationSet.PrefixCertApi}.{domainName}",
            Value = dns.ApexAliasRecord,
            AltValue = domainName,
            Description = "CAPI CNAME"
        });

        // CNAME FILE
        result.Add(new DnsConfig
        {
            Type = "CNAME",
            Name = DnsConfigurationSet.PrefixFile,
            Domain = $"{DnsConfigurationSet.PrefixFile}.{domainName}",
            Value = dns.ApexAliasRecord,
            AltValue = domainName,
            Description = "FILE CNAME"
        });

        // NS delegation (preferred alternative to the records above; we host the zone).
        // Only offered when this deployment can actually host zones (nameservers configured
        // AND PowerDNS available), and never for managed domains - those live as records in
        // the shared apex zone and must not be delegated anywhere.
        var canHostZones = !string.IsNullOrEmpty(_configuration.Registry.PowerDnsApiKey);
        var nameServers = canHostZones && !IsUnderManagedApex(domainName) ? dns.NameServers : [];
        foreach (var nameServer in nameServers)
        {
            result.Add(new DnsConfig
            {
                Type = "NS",
                Name = "",
                Domain = domainName,
                Value = nameServer,
                AltValue = nameServer,
                Description = "NS Record (delegates DNS for the domain to us)"
            });
        }

        // Email records (docs/email-dns-plan.md), only while tenant mail is enabled.
        // All Optional: they must never join the identity-validation success rule or the
        // certificate DNS gate - the mta-sts CNAME would otherwise fail both for every
        // tenant provisioned before the email era.
        var tenantMail = _configuration.Email.TenantMail;
        if (tenantMail.Enabled)
        {
            for (var i = 0; i < tenantMail.MxNodes.Count; i++)
            {
                var value = $"{(i + 1) * 10} {tenantMail.MxNodes[i]}";
                result.Add(new DnsConfig
                {
                    Type = "MX",
                    Name = "",
                    Domain = domainName,
                    Value = value,
                    AltValue = value,
                    Description = "MX Record (inbound mail)",
                    Optional = true,
                });
            }

            result.Add(EmailTxt(domainName, "",
                $"v=spf1 include:{tenantMail.SpfIncludeTarget} -all", "SPF (authorized senders)"));
            result.Add(EmailTxt(domainName, "_dmarc",
                $"v=DMARC1; p=reject; rua=mailto:{tenantMail.DmarcReportEmail}", "DMARC policy"));
            result.Add(EmailTxt(domainName, "_mta-sts",
                $"v=STSv1; id={MtaStsPolicy.ComputeId(tenantMail.MxNodes)}", "MTA-STS (TLS enforcement)"));
            result.Add(EmailTxt(domainName, "_smtp._tls",
                $"v=TLSRPTv1; rua=mailto:{tenantMail.TlsReportEmail}", "TLS-RPT (TLS failure reports)"));

            result.Add(new DnsConfig
            {
                Type = "CNAME",
                Name = DnsConfigurationSet.PrefixMtaSts,
                Domain = $"{DnsConfigurationSet.PrefixMtaSts}.{domainName}",
                Value = dns.ApexAliasRecord,
                AltValue = domainName,
                Description = "MTA-STS policy host CNAME",
                Optional = true,
            });
        }

        return result;
    }

    //

    private static DnsConfig EmailTxt(string domainName, string name, string value, string description)
    {
        return new DnsConfig
        {
            Type = "TXT",
            Name = name,
            Domain = name == "" ? domainName : $"{name}.{domainName}",
            Value = value,
            AltValue = value,
            Description = description,
            Optional = true,
        };
    }

    //

    public async Task<bool> IsDomainDelegatedToUsAsync(AsciiDomainName domain, CancellationToken cancellationToken = default)
    {
        var ourNameServers = _configuration.Registry.DnsConfigurationSet.NameServers;
        if (ourNameServers.Count == 0)
        {
            return false;
        }

        var nsNames = await GetParentDelegationNameServersAsync(domain, cancellationToken);

        // Delegated to us = a delegation exists and every nameserver in it is one of ours.
        // A mixed set (ours plus a stale third party) is NOT delegated: part of real
        // resolver traffic would be referred to a server that does not serve the zone.
        var result = nsNames.Count > 0 && nsNames.TrueForAll(ns => ourNameServers.Contains(ns));

        _logger.LogDebug("Delegation check {domain}: NS [{ns}] => {result}",
            domain, string.Join(',', nsNames), result);

        return result;
    }

    //

    /// <summary>
    /// The NS names the PARENT zone delegates the domain to (empty when no delegation).
    /// Deliberately never queries our own nameservers: it must work before our zone exists,
    /// and it must reflect what the domain owner actually configured at their DNS host -
    /// asking the child's authority would just echo our own zone's NS rrset.
    /// </summary>
    private async Task<List<string>> GetParentDelegationNameServersAsync(AsciiDomainName domain, CancellationToken cancellationToken)
    {
        // A valid domain always has at least two labels, so there is always a parent.
        // The parent can be a bare TLD - not a valid AsciiDomainName itself - which is
        // why the authority lookup below deliberately works on plain strings.
        var domainName = domain.DomainName;
        var parent = domainName[(domainName.IndexOf('.') + 1)..];

        var authority = await _authoritativeDnsLookup.LookupDomainAuthorityAsync(parent, cancellationToken);
        if (string.IsNullOrEmpty(authority.AuthoritativeNameServer))
        {
            return [];
        }

        var options = new DnsQueryOptions
        {
            Recursion = false,
            UseCache = false,
        };
        var response = await _dnsClient.Query(
            authority.NameServers, domainName, QueryType.NS, options, _logger, cancellationToken: cancellationToken);

        // Delegation NS records come back as a referral (Authority section) or, if the
        // parent's server is also authoritative for the child, as answers
        return (response?.Answers.NsRecords() ?? [])
            .Concat(response?.Authorities.NsRecords() ?? [])
            .Select(x => x.NSDName.ToString()!.TrimEnd('.').ToLower())
            .Distinct()
            .ToList();
    }

    //

    public async Task<(bool, List<DnsConfig>)> GetAuthoritativeDomainDnsStatusAsync(AsciiDomainName domain, CancellationToken cancellationToken = default)
    {
        var dnsConfigs = GetDnsConfiguration(domain);
        var authority = await _authoritativeDnsLookup.LookupDomainAuthorityAsync(domain.DomainName, cancellationToken);
        if (string.IsNullOrEmpty(authority.AuthoritativeNameServer))
        {
            foreach (var record in dnsConfigs)
            {
                record.Status = DnsLookupRecordStatus.NoAuthoritativeNameServer;
            }
            return (false, dnsConfigs);
        }

        // NS entries are verified against the PARENT's delegation records - the child's
        // authority (once delegated, our own zone) would just echo our own NS rrset and a
        // stale extra nameserver in the delegation would go unnoticed.
        var nsEntries = dnsConfigs.Where(x => x.Type == "NS").ToList();
        if (nsEntries.Count > 0)
        {
            var ourNameServers = _configuration.Registry.DnsConfigurationSet.NameServers;
            var delegationNs = await GetParentDelegationNameServersAsync(domain, cancellationToken);
            var allOurs = delegationNs.Count > 0 && delegationNs.TrueForAll(ns => ourNameServers.Contains(ns));
            foreach (var entry in nsEntries)
            {
                var status =
                    delegationNs.Count == 0 || !delegationNs.Contains(entry.Value)
                        ? DnsLookupRecordStatus.DomainOrRecordNotFound
                        : allOurs
                            ? DnsLookupRecordStatus.Success
                            // Our NS is present but a foreign one is too - the delegation is wrong
                            : DnsLookupRecordStatus.IncorrectValue;

                entry.QueryResults["delegation"] = status;
                entry.Records["delegation"] = delegationNs.ToArray();
                entry.Status = status;
            }
        }

        var queryOptions = new DnsQueryOptions
        {
            Recursion = false,
            UseCache = false,
        };
        foreach (var record in dnsConfigs.Where(x => x.Type != "NS"))
        {
            var (recordStatus, records) = await VerifyDnsRecordAsync(
                authority.NameServers,
                queryOptions,
                domain.DomainName,
                record.Name,
                record.Type,
                record.Value,
                record.AltValue,
                cancellationToken);

            record.QueryResults[authority.AuthoritativeNameServer] = recordStatus;
            record.Records[authority.AuthoritativeNameServer] = records.ToArray();
            record.Status = recordStatus;
        }

        var result = AreDnsLookupsSuccessful(dnsConfigs);
        return (result, dnsConfigs);
    }

    //

    public async Task<(bool, List<DnsConfig>)> GetExternalDomainDnsStatusAsync(AsciiDomainName domain, CancellationToken cancellationToken = default)
    {
        var dnsConfigs = GetDnsConfiguration(domain);
        var resolvers = new List<string>(_configuration.Registry.DnsResolvers);
        var queryOptions = new DnsQueryOptions
        {
            Recursion = true,
            UseCache = false,
        };
        foreach (var resolver in resolvers)
        {
            // NS entries are excluded: delegation is checked authoritatively at the parent
            // (GetAuthoritativeDomainDnsStatusAsync); recursive resolvers would just return
            // the child zone's own NS rrset
            foreach (var record in dnsConfigs.Where(x => x.Type != "NS"))
            {
                var (recordStatus, records) = await VerifyDnsRecordAsync(
                    new [] {resolver},
                    queryOptions,
                    domain.DomainName,
                    record.Name,
                    record.Type,
                    record.Value,
                    record.AltValue,
                    cancellationToken);

                record.QueryResults[resolver] = recordStatus;
                record.Records[resolver] = records.ToArray();
                if (record.Status is DnsLookupRecordStatus.Unknown or not DnsLookupRecordStatus.Success)
                {
                    record.Status = recordStatus;
                }
            }
        }

        var result = AreDnsLookupsSuccessful(dnsConfigs);
        return (result, dnsConfigs);
    }

    //

    // internal for testing
    internal static bool AreDnsLookupsSuccessful(IReadOnlyCollection<DnsConfig> dnsConfigs)
    {
        // Optional records (www-style extras, email records) never affect the verdict -
        // this filter is load-bearing: the certificate DNS gate consumes this rule, and a
        // failing optional CNAME (mta-sts on a manual-records tenant) must not block issuance
        dnsConfigs = dnsConfigs.Where(x => !x.Optional).ToList();

        // Delegated mode: the NS entries verify the PARENT's delegation records (strict,
        // all-ours - see GetAuthoritativeDomainDnsStatusAsync). Verified delegation counts
        // as overall success even before the domain's zone exists on our servers: the zone
        // is created AND populated at the commit points that consume this verdict (the
        // provisioning UI's Provision action, CreateIdentityOnDomainAsync's ensure-net,
        // the CLI backfill), and populate is an idempotent REPLACE, so records exist before
        // anything (certificates, requests) needs them.
        var nsRecords = dnsConfigs.Where(x => x.Type == "NS").ToList();
        if (nsRecords.Count > 0 && nsRecords.TrueForAll(x => x.Status == DnsLookupRecordStatus.Success))
        {
            return true;
        }

        // Manual mode: only one of records A or ALIAS need to be successful
        if (dnsConfigs.Count(x => (x.Type is "A" or "ALIAS") && x.Status == DnsLookupRecordStatus.Success) < 1)
        {
            return false;
        }

        // All CNAME records must be successful
        if (dnsConfigs.Where(x => x.Type == "CNAME").Any(record => record.Status != DnsLookupRecordStatus.Success))
        {
            return false;
        }

        return true;
    }

    //

    // Thin wrappers over the shared generic primitives (Odin.Core.Dns.DnssecLookup):
    // both are public-DNS data, deliberately NOT PowerDNS - see the architectural
    // boundary in docs/byod-dnssec-plan.md

    public Task<List<DsRecordData>> GetParentDsRecordsAsync(AsciiDomainName domain, CancellationToken cancellationToken = default)
    {
        return _dnssecLookup.GetParentDsRecordsAsync(domain.DomainName, cancellationToken);
    }

    public Task<bool> IsParentZoneSignedAsync(AsciiDomainName domain, CancellationToken cancellationToken = default)
    {
        // Enclosing-zone semantics (not literally one label up): for a domain below an
        // empty non-terminal the DS lives in the delegating zone further up
        return _dnssecLookup.IsParentZoneSignedAsync(domain.DomainName, cancellationToken);
    }

    //

    public async Task<bool> IsManagedDomainAvailableAsync(string prefix, string apex, CancellationToken cancellationToken = default)
    {
        var domain = new AsciiDomainName(prefix + "." + apex); // ctor validates
        AssertManagedDomainApexAndPrefix(prefix, apex);

        var resolver = _configuration.Registry.PowerDnsHostAddress;
        var dnsConfig = GetDnsConfiguration(domain);

        var recordTypes = new[] { QueryType.A, QueryType.CNAME, QueryType.SOA, QueryType.AAAA };

        if (await DnsRecordsOfTypeExists(resolver, domain.DomainName, recordTypes, cancellationToken))
        {
            return false;
        }

        // Optional records (email extras) are excluded: this probe is keystroke-sensitive
        // and their names (e.g. _dmarc) cannot pre-exist for a fresh managed prefix anyway
        foreach (var record in dnsConfig.Where(x => !x.Optional))
        {
            if (record.Name != "")
            {
                if (await DnsRecordsOfTypeExists(resolver, record.Name + "." + domain.DomainName, recordTypes, cancellationToken))
                {
                    return false;
                }
            }
        }

        return true;
    }

    //

    public void AssertManagedDomainApexAndPrefix(string prefix, string apex)
    {
        var managedApexes = _configuration.Registry.ManagedDomainApexes;
        var managedApex = managedApexes.Find(x => x.Apex == apex);

        if (managedApex == null)
        {
            throw new OdinSystemException($"Managed domain apex {apex} does not belong here");
        }

        var labelCount = prefix.Count(x => x == '.') + 1;
        if (managedApex.PrefixLabels.Count != labelCount)
        {
            throw new OdinSystemException(
                $"Managed domain prefix {prefix} has incorrect label count. Expected:{managedApex.PrefixLabels.Count}, was:{labelCount},  ");
        }
    }

    //

    private async Task<(DnsLookupRecordStatus, List<string> records)> VerifyDnsRecordAsync(
        IReadOnlyCollection<string> resolvers,
        DnsQueryOptions options,
        string domain,
        string label,
        string type,
        string expectedValue,
        string expectedAltValue,
        CancellationToken cancellationToken = default)
    {
        var result = DnsLookupRecordStatus.Unknown;
        List<string> records = [];

        var sw = new Stopwatch();
        sw.Start();

        domain = domain.Trim().ToLower();
        label = label.Trim().ToLower();
        if (label != "")
        {
            domain = label + "." + domain;
        }

        // Bail if any AAAA records on domain - address/alias records only: MX/TXT at the
        // apex legitimately coexist with AAAA records
        var recordType = QueryType.AAAA;
        IDnsQueryResponse? response = null;
        if (type is "A" or "ALIAS" or "CNAME")
        {
            response = await _dnsClient.Query(resolvers, domain, recordType, options, _logger, cancellationToken: cancellationToken);
            if (response?.Answers.AaaaRecords().Any() == true)
            {
                records = response.Answers.AaaaRecords().Select(x => x.Address.ToString()).ToList() ?? [];
                result = DnsLookupRecordStatus.AaaaRecordsNotSupported;
            }
        }

        if (result != DnsLookupRecordStatus.AaaaRecordsNotSupported)
        {
            switch (type)
            {
                case "A":
                    recordType = QueryType.A;
                    response = await _dnsClient.Query(resolvers, domain, recordType, options, _logger, cancellationToken: cancellationToken);
                    records = response?.Answers.ARecords().Select(x => x.Address.ToString()).ToList() ?? [];
                    result = VerifyDnsValue(records, expectedValue, expectedAltValue);
                    break;

                case "ALIAS":
                case "CNAME":
                    recordType = QueryType.CNAME;
                    response = await _dnsClient.Query(resolvers, domain, recordType, options, _logger, cancellationToken: cancellationToken);
                    records = response?.Answers.CnameRecords().Select(x => x.CanonicalName.ToString()!.TrimEnd('.')).ToList() ?? [];
                    result = VerifyDnsValue(records, expectedValue, expectedAltValue);
                    break;

                case "MX":
                    recordType = QueryType.MX;
                    response = await _dnsClient.Query(resolvers, domain, recordType, options, _logger, cancellationToken: cancellationToken);
                    records = response?.Answers.MxRecords()
                        .Select(x => $"{x.Preference} {x.Exchange.ToString()!.TrimEnd('.')}").ToList() ?? [];
                    result = VerifyDnsValueContained(records, expectedValue);
                    break;

                case "TXT":
                    recordType = QueryType.TXT;
                    response = await _dnsClient.Query(resolvers, domain, recordType, options, _logger, cancellationToken: cancellationToken);
                    // A long TXT value arrives as multiple <=255-byte character strings of one
                    // record; concatenate before comparing
                    records = response?.Answers.TxtRecords().Select(x => string.Concat(x.Text)).ToList() ?? [];
                    result = VerifyDnsValueContained(records, expectedValue);
                    break;

                default:
                    throw new OdinSystemException($"Record type not supported: {type}");
            }
        }

        _logger.LogDebug("DNS lookup {domain} {type} @{address} {elapsed}ms result:{result} answer:{answer}",
            domain,
            recordType,
            response?.NameServer.Address ?? string.Join(',', resolvers),
            sw.ElapsedMilliseconds,
            result,
            response?.Answers.Count > 0 ? string.Join(',', response.Answers) : "");

        // Sanity
        if (result == DnsLookupRecordStatus.Unknown)
        {
            _logger.LogError("Unexpected result '{result}'. Your logic is faulty!", result);
        }

        return (result, records);
    }

    //

    private bool IsUnderManagedApex(string domain)
    {
        return _configuration.Registry.ManagedDomainApexes.Exists(x =>
            domain.Equals(x.Apex, System.StringComparison.OrdinalIgnoreCase) ||
            domain.EndsWith("." + x.Apex, System.StringComparison.OrdinalIgnoreCase));
    }

    //

    private static DnsLookupRecordStatus VerifyDnsValue(
        IReadOnlyCollection<string> records, 
        string expectedValue,
        string expectedAltValue)
    {
        if (records.Count < 1)
        {
            return DnsLookupRecordStatus.DomainOrRecordNotFound;
        }
        if (records.Count > 1)
        {
            return DnsLookupRecordStatus.MultipleRecordsNotSupported;
        }
        var record = records.First().ToLower();
        if (record != expectedValue.ToLower() && record != expectedAltValue.ToLower())
        {
            return DnsLookupRecordStatus.IncorrectValue;
        }
        return DnsLookupRecordStatus.Success;
    }

    //

    // Set containment for record types where multiple values at one name are normal
    // (multi-target MX, a foreign verification TXT beside our SPF at the apex): OUR
    // value must be present; other values are none of our business.
    private static DnsLookupRecordStatus VerifyDnsValueContained(
        IReadOnlyCollection<string> records,
        string expectedValue)
    {
        if (records.Count < 1)
        {
            return DnsLookupRecordStatus.DomainOrRecordNotFound;
        }
        return records.Any(x => string.Equals(x, expectedValue, System.StringComparison.OrdinalIgnoreCase))
            ? DnsLookupRecordStatus.Success
            : DnsLookupRecordStatus.IncorrectValue;
    }

    //

    private async Task<bool> DnsRecordsOfTypeExists(string resolver, string domain, QueryType[] recordTypes, CancellationToken cancellationToken = default)
    {
        var dnsQueryOptions = new DnsQueryOptions
        {
            Recursion = false,
            UseCache = false,
        };

        foreach (var recordType in recordTypes)
        {
            var response = await _dnsClient.Query(resolver, domain, recordType, dnsQueryOptions, cancellationToken: cancellationToken);
            if (response.Answers.Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    //
}