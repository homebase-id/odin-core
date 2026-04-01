using Odin.Core.Storage.Factory;

namespace Odin.KeyChain.Database.Connection;

#nullable enable

public class ScopedKeyChainTransactionFactory(ScopedKeyChainConnectionFactory connectionFactory)
    : ScopedTransactionFactory<IKeyChainDbConnectionFactory>(connectionFactory)
{
}
