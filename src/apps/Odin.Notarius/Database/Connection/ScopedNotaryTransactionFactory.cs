using Odin.Core.Storage.Factory;

namespace Odin.Notarius.Database.Connection;

#nullable enable

public class ScopedNotaryTransactionFactory(ScopedNotaryConnectionFactory connectionFactory)
    : ScopedTransactionFactory<INotaryDbConnectionFactory>(connectionFactory)
{
}
