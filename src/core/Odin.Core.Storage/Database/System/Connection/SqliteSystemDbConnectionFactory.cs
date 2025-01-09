using Odin.Core.Storage.Factory;
using Odin.Core.Storage.Factory.Sqlite;

namespace Odin.Core.Storage.Database.System.Connection;

#nullable enable

public sealed class SqliteSystemDbConnectionFactory(string connectionString, IDbConnectionPool connectionPool)
    : AbstractSqliteDbConnectionFactory(connectionString, connectionPool), ISystemDbConnectionFactory
{
}
