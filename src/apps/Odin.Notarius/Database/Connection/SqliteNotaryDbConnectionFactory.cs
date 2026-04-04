using Odin.Core.Storage.Factory;
using Odin.Core.Storage.Factory.Sqlite;

namespace Odin.Notarius.Database.Connection;

#nullable enable

public sealed class SqliteNotaryDbConnectionFactory(string connectionString, IDbConnectionPool connectionPool)
    : AbstractSqliteDbConnectionFactory(connectionString, connectionPool), INotaryDbConnectionFactory
{
}
