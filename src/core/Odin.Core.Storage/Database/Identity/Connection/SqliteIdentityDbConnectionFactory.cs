using Odin.Core.Storage.Factory.Sqlite;

namespace Odin.Core.Storage.Database.Identity.Connection;

#nullable enable

public sealed class SqliteIdentityDbConnectionFactory(string connectionString)
    : AbstractSqliteDbConnectionFactory(connectionString), IIdentityDbConnectionFactory
{
}
