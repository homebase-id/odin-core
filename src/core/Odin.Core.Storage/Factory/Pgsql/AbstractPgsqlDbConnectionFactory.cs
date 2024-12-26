using System.Data.Common;
using System.Threading.Tasks;

namespace Odin.Core.Storage.Factory.Pgsql;

public abstract class AbstractPgsqlDbConnectionFactory(string connectionString)
{
    public DatabaseType DatabaseType => DatabaseType.Postgres;

    //

    public async Task<DbConnection> OpenAsync()
    {
        return await PgsqlConcreteConnectionFactory.CreateAsync(connectionString);
    }

    //

    public async Task CloseAsync(DbConnection connection)
    {
        await connection.DisposeAsync();
    }
}