using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database.System.Connection;

public class ScopedSystemTransactionFactory(ScopedSystemConnectionFactory connectionFactory)
    : ScopedTransactionFactory<ISystemDbConnectionFactory>(connectionFactory)
{
}
