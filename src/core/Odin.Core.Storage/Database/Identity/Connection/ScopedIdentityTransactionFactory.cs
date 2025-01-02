using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database.Identity.Connection;

#nullable enable

public class ScopedIdentityTransactionFactory(ScopedIdentityConnectionFactory connectionFactory)
    : ScopedTransactionFactory<IIdentityDbConnectionFactory>(connectionFactory)
{
}
