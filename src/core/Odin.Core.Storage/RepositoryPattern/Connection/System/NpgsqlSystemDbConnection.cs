using System.Data;
using System.Threading.Tasks;
using Nito.AsyncEx;

namespace Odin.Core.Storage.RepositoryPattern.Connection.System;

#nullable enable

public class NpgsqlSystemDbConnection(string connectionString) : ISystemDbConnection
{
    private readonly AsyncLazy<IDbConnection> _connection = new(
        async () => await NpgsqlConnectionFactory.Create(connectionString));

    public async Task<IDbConnection> Get() => await _connection;

    //

    public void Dispose()
    {
        if (_connection.IsStarted)
        {
            _connection.Task.Result.Dispose();
        }
    }

    //

}
