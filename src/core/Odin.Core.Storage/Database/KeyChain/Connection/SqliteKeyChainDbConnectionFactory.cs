using Odin.Core.Storage.Factory;
using Odin.Core.Storage.Factory.Sqlite;

namespace Odin.Core.Storage.Database.KeyChain.Connection;

#nullable enable

public sealed class SqliteKeyChainDbConnectionFactory(string connectionString, IDbConnectionPool connectionPool)
    : AbstractSqliteDbConnectionFactory(connectionString, connectionPool), IKeyChainDbConnectionFactory
{
}
