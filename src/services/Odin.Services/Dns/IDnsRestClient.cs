using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Services.Dns.PowerDns;

namespace Odin.Services.Dns;

// SEB:NOTE
// This is modelled after PowerDNS: https://github.com/PowerDNS/pdns/blob/auth-4.5.3/docs/http-api/swagger/authoritative-api-swagger.yaml
// We could probably use a tool for auto generating an API wrapper.  
public interface IDnsRestClient
{
    //
    // Zones
    //
    
    Task<IList<Zone>> GetZones();
    Task<ZoneWithRecords> GetZone(string zoneId);
    Task<ZoneWithRecords> CreateZone(string zoneName, string[] nameServers, string adminEmail);
    Task DeleteZone(string zoneId);
    
    //
    // Rrsets / records
    //

    Task CreateARecords(string zoneId, string name, IEnumerable<string> ipAddresses);
    Task DeleteARecords(string zoneId, string name);

    Task CreateCnameRecords(string zoneId, string name, string alias);
    Task DeleteCnameRecords(string zoneId, string name);
    
    
}