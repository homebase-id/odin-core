using Odin.Attestation.Database.Connection;

namespace Odin.Attestation.Database.Table;

public class TableAttestationStatus(
    ScopedAttestationConnectionFactory scopedConnectionFactory)
    : TableAttestationStatusCRUD(scopedConnectionFactory)
{
}