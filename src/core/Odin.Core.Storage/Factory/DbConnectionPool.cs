using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Storage.Database;

namespace Odin.Core.Storage.Factory;

public interface IDbConnectionPool : IDisposable, IAsyncDisposable
{
    Task<DbConnection> GetConnectionAsync(string connectionString, Func<Task<DbConnection>> creator);
    Task ReturnConnectionAsync(DbConnection connection);
    Task ClearAsync(string connectionString);
    Task ClearAllAsync();
    int PoolSize { get; }
}

public class DbConnectionPool(
    ILogger<DbConnectionPool> logger,
    DatabaseCounters counters,
    int poolSize) : IDbConnectionPool
{
    private bool _disposed;
    private readonly object _mutex = new();
    private readonly Dictionary<string, Stack<DbConnection>> _connections = new (); // connectionString -> connections

    public int PoolSize => poolSize;

    //

    public async Task<DbConnection> GetConnectionAsync(string connectionString, Func<Task<DbConnection>> creator)
    {
        LogTrace("Acquiring pool lock");
        lock (_mutex)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(DbConnectionPool));
            }

            if (_connections.TryGetValue(connectionString, out var stack) && stack.Count > 0)
            {
                LogTrace("Pool returning cached connection");
                LogTrace("Releasing pool lock");
                return stack.Pop();
            }
            LogTrace("Releasing pool lock");
        }

        LogTrace("Pool creating new connection");
        counters.IncrementNoPoolOpened();
        var cn = await creator();
        LogTrace("Pool created new connection");
        return cn;
    }

    //

    public async Task ReturnConnectionAsync(DbConnection connection)
    {
        var connectionString = connection.ConnectionString;

        LogTrace("Acquiring pool lock");
        lock(_mutex)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(DbConnectionPool));
            }

            if (!_connections.TryGetValue(connectionString, out var stack))
            {
                stack = new Stack<DbConnection>();
                _connections[connectionString] = stack;
            }

            if (stack.Count < poolSize)
            {
                LogTrace("Pool caching connection");
                stack.Push(connection);
                connection = null;
            }
            LogTrace("Releasing pool lock");
        }

        if (connection != null)
        {
            LogTrace("Pool disposing connection due to pool size limit");
            await CloseConnectionAsync(connection);
            LogTrace("Pool disposed connection");
        }
    }

    //

    public async Task ClearAsync(string connectionString)
    {
        // NOTE: don't log connection string here, it might contain sensitive information
        LogTrace("Pool clearing connections");
        List<DbConnection> connectionsToDispose = [];

        lock(_mutex)
        {
            if (_connections.TryGetValue(connectionString, out var stack))
            {
                connectionsToDispose.AddRange(stack);
                stack.Clear();
            }
        }

        foreach (var connection in connectionsToDispose)
        {
            await CloseConnectionAsync(connection);
        }

        LogTrace("Pool done clearing connections");
    }

    //

    public async Task ClearAllAsync()
    {
        LogTrace("Pool clearing all connections");

        List<DbConnection> connectionsToDispose = [];
        lock(_mutex)
        {
            foreach (var stack in _connections.Values)
            {
                foreach (var connection in stack)
                {
                    connectionsToDispose.Add(connection);
                }
            }
            _connections.Clear();
        }

        foreach (var connection in connectionsToDispose)
        {
            await CloseConnectionAsync(connection);
        }

        LogTrace("Pool done clearing all connections");
    }

    //

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    //

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        lock(_mutex)
        {
            _disposed = true;
        }
        await ClearAllAsync();
    }

    //

    private async Task CloseConnectionAsync(DbConnection connection)
    {
        counters.IncrementNoPoolClosed();
        await connection.DisposeAsync();
    }

    //

    private void LogTrace(string message)
    {
        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.LogTrace("{message}", message);
        }
    }

    //

}
