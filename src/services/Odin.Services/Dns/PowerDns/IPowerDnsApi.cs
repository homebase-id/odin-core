using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Refit;

namespace Odin.Services.Dns.PowerDns;

public interface IPowerDnsApi
{
    //
    // Zones
    //

    [Get("/servers/localhost/zones")]
    Task<IList<Zone>> GetZones(CancellationToken cancellationToken = default);

    [Get("/servers/localhost/zones/{zone_id}")]
    Task<ZoneWithRecords> GetZone([AliasAs("zone_id")] string zoneId, CancellationToken cancellationToken = default);

    [Post("/servers/localhost/zones")]
    Task<ZoneWithRecords> CreateZone([Body] object zone, CancellationToken cancellationToken = default);

    [Delete("/servers/localhost/zones/{zone_id}")]
    Task DeleteZone([AliasAs("zone_id")] string zoneId, CancellationToken cancellationToken = default);

    //
    // Rrsets / Records
    //

    [Patch("/servers/localhost/zones/{zone_id}")]
    Task CreateReplaceDeleteRrsets([AliasAs("zone_id")] string zoneId, [Body] object rrsets, CancellationToken cancellationToken = default);

    //
    // DNSSEC
    //

    [Get("/servers/localhost/zones/{zone_id}/cryptokeys")]
    Task<IList<Cryptokey>> GetCryptokeys([AliasAs("zone_id")] string zoneId, CancellationToken cancellationToken = default);

    // Creates or replaces the metadata of the given kind (idempotent)
    [Put("/servers/localhost/zones/{zone_id}/metadata/{metadata_kind}")]
    Task ReplaceZoneMetadata(
        [AliasAs("zone_id")] string zoneId,
        [AliasAs("metadata_kind")] string kind,
        [Body] object metadata,
        CancellationToken cancellationToken = default);
}