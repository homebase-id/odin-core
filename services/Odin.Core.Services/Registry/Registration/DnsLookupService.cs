using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using DnsClient;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Services.Configuration;
using Odin.Core.Util;

namespace Odin.Core.Services.Registry.Registration;

#nullable enable
public class DnsLookupService : IDnsLookupService
{
    private readonly ILogger<DnsLookupService> _logger;
    private readonly OdinConfiguration _configuration;
    private readonly IAuthorativeDnsLookup _authorativeDnsLookup;

    public DnsLookupService(
        ILogger<DnsLookupService> logger,
        OdinConfiguration configuration,
        IAuthorativeDnsLookup authorativeDnsLookup)
    {
        _logger = logger;
        _configuration = configuration;
        _authorativeDnsLookup = authorativeDnsLookup;
    }

    //

    public async Task<string> LookupZoneApex(string domain)
    {
        domain = domain.ToLower();
        if (!AsciiDomainNameValidator.TryValidateDomain(domain))
        {
            return "";
        }

        var result = await _authorativeDnsLookup.LookupZoneApex(domain);

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
            Verify = dns.ApexARecord,
            Description = "A Record"
        });

        // ALIAS Apex (if DNS provider supports ALIAS/ANAME/CNAME flattening, e.g. clouldflare)
        result.Add(new DnsConfig
        {
            Type = "ALIAS",
            Name = "",
            Domain = domain,
            Value = dns.ApexAliasRecord,
            Verify = dns.ApexARecord,
            Description = "Apex flattened CNAME / ALIAS / ANAME"
        });

        // CNAME WWW
        result.Add(new DnsConfig
        {
            Type = "CNAME",
            Name = DnsConfigurationSet.PrefixWww,
            Domain = $"{DnsConfigurationSet.PrefixWww}.{domain}",
            Value = dns.WwwCnameTarget == "" ? domain : dns.WwwCnameTarget,
            Verify = dns.WwwCnameTarget == "" ? domain : dns.WwwCnameTarget,
            Description = "WWW CNAME"
        });

        // CNAME CAPI
        result.Add(new DnsConfig
        {
            Type = "CNAME",
            Name = DnsConfigurationSet.PrefixCertApi,
            Domain = $"{DnsConfigurationSet.PrefixCertApi}.{domain}",
            Value = dns.CApiCnameTarget == "" ? domain : dns.CApiCnameTarget,
            Verify = dns.CApiCnameTarget == "" ? domain : dns.CApiCnameTarget,
            Description = "CAPI CNAME"
        });

        // CNAME FILE
        result.Add(new DnsConfig
        {
            Type = "CNAME",
            Name = DnsConfigurationSet.PrefixFile,
            Domain = $"{DnsConfigurationSet.PrefixFile}.{domain}",
            Value = dns.FileCnameTarget == "" ? domain : dns.FileCnameTarget,
            Verify = dns.FileCnameTarget == "" ? domain : dns.FileCnameTarget,
            Description = "FILE CNAME"
        });

        return result;
    }

    //

    public async Task<(bool, List<DnsConfig>)> GetAuthorativeDomainDnsStatus(string domain)
    {
        AsciiDomainNameValidator.AssertValidDomain(domain);

        var dnsConfigs = GetDnsConfiguration(domain);
        var authorativeServer = await _authorativeDnsLookup.LookupNameServer(domain);
        if (string.IsNullOrEmpty(authorativeServer))
        {
            foreach (var record in dnsConfigs)
            {
                record.Status = DnsLookupRecordStatus.NoAuthorativeNameServer;
            }
            return (false, dnsConfigs);
        }

        var dnsClient = await CreateDnsClient(authorativeServer);
        foreach (var record in dnsConfigs)
        {
            var recordStatus = await VerifyDnsRecord(domain, record, dnsClient);
            record.QueryResults[authorativeServer] = recordStatus;
            if (record.Status is DnsLookupRecordStatus.Unknown or not DnsLookupRecordStatus.Success)
            {
                record.Status = recordStatus;
            }
        }

        foreach (var record in dnsConfigs)
        {
            if (record.Status != DnsLookupRecordStatus.Success)
            {
                return (false, dnsConfigs);
            }
        }

        // HURRAH!
        return (true, dnsConfigs);
    }

    //

    public async Task<(bool, List<DnsConfig>)> GetExternalDomainDnsStatus(string domain)
    {
        AsciiDomainNameValidator.AssertValidDomain(domain);

        var dnsConfigs = GetDnsConfiguration(domain);
        var resolvers = new List<string>(_configuration.Registry.DnsResolvers);
        foreach (var resolver in resolvers)
        {
            var dnsClient = await CreateDnsClient(resolver);
            foreach (var record in dnsConfigs)
            {
                var recordStatus = await VerifyDnsRecord(domain, record, dnsClient);
                record.QueryResults[resolver] = recordStatus;
                if (record.Status is DnsLookupRecordStatus.Unknown or not DnsLookupRecordStatus.Success)
                {
                    record.Status = recordStatus;
                }
            }
        }

        foreach (var record in dnsConfigs)
        {
            if (record.Status != DnsLookupRecordStatus.Success)
            {
                return (false, dnsConfigs);
            }
        }

        // HURRAH!
        return (true, dnsConfigs);
    }

    //

    public async Task<bool> IsManagedDomainAvailable(string prefix, string apex)
    {
        var domain = prefix + "." + apex;
        AsciiDomainNameValidator.AssertValidDomain(domain);
        AssertManagedDomainApexAndPrefix(prefix, apex);

        var dnsClient = await CreateDnsClient(_configuration.Registry.PowerDnsHostAddress);
        var dnsConfig = GetDnsConfiguration(domain);

        var recordTypes = new[] { QueryType.A, QueryType.CNAME, QueryType.SOA, QueryType.AAAA };

        if (await DnsRecordsOfTypeExists(domain, recordTypes, dnsClient))
        {
            return false;
        }

        foreach (var record in dnsConfig)
        {
            if (record.Name != "")
            {
                if (await DnsRecordsOfTypeExists(record.Name + "." + domain, recordTypes, dnsClient))
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
                $"Managed domain prefix {prefix} has incorret label count. Expected:{managedApex.PrefixLabels.Count}, was:{labelCount},  ");
        }
    }

    //

    private async Task<DnsLookupRecordStatus> VerifyDnsRecord(string domain, DnsConfig dnsConfig, IDnsQuery dnsClient)
    {
        var sw = new Stopwatch();
        sw.Start();

        domain = domain.Trim();
        if (dnsConfig.Name != "")
        {
            domain = dnsConfig.Name + "." + domain;
        }

        List<string> entries;
        IDnsQueryResponse response;

        switch (dnsConfig.Type.ToUpper())
        {
            case "A":
            case "ALIAS":
                response = await dnsClient.QueryAsync(domain, QueryType.A);
                entries = response.Answers.ARecords().Select(x => x.Address.ToString()).ToList();
                break;
            case "CNAME":
                response = await dnsClient.QueryAsync(domain, QueryType.CNAME);
                entries = response.Answers.CnameRecords()
                    .Select(x => x.CanonicalName.ToString()!.TrimEnd('.'))
                    .ToList();
                break;
            default:
                throw new OdinSystemException($"Record type not supported: {dnsConfig.Type}");
        }

        _logger.LogDebug("DNS lookup {domain} {type} @{address} {elapsed}ms: {answer}",
            domain,
            dnsConfig.Type,
            response.NameServer.Address,
            sw.ElapsedMilliseconds,
            string.Join(" ; ", response.Answers));

        if (entries.Count == 0)
        {
            return DnsLookupRecordStatus.DomainOrRecordNotFound;
        }
        if (entries.Contains(dnsConfig.Verify))
        {
            return DnsLookupRecordStatus.Success;
        }
        return DnsLookupRecordStatus.IncorrectValue;
    }

    //

    private async Task<bool> DnsRecordsOfTypeExists(string domain, QueryType[] recordTypes, ILookupClient dnsClient)
    {
        foreach (var recordType in recordTypes)
        {
            var response = await dnsClient.QueryAsync(domain, recordType);
            if (response.Answers.Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    //

    private static async Task<ILookupClient> CreateDnsClient(string resolverAddressOrHostName)
    {
        if (resolverAddressOrHostName == "")
        {
            return new LookupClient(new LookupClientOptions
            {
                UseCache = false,
            });
        }

        if (IPAddress.TryParse(resolverAddressOrHostName, out var nameServerIp))
        {
            return new LookupClient(nameServerIp);
        }

        var ips = await System.Net.Dns.GetHostAddressesAsync(resolverAddressOrHostName);
        nameServerIp = ips.First();

        var options = new LookupClientOptions(nameServerIp)
        {
            UseCache = false,
        };

        return new LookupClient(options);
    }

    //
}