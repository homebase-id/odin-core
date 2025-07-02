using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Http;
using Refit;

namespace Odin.Services.Dns.PowerDns;

public class PowerDnsRestClient : IDnsRestClient
{
    private const int DefaultTtl = 3600;

    private readonly IDynamicHttpClientFactory _httpClientFactory;
    private readonly Uri _baseAddress;
    private readonly string _apiKey;
    private readonly ILogger<PowerDnsRestClient> _logger;
    
    public PowerDnsRestClient(
        ILogger<PowerDnsRestClient> logger, 
        IDynamicHttpClientFactory httpClientFactory,
        Uri baseAddress, 
        string apiKey)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _baseAddress = baseAddress;
        _apiKey = apiKey;
    }
   
    //
    
    private IPowerDnsApi Api
    {
        get
        {
            var httpClient = _httpClientFactory.CreateClient(_baseAddress.Host, config =>
            {
                config.HandlerLifetime = TimeSpan.FromSeconds(5); // Short-lived to deal with DNS changes
            });
            httpClient.BaseAddress = _baseAddress;
            httpClient.DefaultRequestHeaders.Add("X-API-Key", _apiKey);
            return RestService.For<IPowerDnsApi>(httpClient);
        } 
    }    

    public Task<IList<Zone>> GetZones()
    {
        return Api.GetZones();
    }
    
    //
    
    public Task<ZoneWithRecords> GetZone(string zoneId)
    {
        return Api.GetZone(zoneId);
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

        return Api.CreateZone(data);
    }
    
    //

    public Task DeleteZone(string zoneId)
    {
        return Api.DeleteZone(zoneId);
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
   
        return Api.CreateReplaceDeleteRrsets(zoneId, data);
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
        
        return Api.CreateReplaceDeleteRrsets(zoneId, data);
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
   
        return Api.CreateReplaceDeleteRrsets(zoneId, data);
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
        
        return Api.CreateReplaceDeleteRrsets(zoneId, data);
    }
}

