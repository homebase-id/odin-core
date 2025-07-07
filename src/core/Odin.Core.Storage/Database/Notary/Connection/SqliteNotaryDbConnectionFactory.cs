using Odin.Core.Storage.Factory;
using Odin.Core.Storage.Factory.Sqlite;

namespace Odin.Core.Storage.Database.Notary.Connection;

#nullable enable

public sealed class SqliteNotaryDbConnectionFactory(string connectionString, IDbConnectionPool connectionPool)
    : AbstractSqliteDbConnectionFactory(connectionString, connectionPool), INotaryDbConnectionFactory
{
}
