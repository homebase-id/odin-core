using System;
using System.Data.Common;
using System.Threading.Tasks;

namespace Odin.Core.Storage.Factory.Sqlite;

public abstract class AbstractSqliteDbConnectionFactory(string connectionString, IDbConnectionPool connectionPool) : IDisposable
{
    public DatabaseType DatabaseType => DatabaseType.Sqlite;

    //

    public async Task<DbConnection> OpenAsync()
    {
        return await connectionPool.GetConnectionAsync(
            connectionString,
            async () => await SqliteConcreteConnectionFactory.CreateAsync(connectionString));
    }

    //

    public async Task CloseAsync(DbConnection connection)
    {
        await connectionPool.ReturnConnectionAsync(connection);
    }

    //

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        connectionPool.Dispose();
    }
}
