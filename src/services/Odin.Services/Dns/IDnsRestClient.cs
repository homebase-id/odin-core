using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Dns;
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
    Task<bool> ZoneExists(string zoneId);
    Task<ZoneWithRecords> CreateZone(string zoneName, string[] nameServers, string adminEmail);
    Task DeleteZone(string zoneId);

    //
    // Rrsets / records
    //
    // An empty name addresses the zone apex.
    //

    Task CreateARecords(string zoneId, string name, IEnumerable<string> ipAddresses);
    Task DeleteARecords(string zoneId, string name);

    Task CreateCnameRecords(string zoneId, string name, string alias);
    Task DeleteCnameRecords(string zoneId, string name);

    /// <summary>
    /// Creates/replaces the MX rrset at name. Each content is "&lt;preference&gt; &lt;target&gt;."
    /// (e.g. "10 node-a.example.") - all of a name's MX records must be written in ONE call,
    /// since REPLACE swaps the whole rrset.
    /// </summary>
    Task CreateMxRecords(string zoneId, string name, IEnumerable<string> exchanges);
    Task DeleteMxRecords(string zoneId, string name);

    /// <summary>
    /// Creates/replaces the TXT rrset at name. Values are plain text - zone-file quoting,
    /// escaping, and 255-byte chunking are applied internally.
    /// </summary>
    Task CreateTxtRecords(string zoneId, string name, IEnumerable<string> values);
    Task DeleteTxtRecords(string zoneId, string name);

    //
    // DNSSEC
    //

    /// <summary>
    /// The DS records of the zone's active, published signing keys - the values a user
    /// publishes at the parent (registrar/DNS host) to anchor the chain of trust.
    /// Empty when the zone has no active keys (i.e. is not signed).
    /// </summary>
    Task<List<DsRecordData>> GetZoneDsRecords(string zoneId);

    /// <summary>
    /// Publishes CDS/CDNSKEY records in the zone (RFC 8078/7344), so parents that scan
    /// for them install/update the DS automatically. Idempotent.
    /// </summary>
    Task PublishCdsRecords(string zoneId);
}