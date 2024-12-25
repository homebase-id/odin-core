using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Odin.Core.Storage.Factory;

public interface IDbConnectionPool : IDisposable
{
    Task<DbConnection> GetConnectionAsync(string connectionString, Func<Task<DbConnection>> creator);
    Task ReturnConnectionAsync(DbConnection connection);
    void Clear(string connectionString);
}

public class DbConnectionPool(ILogger<DbConnectionPool> logger, int poolSize) : IDbConnectionPool
{
    private readonly object _mutex = new ();
    private readonly Dictionary<string, Stack<DbConnection>> _connections = new (); // connectionString -> connections

    //

    public async Task<DbConnection> GetConnectionAsync(string connectionString, Func<Task<DbConnection>> creator)
    {
        lock (_mutex)
        {
            if (_connections.TryGetValue(connectionString, out var stack) && stack.Count > 0)
            {
                logger.LogTrace("Pool returning cached connection");
                return stack.Pop();
            }
        }

        logger.LogTrace("Pool creating new connection");
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
                logger.LogTrace("Pool caching connection");
                stack.Push(connection);
                connection = null;
            }
        }

        if (connection != null)
        {
            logger.LogTrace("Pool disposing connection due to pool size limit");
            await connection.DisposeAsync();
        }
    }

    //

    public void Clear(string connectionString)
    {
        logger.LogTrace("Pool clearing connections");
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
        logger.LogTrace("Pool disposing connections");
        lock (_mutex)
        {
            foreach (var stack in _connections.Values)
            {
                foreach (var connection in stack)
                {
                    connection.Dispose();
                }
            }
            _connections.Clear();
        }
    }
}
