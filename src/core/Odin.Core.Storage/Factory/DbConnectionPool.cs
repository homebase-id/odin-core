using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
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
    private readonly AsyncLock _mutex = new();
    private readonly Dictionary<string, Stack<DbConnection>> _connections = new (); // connectionString -> connections

    public int PoolSize => poolSize;

    //

    public async Task<DbConnection> GetConnectionAsync(string connectionString, Func<Task<DbConnection>> creator)
    {
        using (_mutex.Lock())
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(DbConnectionPool));
            }

            if (_connections.TryGetValue(connectionString, out var stack) && stack.Count > 0)
            {
                logger.LogTrace("Pool returning cached connection");
                return stack.Pop();
            }
        }

        logger.LogTrace("Pool creating new connection");
        counters.IncrementNoPoolOpened();
        return await creator();
    }

    //

    public async Task ReturnConnectionAsync(DbConnection connection)
    {
        var connectionString = connection.ConnectionString;

        using (await _mutex.LockAsync())
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
                logger.LogTrace("Pool caching connection");
                stack.Push(connection);
                connection = null;
            }
        }

        if (connection != null)
        {
            logger.LogTrace("Pool disposing connection due to pool size limit");
            await CloseConnectionAsync(connection);
        }
    }

    //

    public async Task ClearAsync(string connectionString)
    {
        // NOTE: don't log connection string here, it might contain sensitive information
        logger.LogTrace("Pool clearing connections");
        using (await _mutex.LockAsync())
        {
            if (_connections.TryGetValue(connectionString, out var stack))
            {
                foreach (var connection in stack)
                {
                    await CloseConnectionAsync(connection);
                }
                stack.Clear();
            }
        }
    }

    //

    public async Task ClearAllAsync()
    {
        logger.LogTrace("Pool clearing all connections");
        using (await _mutex.LockAsync())
        {
            foreach (var stack in _connections.Values)
            {
                foreach (var connection in stack)
                {
                    await CloseConnectionAsync(connection);
                }
            }
            _connections.Clear();
        }
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
        using (await _mutex.LockAsync())
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
}
