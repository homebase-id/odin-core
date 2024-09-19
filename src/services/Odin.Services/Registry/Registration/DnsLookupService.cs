using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using DnsClient;
using Microsoft.Extensions.Logging;
using Odin.Core.Dns;
using Odin.Core.Exceptions;
using Odin.Core.Util;
using Odin.Services.Configuration;

namespace Odin.Services.Registry.Registration;

#nullable enable
public class DnsLookupService : IDnsLookupService
{
    private readonly ILogger<DnsLookupService> _logger;
    private readonly OdinConfiguration _configuration;
    private readonly ILookupClient _dnsClient;
    private readonly IAuthoritativeDnsLookup _authoritativeDnsLookup;

    public DnsLookupService(
        ILogger<DnsLookupService> logger,
        OdinConfiguration configuration,
        ILookupClient dnsClient,
        IAuthoritativeDnsLookup authoritativeDnsLookup)
    {
        _logger = logger;
        _configuration = configuration;
        _dnsClient = dnsClient;
        _authoritativeDnsLookup = authoritativeDnsLookup;
    }

    //

    public async Task<string> LookupZoneApex(string domain)
    {
        domain = domain.ToLower();
        if (!AsciiDomainNameValidator.TryValidateDomain(domain))
        {
            return "";
        }

        var result = await _authoritativeDnsLookup.LookupZoneApex(domain);

        return result;
    }

    //

