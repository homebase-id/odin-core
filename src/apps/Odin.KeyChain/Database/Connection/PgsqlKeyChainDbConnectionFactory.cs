using Odin.Core.Storage.Factory.Pgsql;

namespace Odin.KeyChain.Database.Connection;

#nullable enable

public class PgsqlKeyChainDbConnectionFactory(string connectionString)
    : AbstractPgsqlDbConnectionFactory(connectionString), IKeyChainDbConnectionFactory
{
}