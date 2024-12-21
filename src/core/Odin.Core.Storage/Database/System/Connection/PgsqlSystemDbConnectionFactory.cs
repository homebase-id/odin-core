using Odin.Core.Storage.Factory.Pgsql;

namespace Odin.Core.Storage.Database.System.Connection;

#nullable enable

public class PgsqlSystemDbConnectionFactory(string connectionString)
    : AbstractPgsqlDbConnectionFactory(connectionString), ISystemDbConnectionFactory
{
}
