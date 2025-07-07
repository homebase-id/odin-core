using Odin.Core.Storage.Factory;
using Odin.Core.Storage.Factory.Sqlite;

namespace Odin.Core.Storage.Database.Attestation.Connection;

#nullable enable

public sealed class SqliteAttestationDbConnectionFactory(string connectionString, IDbConnectionPool connectionPool)
    : AbstractSqliteDbConnectionFactory(connectionString, connectionPool), IAttestationDbConnectionFactory
{
}