    public List<DnsConfig> GetDnsConfiguration(string domain)
    {
        AsciiDomainNameValidator.AssertValidDomain(domain);

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
            Domain = domain,
            Value = dns.ApexARecord,
            AltValue = dns.ApexARecord,
            Description = "A Record"
        });

        // ALIAS Apex (if DNS provider supports ALIAS/ANAME/CNAME flattening, e.g. clouldflare)
        result.Add(new DnsConfig
        {
            Type = "ALIAS",
            Name = "",
            Domain = domain,
            Value = dns.ApexAliasRecord,
            AltValue = dns.ApexAliasRecord,
            Description = "Apex flattened CNAME / ALIAS / ANAME"
        });

        // CNAME CAPI
        result.Add(new DnsConfig
        {
            Type = "CNAME",
            Name = DnsConfigurationSet.PrefixCertApi,
            Domain = $"{DnsConfigurationSet.PrefixCertApi}.{domain}",
            Value = dns.ApexAliasRecord,
            AltValue = domain,
            Description = "CAPI CNAME"
        });

        // CNAME FILE
        result.Add(new DnsConfig
        {
            Type = "CNAME",
            Name = DnsConfigurationSet.PrefixFile,
            Domain = $"{DnsConfigurationSet.PrefixFile}.{domain}",
            Value = dns.ApexAliasRecord,
            AltValue = domain,
            Description = "FILE CNAME"
        });

        return result;
    }

    //

    public async Task<(bool, List<DnsConfig>)> GetAuthoritativeDomainDnsStatus(string domain)
    {
        AsciiDomainNameValidator.AssertValidDomain(domain);

        var dnsConfigs = GetDnsConfiguration(domain);
        var authority = await _authoritativeDnsLookup.LookupDomainAuthority(domain);
        if (string.IsNullOrEmpty(authority.AuthoritativeNameServer))
        {
            foreach (var record in dnsConfigs)
            {
                record.Status = DnsLookupRecordStatus.NoAuthoritativeNameServer;
            }
            return (false, dnsConfigs);
        }

        var queryOptions = new DnsQueryOptions
        {
            Recursion = false,
            UseCache = false,
        };
        foreach (var record in dnsConfigs)
        {
            var (recordStatus, records) = await VerifyDnsRecord(
                authority.NameServers,
                queryOptions,
                domain,
                record.Name,
                record.Type,
                record.Value,
                record.AltValue);

            record.QueryResults[authority.AuthoritativeNameServer] = recordStatus;
            record.Records[authority.AuthoritativeNameServer] = records.ToArray();
            record.Status = recordStatus;
        }

        var result = AreDnsLookupsSuccessful(dnsConfigs);
        return (result, dnsConfigs);
    }

    //

    public async Task<(bool, List<DnsConfig>)> GetExternalDomainDnsStatus(string domain)
    {
        AsciiDomainNameValidator.AssertValidDomain(domain);

        var dnsConfigs = GetDnsConfiguration(domain);
        var resolvers = new List<string>(_configuration.Registry.DnsResolvers);
        var queryOptions = new DnsQueryOptions
        {
            Recursion = true,
            UseCache = false,
        };
        foreach (var resolver in resolvers)
        {
            foreach (var record in dnsConfigs)
            {
                var (recordStatus, records) = await VerifyDnsRecord(
                    new [] {resolver},
                    queryOptions,
                    domain,
                    record.Name,
                    record.Type,
                    record.Value,
                    record.AltValue);

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

    private static bool AreDnsLookupsSuccessful(IReadOnlyCollection<DnsConfig> dnsConfigs)
    {
        // Only one of records A or ALIAS need to be successful
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

    public async Task<bool> IsManagedDomainAvailable(string prefix, string apex)
    {
        var domain = prefix + "." + apex;
        AsciiDomainNameValidator.AssertValidDomain(domain);
        AssertManagedDomainApexAndPrefix(prefix, apex);

        var resolver = _configuration.Registry.PowerDnsHostAddress;
        var dnsConfig = GetDnsConfiguration(domain);

        var recordTypes = new[] { QueryType.A, QueryType.CNAME, QueryType.SOA, QueryType.AAAA };

        if (await DnsRecordsOfTypeExists(resolver, domain, recordTypes))
        {
            return false;
        }

        foreach (var record in dnsConfig)
        {
            if (record.Name != "")
            {
                if (await DnsRecordsOfTypeExists(resolver, record.Name + "." + domain, recordTypes))
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

    private async Task<(DnsLookupRecordStatus, List<string> records)> VerifyDnsRecord(
        IReadOnlyCollection<string> resolvers,
        DnsQueryOptions options,
        string domain,
        string label,
        string type,
        string expectedValue,
        string expectedAltValue)
    {
        var result = DnsLookupRecordStatus.Unknown;
        List<string> records;

        var sw = new Stopwatch();
        sw.Start();

        domain = domain.Trim().ToLower();
        label = label.Trim().ToLower();
        if (label != "")
        {
            domain = label + "." + domain;
        }

        // Bail if any AAAA records on domain
        var recordType = QueryType.AAAA;
        var response = await _dnsClient.Query(resolvers, domain, recordType, options, _logger);
        if (response?.Answers.AaaaRecords().Any() == true)
        {
            records = response.Answers.AaaaRecords().Select(x => x.Address.ToString()).ToList() ?? [];
            result = DnsLookupRecordStatus.AaaaRecordsNotSupported;
        }
        else
        {
            switch (type)
            {
                case "A":
                    recordType = QueryType.A;
                    response = await _dnsClient.Query(resolvers, domain, recordType, options, _logger);
                    records = response?.Answers.ARecords().Select(x => x.Address.ToString()).ToList() ?? [];
                    result = VerifyDnsValue(records, expectedValue, expectedAltValue);
                    break;

                case "ALIAS":
                case "CNAME":
                    recordType = QueryType.CNAME;
                    response = await _dnsClient.Query(resolvers, domain, recordType, options, _logger);
                    records = response?.Answers.CnameRecords().Select(x => x.CanonicalName.ToString()!.TrimEnd('.')).ToList() ?? [];
                    result = VerifyDnsValue(records, expectedValue, expectedAltValue);
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
        var record = records.First();
        if (record != expectedValue && record != expectedAltValue)
        {
            return DnsLookupRecordStatus.IncorrectValue;
        }
        return DnsLookupRecordStatus.Success;
    }

    //

    private async Task<bool> DnsRecordsOfTypeExists(string resolver, string domain, QueryType[] recordTypes)
    {
        var dnsQueryOptions = new DnsQueryOptions
        {
            Recursion = false,
            UseCache = false,
        };

        foreach (var recordType in recordTypes)
        {
            var response = await _dnsClient.Query(resolver, domain, recordType, dnsQueryOptions);
            if (response.Answers.Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    //
}