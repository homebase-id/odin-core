using System.Data.Common;
using System.Threading.Tasks;

namespace Odin.Core.Storage.Factory.Pgsql;

public abstract class AbstractPgsqlDbConnectionFactory(string connectionString)
{
    public DatabaseType DatabaseType => DatabaseType.Postgres;
    public async Task<DbConnection> CreateAsync() => await PgsqlConcreteConnectionFactory.Create(connectionString);
}