using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Refit;
using Youverse.Core.Services.Configuration;

namespace Youverse.Core.Services.Dns.PowerDns;

public class PowerDnsRestClient : IDnsRestClient
{
    private const int DefaultTtl = 3600;
    
    private readonly ILogger<PowerDnsRestClient> _logger;
    private readonly IPowerDnsApi _pdnsApi; 
    
    public PowerDnsRestClient(
        ILogger<PowerDnsRestClient> logger,
        string powerDnsHostAddress,
        string powerDnsApiKey)
    {
        _logger = logger;

        var httpClient = new HttpClient();
        httpClient.BaseAddress = new Uri($"https://{powerDnsHostAddress}/api/v1");
        httpClient.DefaultRequestHeaders.Add("X-API-Key", powerDnsApiKey);
        _pdnsApi = RestService.For<IPowerDnsApi>(httpClient);
    }
    
    public PowerDnsRestClient(
        ILogger<PowerDnsRestClient> logger,
        YouverseConfiguration config) 
        : this(logger, config.Registry.PowerDnsHostAddress, config.Registry.PowerDnsApiKey)
    {
    }
    
    //

    public Task<IList<Zone>> GetZones()
    {
        return _pdnsApi.GetZones();
    }
    
    //
    
    public Task<ZoneWithRecords> GetZone(string zoneId)
    {
        return _pdnsApi.GetZone(zoneId);
    }

    //
    
    public Task<ZoneWithRecords> CreateZone(string zoneName, string[] nameServers, string adminEmail)
    {
        var data = new
        {
            name = zoneName,
            kind = "Native",
            masters = Array.Empty<string>(),
            nameservers = nameServers,
            api_rectify = true,
            dnssec = true,
            rrsets = new[]
            {
                new
                {
                    comments = Array.Empty<string>(),
                    name = zoneName,
                    records = new []
                    {
                        new
                        {
                            content = $"{nameServers[0]} {adminEmail.Replace('@', '.') + '.'} 1111 10000 2400 604800 3600",    
                        }
                    },
                    ttl = "3600",
                    type = "SOA"
                }
            }
        };

        return _pdnsApi.CreateZone(data);
    }
    
    //

    public Task DeleteZone(string zoneId)
    {
        return _pdnsApi.DeleteZone(zoneId);
    }
    
    //

    public Task CreateARecords(string zoneId, string name, IEnumerable<string> ipAddresses)
    {
        var records = ipAddresses.Select(x =>
            new
            {
                content = x,
                disabled = false
            }
        );

        var data = new
        {
            rrsets = new[]
            {
                new
                {
                    name = $"{name}.{zoneId}",
                    type = "A",
                    changetype = "REPLACE",
                    ttl = DefaultTtl,
                    records
                }
            }
        };
   
        return _pdnsApi.CreateReplaceDeleteRrsets(zoneId, data);
    }
    
    //

    public Task DeleteARecords(string zoneId, string name)
    {
        var data = new
        {
            rrsets = new[]
            {
                new
                {
                    name = $"{name}.{zoneId}",
                    type = "A",
                    changetype = "DELETE",
                }
            }
        };
        
        return _pdnsApi.CreateReplaceDeleteRrsets(zoneId, data);
    }
    
    //

    public Task CreateCnameRecords(string zoneId, string name, string alias)
    {
        var data = new
        {
            rrsets = new[]
            {
                new
                {
                    name = $"{name}.{zoneId}",
                    type = "CNAME",
                    changetype = "REPLACE",
                    ttl = DefaultTtl,
                    records = new []
                    {
                        new
                        {
                            content = alias,
                            disabled = false
                        }
                    }
                }
            }
        };
   
        return _pdnsApi.CreateReplaceDeleteRrsets(zoneId, data);
    }

    public Task DeleteCnameRecords(string zoneId, string name)
    {
        var data = new
        {
            rrsets = new[]
            {
                new
                {
                    name = $"{name}.{zoneId}",
                    type = "CNAME",
                    changetype = "DELETE",
                }
            }
        };
        
        return _pdnsApi.CreateReplaceDeleteRrsets(zoneId, data);
        
    }
    
    
    
}