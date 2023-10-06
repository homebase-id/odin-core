using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using DnsClient;
using HttpClientFactoryLite;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Services.Configuration;
using Odin.Core.Services.Dns;
using Odin.Core.Services.Email;
using Odin.Core.Util;
using IHttpClientFactory = HttpClientFactoryLite.IHttpClientFactory;

// Managed Domain: DNS records are managed by e.g. an ISP
// Own Domain: DNS records are managed by end user

namespace Odin.Core.Services.Registry.Registration;

#nullable enable

/// <summary>
/// Handles creating an identity on this host
/// </summary>
public class IdentityRegistrationService : IIdentityRegistrationService
{
    private readonly ILogger<IdentityRegistrationService> _logger;
    private readonly IIdentityRegistry _registry;
    private readonly OdinConfiguration _configuration;
    private readonly IDnsRestClient _dnsRestClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IEmailSender _emailSender;
    private readonly object _mutex = new();

    public IdentityRegistrationService(
        ILogger<IdentityRegistrationService> logger,
        IIdentityRegistry registry,
        OdinConfiguration configuration,
        IDnsRestClient dnsRestClient,
        IHttpClientFactory httpClientFactory,
        IEmailSender emailSender)
    {
        _logger = logger;
        _configuration = configuration;
        _registry = registry;
        _dnsRestClient = dnsRestClient;
        _httpClientFactory = httpClientFactory;
        _emailSender = emailSender;

        RegisterHttpClient();
    }

    //

