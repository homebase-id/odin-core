using Odin.Core.Storage.Factory;
using Odin.Core.Storage.Factory.Sqlite;

namespace Odin.Attestation.Database.Connection;

#nullable enable

public sealed class SqliteAttestationDbConnectionFactory(string connectionString, IDbConnectionPool connectionPool)
    : AbstractSqliteDbConnectionFactory(connectionString, connectionPool), IAttestationDbConnectionFactory
{
}
