using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Dawn;
using DnsClient;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Services.Configuration;
using Youverse.Core.Services.Dns;
using Youverse.Core.Util;

// Managed Domain: DNS records are managed by e.g. an ISP
// Own Domain: DNS records are managed by end user

namespace Youverse.Core.Services.Registry.Registration;

#nullable enable

    /// <summary>
    /// Handles creating an identity on this host
    /// </summary>
public class IdentityRegistrationService : IIdentityRegistrationService
{
    private readonly ILogger<IdentityRegistrationService> _logger;
    private readonly IIdentityRegistry _registry;
    private readonly ReservationStorage _reservationStorage;
    private readonly YouverseConfiguration _configuration;
    private readonly IDnsRestClient _dnsRestClient;
    private readonly HttpClient _certifacteTester;

    public IdentityRegistrationService(
        ILogger<IdentityRegistrationService> logger, 
        IIdentityRegistry registry,
        YouverseConfiguration configuration,
        IDnsRestClient dnsRestClient,
        HttpClient certifacteTester)
    {
        _logger = logger;
        _configuration = configuration;
        _registry = registry;
        _reservationStorage = new ReservationStorage();
        _dnsRestClient = dnsRestClient;
        _certifacteTester = certifacteTester;
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
        try
        {
            await _certifacteTester.GetAsync($"https://{domain}");
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
    
    //

    public Task<List<YouverseConfiguration.RegistrySection.ManagedDomainApex>> GetManagedDomainApexes()
    {
        return Task.FromResult(_configuration.Registry.ManagedDomainApexes);
    }
    
    //

    public Task<DnsConfigurationSet> GetDnsConfiguration(string domain)
    {
        DomainNameValidator.AssertValidDomain(domain);
        
        List<DnsConfig> MapDnsConfig(List<YouverseConfiguration.RegistrySection.DnsRecord> configRecords)
        {
            var result = new List<DnsConfig>();
            foreach (var record in configRecords)
            {
                result.Add(new DnsConfig
                {
                    Type = record.Type,
                    Name = record.Name,
                    Domain = record.Name == "" ? domain : record.Name + "." + domain,    
                    Value = record.Value == "{{domain-placeholder}}" ? domain : record.Value,
                    Description = record.Description
                });
            }
            return result;
        }

        var result = new DnsConfigurationSet
        {
            BackendDnsRecords = MapDnsConfig(_configuration.Registry.BackendDnsRecords),
            FrontendDnsRecords = MapDnsConfig(_configuration.Registry.FrontendDnsRecords),
            StorageDnsRecords = MapDnsConfig(_configuration.Registry.StorageDnsRecords)
        };

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
            foreach (var record in dnsConfig.AllDnsRecords)
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
        DomainNameValidator.AssertValidDomain(domain);
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
        
        foreach (var record in dnsConfig.AllDnsRecords)
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
        DomainNameValidator.AssertValidDomain(domain);
        await AssertManagedDomainApexAndPrefix(prefix, apex);
    
        var dnsConfig = await GetDnsConfiguration(domain);

        var zoneId = apex + ".";
        foreach (var record in dnsConfig.AllDnsRecords)
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
                throw new YouverseSystemException($"Unsupported record: {record.Type}");
            }
        }
    }
    
    //

    public async Task DeleteManagedDomain(string prefix, string apex)
    {
        var domain = prefix + "." + apex;
        DomainNameValidator.AssertValidDomain(domain);
        await AssertManagedDomainApexAndPrefix(prefix, apex);

        await _registry.DeleteRegistration(domain);
        
        var dnsConfig = await GetDnsConfiguration(domain);

        var zoneId = apex + ".";
        foreach (var record in dnsConfig.AllDnsRecords)
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
                throw new YouverseSystemException($"Unsupported record: {record.Type}");
            }
        }
    }
    
    //
    // Own Domain
    //

    public async Task<bool> IsOwnDomainAvailable(string domain)
    {
        DomainNameValidator.AssertValidDomain(domain);

        var identity = await _registry.Get(domain);
        if (identity != null)
        {
            // Identity already exists
            return false;
        }

        return true;
    }

    //

    public async Task<(bool, DnsConfigurationSet)> GetOwnDomainDnsStatus(string domain)
    {
        DomainNameValidator.AssertValidDomain(domain);
        
        var dnsConfig = await GetDnsConfiguration(domain);

        var lookups = new List<Task<bool>>();
        var dnsClient = await CreateDnsClient();

        foreach (var record in dnsConfig.AllDnsRecords)
        {
            lookups.Add(VerifyDnsRecord(domain, record, dnsClient, true));
        }

        await Task.WhenAll(lookups);

        foreach (var record in dnsConfig.AllDnsRecords)
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
        DomainNameValidator.AssertValidDomain(domain);
        await _registry.DeleteRegistration(domain);        
    }
    
    //
 
    public async Task<Guid> CreateIdentityOnDomain(string domain)
    {
        var identity = await _registry.Get(domain);
        if (identity != null)
        { 
            throw new YouverseSystemException($"Identity {domain} already exists");
        }
        
        // SEB:TODO get rid of reservations
        var reservation = new Reservation()
        {
            Id = Guid.NewGuid(),
            Domain = domain,
            CreatedTime = UnixTimeUtc.Now(),
            ExpiresTime = UnixTimeUtc.Now().AddSeconds(60 * 60 * 48) //TODO: add to config (48 hours)
        };
        
        var request = new IdentityRegistrationRequest()
        {
            OdinId = (OdinId)reservation.Domain,
            IsCertificateManaged = false, //TODO
        };

        try
        {
            var firstRunToken = await _registry.AddRegistration(request);
            return firstRunToken;
        }
        catch (Exception)
        {
            await _registry.DeleteRegistration(domain);
            throw;
        }
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
                throw new YouverseSystemException($"Record type not supported: {dnsConfig.Type}");    
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
            throw new YouverseSystemException($"Managed domain apex {apex} does not belong here");
        }

        var labelCount = prefix.Count(x => x == '.') + 1;
        if (managedApex.PrefixLabels.Count != labelCount)
        {
            throw new YouverseSystemException(
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
   
}

