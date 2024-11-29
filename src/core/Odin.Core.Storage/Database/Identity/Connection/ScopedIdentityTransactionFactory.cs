using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database.Identity.Connection;

public class ScopedIdentityTransactionFactory(ScopedIdentityConnectionFactory connectionFactory)
    : ScopedTransactionFactory<IIdentityDbConnectionFactory>(connectionFactory)
{
}
