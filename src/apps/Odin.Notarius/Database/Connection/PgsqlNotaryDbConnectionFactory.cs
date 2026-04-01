using Odin.Core.Storage.Factory.Pgsql;

namespace Odin.Notarius.Database.Connection;

#nullable enable

public class PgsqlNotaryDbConnectionFactory(string connectionString)
    : AbstractPgsqlDbConnectionFactory(connectionString), INotaryDbConnectionFactory
{
}