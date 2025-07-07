using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database.KeyChain.Connection;

#nullable enable

public class ScopedKeyChainTransactionFactory(ScopedKeyChainConnectionFactory connectionFactory)
    : ScopedTransactionFactory<IKeyChainDbConnectionFactory>(connectionFactory)
{
}
