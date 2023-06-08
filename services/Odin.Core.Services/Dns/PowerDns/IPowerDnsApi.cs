using System.Collections.Generic;
using System.Threading.Tasks;
using Refit;

namespace Youverse.Core.Services.Dns.PowerDns;

public interface IPowerDnsApi
{
    //
    // Zones
    //
    
    [Get("/servers/localhost/zones")]
    Task<IList<Zone>> GetZones();
    
    [Get("/servers/localhost/zones/{zone_id}")]
    Task<ZoneWithRecords> GetZone([AliasAs("zone_id")] string zoneId);
    
    [Post("/servers/localhost/zones")]
    Task<ZoneWithRecords> CreateZone([Body] object zone);
    
    [Delete("/servers/localhost/zones/{zone_id}")]
    Task DeleteZone([AliasAs("zone_id")] string zoneId);
    
    // 
    // Rrsets / Records
    //
    
    [Patch("/servers/localhost/zones/{zone_id}")]
    Task CreateReplaceDeleteRrsets([AliasAs("zone_id")] string zoneId, [Body] object rrsets);
}