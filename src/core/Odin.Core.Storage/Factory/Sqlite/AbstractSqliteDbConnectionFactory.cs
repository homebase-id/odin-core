using System;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Odin.Core.Storage.Factory.Sqlite;

public abstract class AbstractSqliteDbConnectionFactory(string connectionString) : IDisposable
{
    public DatabaseType DatabaseType => DatabaseType.Sqlite;
    public async Task<DbConnection> CreateAsync() => await SqliteConcreteConnectionFactory.Create(connectionString);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        using var cn = new SqliteConnection(connectionString);
        SqliteConnection.ClearPool(cn);
    }
}
