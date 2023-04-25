using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
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
    private readonly ILookupClient _provisioningDnsClient;
    private readonly IDnsRestClient _dnsRestClient;

    public IdentityRegistrationService(
        ILogger<IdentityRegistrationService> logger, 
        IHttpContextAccessor accessor, 
        YouverseConfiguration configuration,
        ILookupClient provisioningDnsClient,
        IDnsRestClient dnsRestClient)
    {
        _logger = logger;
        _configuration = configuration;
        _registry = accessor!.HttpContext!.RequestServices!.GetRequiredService<IIdentityRegistry>();
        _provisioningDnsClient = provisioningDnsClient;
        _reservationStorage = new ReservationStorage();
        _dnsRestClient = dnsRestClient;
    }

    public async Task<Guid> StartRegistration(RegistrationInfo registrationInfo)
    {
        _logger.LogInformation($"Starting Registration:{registrationInfo.ReservationId}");

        Guard.Argument(registrationInfo, nameof(registrationInfo)).NotNull();
        Guard.Argument(registrationInfo.ReservationId, nameof(registrationInfo.ReservationId)).NotEqual(Guid.Empty);

        var reservation = _reservationStorage.Get(registrationInfo.ReservationId);
        if (IsReservationValid(reservation) == false)
        {
            _logger.LogInformation($"Invalid Reservation:{registrationInfo.ReservationId}");
            _reservationStorage.Delete(registrationInfo.ReservationId);
            throw new Exception("Reservation not valid");
        }

        var request = new IdentityRegistrationRequest()
        {
            OdinId = (OdinId)reservation.Domain,
            IsCertificateManaged = false, //TODO
        };

        var firstRunToken = await _registry.AddRegistration(request);
        
        _logger.LogInformation($"Pending registration record saved:{registrationInfo.ReservationId}");
        return firstRunToken;
    }

    public async Task FinalizeRegistration(Guid firstRunToken)
    {
        await _registry.MarkRegistrationComplete(firstRunToken);
    }

    public async Task<RegistrationStatus> GetRegistrationStatus(Guid firstRunToken)
    {
        var status = await _registry.GetRegistrationStatus(firstRunToken);
        return status;
    }

    public async Task<Reservation> Reserve(ReservationRequest request)
    {
        Guard.Argument(request, nameof(request)).NotNull();
        Guard.Argument(request.DomainName, nameof(request.DomainName)).NotNull().NotEmpty();

        if (request.PreviousReservationId.HasValue)
        {
            await CancelReservation(request.PreviousReservationId.GetValueOrDefault());
        }

        if (!await IsAvailable(request.DomainName))
        {
            throw new YouverseClientException("Already Reserved", YouverseClientErrorCode.IdAlreadyExists);
        }

        //TODO: need a background clean up job to remove old reservations; for now we will overwrite it
        var record = _reservationStorage.GetByDomain(request.DomainName);

        var result = new Reservation()
        {
            Id = record?.Id ?? Guid.NewGuid(),
            Domain = request.DomainName,
            CreatedTime = UnixTimeUtc.Now(),
            ExpiresTime = UnixTimeUtc.Now().AddSeconds(60 * 60) //TODO: add to config
        };

        _reservationStorage.Save(result);

        return result;
    }

    public async Task<bool> IsAvailable(string domain)
    {
        // SEB:TODO
        return await Task.FromResult(true);
        
        // Guard.Argument(domain, nameof(domain)).NotNull().NotEmpty();
        //
        // var reservation = _reservationStorage.GetByDomain(domain);
        //
        // if (IsReservationValid(reservation))
        // {
        //     return false;
        // }
        //
        // // var pendingReg = _pendingRegistrationStorage.Get(id);
        // // if (null != pendingReg)
        // // {
        // //     return false;
        // // }
        //
        // return await _registry.IsIdentityRegistered(domain) == false;
    }

    public async Task CancelReservation(Guid reservationId)
    {
        _reservationStorage.Delete(reservationId);
        await Task.CompletedTask;
    }

    public Task<List<string>> GetManagedDomainApexes()
    {
        return Task.FromResult(_configuration.Registry.ManagedDomains);
    }


    public Task<DnsConfigurationSet> GetDnsConfiguration(string domain)
    {
        List<DnsConfig> MapDnsConfig(List<YouverseConfiguration.RegistrySection.DnsRecord> configRecords)
        {
            var result = new List<DnsConfig>();
            foreach (var record in configRecords)
            {
                result.Add(new DnsConfig
                {
                    Type = record.Type,
                    Name = record.Name,
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
    // Managed Domain
    //

    public async Task<bool> IsManagedDomainAvailable(string prefix, string apex)
    {
        var domain = prefix + "." + apex;
        DomainNameValidator.AssertValidDomain(domain);
        await AssertManagedDomainApexBelongsHere(apex);

        var dnsClient = await GetDnsClientForManagedDomainNameServer();
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
        await AssertManagedDomainApexBelongsHere(apex);
    
        var dnsClient = await GetDnsClientForManagedDomainNameServer();
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
    // Own Domain
    //
    
    public async Task<(bool, DnsConfigurationSet)> VerifyOwnDomain(string domain)
    {
        var dnsConfig = await GetDnsConfiguration(domain);

        var lookups = new List<Task<bool>>();
        foreach (var record in dnsConfig.AllDnsRecords)
        {
            lookups.Add(VerifyDnsRecord(domain, record, _provisioningDnsClient, true));
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

    private async Task<ILookupClient> GetDnsClientForManagedDomainNameServer()
    {
        if (IPAddress.TryParse(_configuration.Registry.PowerDnsHostAddress, out var nameServerIp))
        {
            return new LookupClient(nameServerIp);
        }
            
        var ips = await System.Net.Dns.GetHostAddressesAsync(_configuration.Registry.PowerDnsHostAddress);
        nameServerIp = ips.First();
        
        return new LookupClient(nameServerIp);
    }
    
    //
    
    private static async Task<bool> VerifyDnsRecord(
        string domain, 
        DnsConfig dnsConfig, 
        IDnsQuery dnsClient, 
        bool validateCofiguredValue)
    {
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

        return dnsConfig.Status == DnsConfig.LookupRecordStatus.Success;
    }
    
    //
    
    private async Task AssertManagedDomainApexBelongsHere(string apex)
    {
        var managedApexes = await GetManagedDomainApexes();
        if (!managedApexes.Contains(apex))
        {
            throw new YouverseSystemException($"Managed domain apex ${apex} does not belong here");
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
   
    private bool IsReservationValid(Reservation reservation)
    {
        var now = UnixTimeUtc.Now();
        return null != reservation && now < reservation.ExpiresTime;
    }

}

public class DnsConfigurationSet
{
    public List<DnsConfig> BackendDnsRecords { get; init; } = new ();
    public List<DnsConfig> FrontendDnsRecords { get; init; } = new ();
    public List<DnsConfig> StorageDnsRecords { get; init; } = new ();
    public List<DnsConfig> AllDnsRecords =>
        BackendDnsRecords
            .Concat(FrontendDnsRecords)
            .Concat(StorageDnsRecords)
            .ToList();
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
    
    public string Type { get; init; } = "";
    public string Name { get; init; } = "";
    public string Value { get; init; } = "";
    public string Description { get; init; } = "";
    public LookupRecordStatus Status { get; set; } = LookupRecordStatus.Unknown;
}
