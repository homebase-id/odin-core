using System;
using System.Data.Common;
using System.Threading.Tasks;

namespace Odin.Core.Storage.Factory.Sqlite;

public abstract class AbstractSqliteDbConnectionFactory(string connectionString, int poolSize) : IDisposable
{
    private readonly ConnectionPool _connectionPool = new(poolSize);

    public DatabaseType DatabaseType => DatabaseType.Sqlite;

    //

    public async Task<DbConnection> OpenAsync()
    {
        return await _connectionPool.GetConnectionAsync(
            connectionString, () => SqliteConcreteConnectionFactory.Create(connectionString));
    }

    //

    public async Task CloseAsync(DbConnection connection)
    {
        await _connectionPool.ReturnConnectionAsync(connection);
    }

    //

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _connectionPool.Dispose();
    }

    //
}
