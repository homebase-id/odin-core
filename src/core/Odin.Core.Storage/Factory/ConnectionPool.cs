using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;

namespace Odin.Core.Storage.Factory;

public class ConnectionPool(int poolSize) : IDisposable
{
    private readonly object _mutex = new ();
    private readonly Dictionary<string, Stack<DbConnection>> _connections = new (); // connectionString -> connections

    public async Task<DbConnection> GetConnectionAsync(string connectionString, Func<Task<DbConnection>> creator)
    {
        lock (_mutex)
        {
            if (_connections.TryGetValue(connectionString, out var stack) && stack.Count > 0)
            {
                return stack.Pop();
            }
        }

        return await creator();
    }

    //

    public async Task ReturnConnectionAsync(DbConnection connection)
    {
        var connectionString = connection.ConnectionString;

        lock (_mutex)
        {
            if (!_connections.TryGetValue(connectionString, out var stack))
            {
                stack = new Stack<DbConnection>();
                _connections[connectionString] = stack;
            }

            if (stack.Count < poolSize)
            {
                stack.Push(connection);
                connection = null;
            }
        }

        if (connection != null)
        {
            await connection.CloseAsync();
        }
    }

    //

    public void Clear(string connectionString)
    {
        lock (_mutex)
        {
            if (_connections.TryGetValue(connectionString, out var stack))
            {
                foreach (var connection in stack)
                {
                    connection.Dispose();
                }
                stack.Clear();
            }
        }
    }

    //

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        lock (_mutex)
        {
            foreach (var stack in _connections.Values)
            {
                foreach (var connection in stack)
                {
                    connection.Dispose();
                }
                stack.Clear();
            }
            _connections.Clear();
        }
    }
}
