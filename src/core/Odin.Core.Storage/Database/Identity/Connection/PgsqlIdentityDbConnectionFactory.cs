using Odin.Core.Storage.Factory.Pgsql;

namespace Odin.Core.Storage.Database.Identity.Connection;

#nullable enable

public class PgsqlIdentityDbConnectionFactory(string connectionString)
    : AbstractPgsqlDbConnectionFactory(connectionString), IIdentityDbConnectionFactory
{
}