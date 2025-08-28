using Odin.Core.Storage.Database.Attestation.Connection;

namespace Odin.Core.Storage.Database.Attestation.Table;

public class TableAttestationStatus(
    ScopedAttestationConnectionFactory scopedConnectionFactory)
    : TableAttestationStatusCRUD(scopedConnectionFactory)
{
}