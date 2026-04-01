using Odin.Core.Storage.Factory;

namespace Odin.Attestation.Database.Connection;

#nullable enable

public class ScopedAttestationTransactionFactory(ScopedAttestationConnectionFactory connectionFactory)
    : ScopedTransactionFactory<IAttestationDbConnectionFactory>(connectionFactory)
{
}