    public async Task<bool> CanConnectToHostAndPort(string domain, int port)
    {
        try
        {
            // SEB:TODO will we get a TIME_WAIT problem here?
            using var tcpClient = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await tcpClient.ConnectAsync(domain, port, cts.Token);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    //

    public async Task<bool> HasValidCertificate(string domain)
    {
        var httpClient = _httpClientFactory.CreateClient<IdentityRegistrationService>();
        try
        {
            await httpClient.GetAsync($"https://{domain}");
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    //

    public async Task<string> LookupZoneApex(string domain)
    {
        domain = domain.ToLower();
        if (!AsciiDomainNameValidator.TryValidateDomain(domain))
        {
            return "";
        }

        var dnsClient = await CreateDnsClient();

        var labels = domain.Split('.');
        for (var i = 0; i < labels.Length; i++)
        {
            var test = string.Join('.', labels.Skip(i));

            _logger.LogDebug("LookupZoneApex query SOA on {domain}", test);
            var response = await dnsClient.QueryAsync(test, QueryType.SOA);
            foreach (var soa in response.Answers.SoaRecords())
            {
                _logger.LogDebug("LookupZoneApex found SOA on {domain}: {SOA}", test, soa);
                if (soa.DomainName.Value.ToLower() == test + '.')
                {
                    _logger.LogDebug("LookupZoneApex zone apex is {domain}", test);
                    return test;
                }
            }
        }

        _logger.LogError("LookupZoneApex found no zone anywhere in {domain}", domain);
        return "";
    }

    //

    public Task<List<OdinConfiguration.RegistrySection.ManagedDomainApex>> GetManagedDomainApexes()
    {
        return Task.FromResult(_configuration.Registry.ManagedDomainApexes);
    }

    //

    public Task<List<DnsConfig>> GetDnsConfiguration(string domain)
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
            Description = "Apex CNAME with flattening"
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

        return Task.FromResult(result);
    }

    //

    // bash: dig @1.1.1.1 example.com
    public async Task<ExternalDnsResolverLookupResult> ExternalDnsResolverRecordLookup(string domain)
    {
        var result = new ExternalDnsResolverLookupResult();

        var lookups = new List<(string, DnsConfig, Task<bool>)>();

        foreach (var resolver in _configuration.Registry.DnsResolvers)
        {
            var dnsConfig = await GetDnsConfiguration(domain);
            var dnsClient = await CreateDnsClient(resolver);
            foreach (var record in dnsConfig)
            {
                lookups.Add(
                    (
                        resolver,
                        record,
                        VerifyDnsRecord(domain, record, dnsClient, true, 4)
                    ));
            }
        }

        await Task.WhenAll(lookups.Select(x => x.Item3));

        foreach (var lookup in lookups)
        {
            result.Statuses.Add(new ExternalDnsResolverLookupResult.ResolverStatus
            {
                ResolverIp = lookup.Item1,
                Domain = lookup.Item2.Domain,
                Success = lookup.Item2.Status == DnsConfig.LookupRecordStatus.Success
            });
        }

        result.Success = result.Statuses.All(x => x.Success);

        return result;
    }

    //
    // Managed Domain
    //

    public async Task<bool> IsManagedDomainAvailable(string prefix, string apex)
    {
        var domain = prefix + "." + apex;
        AsciiDomainNameValidator.AssertValidDomain(domain);
        await AssertManagedDomainApexAndPrefix(prefix, apex);

        var identity = await _registry.Get(domain);
        if (identity != null)
        {
            // Identity already exists
            return false;
        }

        var dnsClient = await CreateDnsClient(_configuration.Registry.PowerDnsHostAddress);
        var dnsConfig = await GetDnsConfiguration(domain);

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

    public async Task CreateManagedDomain(string prefix, string apex)
    {
        var domain = prefix + "." + apex;
        AsciiDomainNameValidator.AssertValidDomain(domain);
        await AssertManagedDomainApexAndPrefix(prefix, apex);

        var dnsConfig = await GetDnsConfiguration(domain);

        var zoneId = apex + ".";
        foreach (var record in dnsConfig)
        {
            var name = record.Name != "" ? record.Name + "." + prefix : prefix;
            if (record.Type == "A")
            {
                await _dnsRestClient.CreateARecords(zoneId, name, new[] { record.Value });
            }
            else if (record.Type == "CNAME")
            {
                await _dnsRestClient.CreateCnameRecords(zoneId, name, record.Value + ".");
            }
            else if (record.Type == "ALIAS")
            {
                // IGNORE
            }
            else
            {
                // Sanity
                throw new OdinSystemException($"Unsupported record: {record.Type}");
            }
        }
    }

    //

    public async Task DeleteManagedDomain(string prefix, string apex)
    {
        var domain = prefix + "." + apex;
        AsciiDomainNameValidator.AssertValidDomain(domain);
        await AssertManagedDomainApexAndPrefix(prefix, apex);

        await _registry.DeleteRegistration(domain);

        var dnsConfig = await GetDnsConfiguration(domain);

        var zoneId = apex + ".";
        foreach (var record in dnsConfig)
        {
            var name = record.Name != "" ? record.Name + "." + prefix : prefix;
            if (record.Type == "A")
            {
                await _dnsRestClient.DeleteARecords(zoneId, name);
            }
            else if (record.Type == "CNAME")
            {
                await _dnsRestClient.DeleteCnameRecords(zoneId, name);
            }
            else if (record.Type == "ALIAS")
            {
                // IGNORE
            }
            else
            {
                // Sanity
                throw new OdinSystemException($"Unsupported record: {record.Type}");
            }
        }
    }

    //
    // Own Domain
    //

    public async Task<bool> IsOwnDomainAvailable(string domain)
    {
        AsciiDomainNameValidator.AssertValidDomain(domain);

        var identity = await _registry.Get(domain);
        if (identity != null)
        {
            // Identity already exists
            return false;
        }

        return true;
    }

    //

    public async Task<(bool, List<DnsConfig>)> GetOwnDomainDnsStatus(string domain)
    {
        AsciiDomainNameValidator.AssertValidDomain(domain);

        var dnsConfigs = await GetDnsConfiguration(domain);

        var lookups = new List<Task<bool>>();

        var resolvers = new List<string>(_configuration.Registry.DnsResolvers);
        resolvers.Insert(0, ""); // Default system resolver

        foreach (var resolver in resolvers)
        {
            var dnsClient = await CreateDnsClient(resolver);
            foreach (var record in dnsConfigs)
            {
                lookups.Add(VerifyDnsRecord(domain, record, dnsClient, true, 4));
            }
        }

        await Task.WhenAll(lookups);

        foreach (var record in dnsConfigs)
        {
            if (record.Status != DnsConfig.LookupRecordStatus.Success)
            {
                return (false, dnsConfigs);
            }
        }

        // HURRAH!
        return (true, dnsConfigs);
    }

    //

    public async Task DeleteOwnDomain(string domain)
    {
        AsciiDomainNameValidator.AssertValidDomain(domain);
        await _registry.DeleteRegistration(domain);
    }

    //

    public async Task<Guid> CreateIdentityOnDomain(string domain, string email, string planId)
    {
        var identity = await _registry.Get(domain);
        if (identity != null)
        {
            throw new OdinSystemException($"Identity {domain} already exists");
        }

        var request = new IdentityRegistrationRequest()
        {
            OdinId = (OdinId)domain,
            Email = email,
            PlanId = planId,
            IsCertificateManaged = false, //TODO
        };

        try
        {
            var firstRunToken = await _registry.AddRegistration(request);

            if (_configuration.Mailgun.Enabled)
            {
                // SEB:TODO we should probably queue this on a Quartz worker instead
                // and only send it once we're sure the certificate has been created
                await SendProvisioningCompleteEmail(domain, email, firstRunToken.ToString());
            }

            return firstRunToken;
        }
        catch (Exception)
        {
            await _registry.DeleteRegistration(domain);
            throw;
        }
    }

    //

    public Task<bool> IsValidInvitationCode(string code)
    {
        if (!_configuration.Registry.InvitationCodes.Any())
        {
            return Task.FromResult(true);
        }
        
        if (string.IsNullOrEmpty(code))
        {
            return Task.FromResult(false);
        }

        var match = _configuration.Registry.InvitationCodes
            .Exists(c => string.Equals(c, code, StringComparison.InvariantCultureIgnoreCase));
        return Task.FromResult(match);
    }

    //

    private async Task SendProvisioningCompleteEmail(string domain, string email, string firstRunToken)
    {
        const string subject = "Your new identity is ready";
        var firstRunlink = $"https://{domain}/owner/firstrun?frt={firstRunToken}";

        var envelope = new Envelope
        {
            To = new List<NameAndEmailAddress> { new() { Email = email } },
            Subject = subject,
            TextMessage = RegistrationEmails.ProvisioningCompletedText(email, domain, firstRunlink),
            HtmlMessage = RegistrationEmails.ProvisioningCompletedHtml(email, domain, firstRunlink),
        };

        await _emailSender.SendAsync(envelope);
    }

    //        

    private static async Task<ILookupClient> CreateDnsClient(string resolverAddressOrHostName = "")
    {
        // SEB:TODO this should be injected into ctor as a factory instead (remember to disable caching!)

        if (resolverAddressOrHostName == "")
        {
            return new LookupClient();
        }

        if (IPAddress.TryParse(resolverAddressOrHostName, out var nameServerIp))
        {
            return new LookupClient(nameServerIp);
        }

        var ips = await System.Net.Dns.GetHostAddressesAsync(resolverAddressOrHostName);
        nameServerIp = ips.First();

        var options = new LookupClientOptions(nameServerIp)
        {
            UseCache = false
        };

        return new LookupClient(options);
    }

    //

    private async Task<bool> VerifyDnsRecord(
        string domain,
        DnsConfig dnsConfig,
        IDnsQuery dnsClient,
        bool validateConfiguredValue,
        int iterations)
    {
        var sw = new Stopwatch();
        sw.Start();

        domain = domain.Trim();
        if (dnsConfig.Name != "")
        {
            domain = dnsConfig.Name + "." + domain;
        }

        for (var iteration = 0; iteration < iterations; iteration++)
        {
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

            var recordStatus = DnsConfig.LookupRecordStatus.Unknown;
            if (entries.Count == 0)
            {
                recordStatus = DnsConfig.LookupRecordStatus.DomainOrRecordNotFound;
            }
            else
            {
                if (!validateConfiguredValue || entries.Contains(dnsConfig.Verify))
                {
                    recordStatus = DnsConfig.LookupRecordStatus.Success;
                }
                else
                {
                    recordStatus = DnsConfig.LookupRecordStatus.IncorrectValue;
                }
            }

            lock (_mutex)
            {
                if (recordStatus != DnsConfig.LookupRecordStatus.Success)
                {
                    dnsConfig.Status = recordStatus;
                }

                if (!dnsConfig.QueryResults.TryGetValue(response.NameServer.Address, out var queryResult))
                {
                    queryResult = new DnsConfig.QueryResult();
                    dnsConfig.QueryResults[response.NameServer.Address] = queryResult;
                }

                queryResult.TotalCount++;
                if (recordStatus == DnsConfig.LookupRecordStatus.Success)
                {
                    queryResult.SuccessCount++;
                }

                _logger.LogDebug("DNS lookup answer {answer}", response.Answers);
                _logger.LogDebug("DNS lookup result {domain}: {status} ({elapsed}ms using {address})",
                    domain, recordStatus, sw.ElapsedMilliseconds, response.NameServer.Address);
            }
        }

        var totals = 0;
        var successes = 0;
        foreach (var queryResult in dnsConfig.QueryResults.Values)
        {
            totals += queryResult.TotalCount;
            successes += queryResult.SuccessCount;
        }
        if (totals > 0 && successes == totals)
        {
            dnsConfig.Status = DnsConfig.LookupRecordStatus.Success;
        }

        return dnsConfig.Status == DnsConfig.LookupRecordStatus.Success;
    }

    //

    private async Task AssertManagedDomainApexAndPrefix(string prefix, string apex)
    {
        var managedApexes = await GetManagedDomainApexes();
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

    private void RegisterHttpClient()
    {
        _httpClientFactory.Register<IdentityRegistrationService>(builder => builder
            .ConfigureHttpClient(c =>
            {
                // this is called everytime you request a httpclient
                c.Timeout = TimeSpan.FromSeconds(3);
            })
            .ConfigurePrimaryHttpMessageHandler(() =>
            {
                // this is called whenever you request a httpclient and handler lifetime has expired
                var handler = new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    UseCookies = false, // DO NOT CHANGE!
                };

                // Make sure we accept certifactes from letsencrypt staging servers if not in production
                if (!_configuration.CertificateRenewal.UseCertificateAuthorityProductionServers)
                {
                    handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
                }

                return handler;
            })
            .SetHandlerLifetime(TimeSpan.FromSeconds(5))); // Shortlived to deal with DNS changes
    }
}

public class DnsConfig
{
    public enum LookupRecordStatus
    {
        Unknown,
        Success, // domain found, correct value returned
        DomainOrRecordNotFound, // domain not found, retry later
        IncorrectValue, // domain found, but DNS value is incorrect
    }

    public string Type { get; init; } = ""; // e.g. "CNAME"
    public string Name { get; init; } = ""; // e.g. "www" or ""
    public string Domain { get; init; } = ""; // e.g. "www.example.com" or "example.com"
    public string Value { get; init; } = ""; // e.g. "example.com" or "127.0.0.1"
    public string Verify { get; init; } = ""; // e.g. "example.com" or "127.0.0.1"
    public string Description { get; init; } = "";
    public LookupRecordStatus Status { get; set; } = LookupRecordStatus.Unknown;

    public class QueryResult
    {
        public int TotalCount { get; set; }
        public int SuccessCount { get; set; }
    }
    public Dictionary<string, QueryResult> QueryResults { get; } = new (); // query results per DNS ip address
}