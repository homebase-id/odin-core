using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database.Notary.Connection;

#nullable enable

public class ScopedNotaryTransactionFactory(ScopedNotaryConnectionFactory connectionFactory)
    : ScopedTransactionFactory<INotaryDbConnectionFactory>(connectionFactory)
{
}
