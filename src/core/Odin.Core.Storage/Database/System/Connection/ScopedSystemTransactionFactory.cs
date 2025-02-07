using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database.System.Connection;

#nullable enable

public class ScopedSystemTransactionFactory(ScopedSystemConnectionFactory connectionFactory)
    : ScopedTransactionFactory<ISystemDbConnectionFactory>(connectionFactory)
{
}
