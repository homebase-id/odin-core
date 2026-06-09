using System.Data.Common;
using System.Threading.Tasks;
using Npgsql;

namespace Odin.Core.Storage.Factory.Pgsql;

public abstract class AbstractPgsqlDbConnectionFactory(string connectionString)
{
    private readonly string _connectionString = new NpgsqlConnectionStringBuilder(connectionString)
    {
        KeepAlive = 30,
    }.ConnectionString;

    //

    public DatabaseType DatabaseType => DatabaseType.Postgres;

    //

    public async Task<DbConnection> OpenAsync()
    {
        return await PgsqlConcreteConnectionFactory.CreateAsync(_connectionString);
    }

    //

    public async Task CloseAsync(DbConnection connection)
    {
        await connection.DisposeAsync();
    }
}