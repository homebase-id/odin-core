using Odin.Core.Storage.Database.Attestation.Connection;

namespace Odin.Core.Storage.Database.Attestation.Table;

public class TableAttestationRequest(
    ScopedAttestationConnectionFactory scopedConnectionFactory)
    : TableAttestationRequestCRUD(scopedConnectionFactory)
{
}