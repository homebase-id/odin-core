using Odin.Attestation.Database.Connection;

namespace Odin.Attestation.Database.Table;

public class TableAttestationRequest(
    ScopedAttestationConnectionFactory scopedConnectionFactory)
    : TableAttestationRequestCRUD(scopedConnectionFactory)
{
}