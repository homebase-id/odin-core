using Odin.Core.Storage.Database.System.Connection;
using Odin.Core.Storage.Factory.Pgsql;

namespace Odin.Core.Storage.Database.KeyChain.Connection;

#nullable enable

public class PgsqlKeyChainDbConnectionFactory(string connectionString)
    : AbstractPgsqlDbConnectionFactory(connectionString), ISystemDbConnectionFactory
{
}