using Odin.Core.Storage.Database.Attestation.Connection;

namespace Odin.Core.Storage.Database.Attestation.Table;

public class TableAttestationStatus(
    CacheHelper cache,
    ScopedAttestationConnectionFactory scopedConnectionFactory)
    : TableAttestationStatusCRUD(cache, scopedConnectionFactory)
{
}