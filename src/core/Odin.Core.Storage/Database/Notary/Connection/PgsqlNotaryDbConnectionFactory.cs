using Odin.Core.Storage.Factory.Pgsql;

namespace Odin.Core.Storage.Database.Notary.Connection;

#nullable enable

public class PgsqlNotaryDbConnectionFactory(string connectionString)
    : AbstractPgsqlDbConnectionFactory(connectionString), INotaryDbConnectionFactory
{
}