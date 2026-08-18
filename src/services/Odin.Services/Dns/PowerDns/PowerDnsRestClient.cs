using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Dns;
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
            var httpClient = _httpClientFactory.CreateClient($"{nameof(IPowerDnsApi)}:{_baseAddress.Host}", config =>
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

    public async Task<bool> ZoneExists(string zoneId)
    {
        try
        {
            await Api.GetZone(zoneId);
            return true;
        }
        catch (ApiException e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
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

    // An empty name addresses the zone apex
    private static string RecordName(string zoneId, string name)
    {
        return name == "" ? zoneId : $"{name}.{zoneId}";
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
                    name = RecordName(zoneId, name),
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
                    name = RecordName(zoneId, name),
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
                    name = RecordName(zoneId, name),
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
                    name = RecordName(zoneId, name),
                    type = "CNAME",
                    changetype = "DELETE",
                }
            }
        };

        return Api.CreateReplaceDeleteRrsets(zoneId, data);
    }

    //

    public async Task<List<DsRecordData>> GetZoneDsRecords(string zoneId)
    {
        var cryptokeys = await Api.GetCryptokeys(zoneId);
        return cryptokeys
            .Where(key => key.active && key.published)
            .SelectMany(key => key.ds ?? [])
            .Select(DsRecordData.TryParse)
            .Where(ds => ds != null)
            .Select(ds => ds!)
            // SHA-256 (digest type 2) first: the digest type registrars expect today
            .OrderByDescending(ds => ds.DigestType == 2)
            .ThenBy(ds => ds.DigestType)
            .Distinct()
            .ToList();
    }

    //

    public async Task PublishCdsRecords(string zoneId)
    {
        // PUBLISH-CDS value = DS digest algorithms to publish; "2" = SHA-256.
        // PUT replaces, so re-running converges (idempotent).
        await Api.ReplaceZoneMetadata(zoneId, "PUBLISH-CDS",
            new { kind = "PUBLISH-CDS", metadata = new[] { "2" }, type = "Metadata" });
        await Api.ReplaceZoneMetadata(zoneId, "PUBLISH-CDNSKEY",
            new { kind = "PUBLISH-CDNSKEY", metadata = new[] { "1" }, type = "Metadata" });
    }
}

