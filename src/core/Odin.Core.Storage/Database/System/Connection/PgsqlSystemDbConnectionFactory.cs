using System.Data.Common;
using System.Threading.Tasks;
using Odin.Core.Storage.Factory;
using Odin.Core.Storage.Factory.Pgsql;

namespace Odin.Core.Storage.Database.System.Connection;

#nullable enable

public class PgsqlSystemDbConnectionFactory(string connectionString) : ISystemDbConnectionFactory
{
    public DatabaseType DatabaseType => DatabaseType.Postgres;
    public async Task<DbConnection> CreateAsync() => await PgsqlConcreteConnectionFactory.Create(connectionString);
}