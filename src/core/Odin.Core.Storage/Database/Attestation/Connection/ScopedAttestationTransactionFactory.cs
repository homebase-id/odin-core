using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database.Attestation.Connection;

#nullable enable

public class ScopedAttestationTransactionFactory(ScopedAttestationConnectionFactory connectionFactory)
    : ScopedTransactionFactory<IAttestationDbConnectionFactory>(connectionFactory)
{
}
