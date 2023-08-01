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

    public async Task<bool> HasValidCertifacte(string domain)
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

    public Task<List<OdinConfiguration.RegistrySection.ManagedDomainApex>> GetManagedDomainApexes()
    {
        return Task.FromResult(_configuration.Registry.ManagedDomainApexes);
    }
    
    //

    public Task<List<DnsConfig>> GetDnsConfiguration(string domain)
    {
        PunyDomainNameValidator.AssertValidDomain(domain);

        var dns = _configuration.Registry.DnsConfigurationSet; 

        var result = new List<DnsConfig>();
       
        // Bare A records
        for (var idx = 0; idx < dns.BareARecords.Count; idx++)
        {
            result.Add(new DnsConfig
            {
                Type = "A",
                Name = "",
                Domain = domain,    
                Value = dns.BareARecords[idx],
                Description = $"A Record #{idx + 1}"
            });
        }
        
        // CNAME WWW
        result.Add(new DnsConfig
        {
            Type = "CNAME",
            Name = DnsConfigurationSet.PrefixWww,
            Domain = $"{DnsConfigurationSet.PrefixWww}.{domain}",
            Value = dns.WwwCnameTarget == "" ? domain : dns.WwwCnameTarget,
            Description = $"WWW CNAME"
        });
        
        // CNAME API
        result.Add(new DnsConfig
        {
            Type = "CNAME",
            Name = DnsConfigurationSet.PrefixApi,
            Domain = $"{DnsConfigurationSet.PrefixApi}.{domain}",
            Value = dns.ApiCnameTarget == "" ? domain : dns.ApiCnameTarget,
           Description = $"API CNAME"
        });

        // CNAME CAPI
        result.Add(new DnsConfig
        {
            Type = "CNAME",
            Name = DnsConfigurationSet.PrefixCertApi,
            Domain = $"{DnsConfigurationSet.PrefixCertApi}.{domain}",
            Value = dns.CApiCnameTarget == "" ? domain : dns.CApiCnameTarget,
            Description = $"CAPI CNAME"
        });
        
        // CNAME FILE
        result.Add(new DnsConfig
        {
            Type = "CNAME",
            Name = DnsConfigurationSet.PrefixFile,
            Domain = $"{DnsConfigurationSet.PrefixFile}.{domain}",
            Value = dns.FileCnameTarget == "" ? domain : dns.FileCnameTarget,
            Description = $"FILE CNAME"
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
                        VerifyDnsRecord(domain, record, dnsClient, true)
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
        PunyDomainNameValidator.AssertValidDomain(domain);
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
        PunyDomainNameValidator.AssertValidDomain(domain);
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
        PunyDomainNameValidator.AssertValidDomain(domain);
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
        PunyDomainNameValidator.AssertValidDomain(domain);

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
        PunyDomainNameValidator.AssertValidDomain(domain);
        
        var dnsConfig = await GetDnsConfiguration(domain);

        var lookups = new List<Task<bool>>();
        var dnsClient = await CreateDnsClient();

        foreach (var record in dnsConfig)
        {
            lookups.Add(VerifyDnsRecord(domain, record, dnsClient, true));
        }

        await Task.WhenAll(lookups);

        foreach (var record in dnsConfig)
        {
            if (record.Status != DnsConfig.LookupRecordStatus.Success)
            {
                return (false, dnsConfig);
            }
        }

        // HURRAH!
        return (true, dnsConfig); 
    }
    
    //

    public async Task DeleteOwnDomain(string domain)
    {
        PunyDomainNameValidator.AssertValidDomain(domain);
        await _registry.DeleteRegistration(domain);        
    }
    
    //
 
    public async Task<Guid> CreateIdentityOnDomain(string domain, string email)
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

    private async Task SendProvisioningCompleteEmail(string domain, string email, string firstRunToken)
    {
        const string subject = "Your new identity is ready";
        var firstRunlink = $"https://{domain}/owner/firstrun?frt={firstRunToken}";
        
        var envelope = new Envelope
        {
            To = new List<NameAndEmailAddress> { new () { Email = email } },
            Subject = subject,
            TextMessage = RegistrationEmails.ProvisioningCompletedText(email, domain, firstRunlink),
            HtmlMessage = RegistrationEmails.ProvisioningCompletedHtml(email, domain, firstRunlink),
        };
        
        await _emailSender.SendAsync(envelope);
    }
    
    //        

    private static async Task<ILookupClient> CreateDnsClient(string resolverAddressOrHostName = "")
    {
        // SEB:TODO this should be injected into ctor as a factory instead 
        
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
        
        return new LookupClient(nameServerIp);
    }
    
    //
    
    private async Task<bool> VerifyDnsRecord(
        string domain, 
        DnsConfig dnsConfig, 
        IDnsQuery dnsClient, 
        bool validateCofiguredValue)
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
                response = await dnsClient.QueryAsync(domain, QueryType.A);
                entries = response.Answers.ARecords().Select(x => x.Address.ToString()).ToList();
                break;
            case "CNAME":
                response = await dnsClient.QueryAsync(domain, QueryType.CNAME);
                entries = response.Answers.CnameRecords()
                    .Select(x => x.CanonicalName.ToString().TrimEnd('.'))
                    .ToList();
                break;  
            default:
                throw new OdinSystemException($"Record type not supported: {dnsConfig.Type}");    
        }

        if (entries.Count == 0)
        {
            dnsConfig.Status = DnsConfig.LookupRecordStatus.DomainOrRecordNotFound;
        }
        else
        {
            if (!validateCofiguredValue || entries.Contains(dnsConfig.Value))
            {
                dnsConfig.Status = DnsConfig.LookupRecordStatus.Success;
            }
            else
            {
                dnsConfig.Status = DnsConfig.LookupRecordStatus.IncorrectValue;
            }
        }
        
        _logger.LogDebug(
            "DNS lookup {domain}: {status} ({elapsed}ms using {address})", 
            domain, dnsConfig.Status, sw.ElapsedMilliseconds, response.NameServer.Address);

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
        Success,                // domain found, correct value returned
        DomainOrRecordNotFound, // domain not found, retry later
        IncorrectValue,         // domain found, but DNS value is incorrect
    } 
    
    public string Type { get; init; } = "";            // e.g. "CNAME"
    public string Name { get; init; } = "";            // e.g. "www" or ""
    public string Domain { get; init; } = "";          // e.g. "www.example.com" or "example.com"
    public string Value { get; init; } = "";           // e.g. "example.com" or "127.0.0.1"
    public string Description { get; init; } = "";
    public LookupRecordStatus Status { get; set; } = LookupRecordStatus.Unknown;
}

