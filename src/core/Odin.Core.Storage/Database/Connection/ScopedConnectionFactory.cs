using System;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Database.Connection.System;
using Odin.Core.Storage.Database.Connection.Tenant;

namespace Odin.Core.Storage.Database.Connection;

#nullable enable

/*
    When dealing with either parallel tasks or threads, it is important to ensure that they run in a new scope:

    using var scope = _scopeFactory.CreateScope();
    var scopedDbConnection = scope.ServiceProvider.GetRequiredService<ScopedDbConnection>();

    var (disposer, connection) = await scopedDbConnection.CreateConnectionAsync();
      // Use the connection
      disposer.Dispose();

    OR

      using var scope = _scopeFactory.CreateScope();

      var someRepo = scope.ServiceProvider.GetRequiredService<SomeRepo>();
      await someRepo.InsertStuffInDbAsync();
 */

public class ScopedConnectionFactory<T>(ILogger<ScopedConnectionFactory<T>> logger, T connectionFactory) where T : IDbConnectionFactory
{
    private readonly T _connectionFactory = connectionFactory;
    private readonly AsyncLock _mutex = new();
    private DbConnection? _connection;
    private int _connectionRefCount;
    private DbTransaction? _transaction;
    private int _transactionRefCount;

    public async Task<ConnectionAccessor> CreateScopedConnectionAsync()
    {
        logger.LogTrace("Creating connection");

        using (await _mutex.LockAsync())
        {
            if (++_connectionRefCount == 1)
            {
                _connection = await _connectionFactory.CreateAsync();
            }

            // Sanity
            if (_connectionRefCount != 0 && _connection == null)
            {
                throw new ScopedDbConnectionException(
                    $"Connection ref count is {_connectionRefCount} but connection is null. This should never happen");
            }

            return new ConnectionAccessor(this);
        }
    }

    //

    private async Task<TransactionAccessor> BeginNestedTransactionAsync()
    {
        logger.LogTrace("Beginning transaction");

        using (await _mutex.LockAsync())
        {
            if (_connection == null)
            {
                throw new ScopedDbConnectionException("No connection available to begin transaction");
            }

            if (++_transactionRefCount == 1)
            {
                _transaction = await _connection.BeginTransactionAsync();
            }

            // Sanity
            if (_transactionRefCount != 0 && _transaction == null)
            {
                throw new ScopedDbConnectionException(
                    $"Transaction ref count is {_transactionRefCount} but transaction is null. This should never happen");
            }

            return new TransactionAccessor(this);
        }
    }

    //

    public sealed class ConnectionAccessor(ScopedConnectionFactory<T> instance) : IDisposable, IAsyncDisposable
    {
        public DbConnection Instance => instance._connection!;
        public int RefCount => instance._connectionRefCount;

        public async Task<TransactionAccessor> BeginNestedTransactionAsync()
        {
            return await instance.BeginNestedTransactionAsync();
        }

        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        public async ValueTask DisposeAsync()
        {
            using (await instance._mutex.LockAsync())
            {
                if (--instance._connectionRefCount == 0)
                {
                    if (instance._transaction != null)
                    {
                        throw new ScopedDbConnectionException(
                            "Cannot dispose connection while a transaction is active");
                    }

                    await instance._connection!.DisposeAsync();
                    instance._connection = null;
                }

                // Sanity
                if (instance._connectionRefCount < 0)
                {
                    throw new ScopedDbConnectionException(
                        $"Connection ref count is negative ({instance._connectionRefCount}). This should never happen");
                }
            }
        }
    }

    //

    public sealed class TransactionAccessor(ScopedConnectionFactory<T> instance) : IDisposable, IAsyncDisposable
    {
        public DbTransaction Instance => instance._transaction!;
        public int RefCount => instance._transactionRefCount;

        public async Task CommitAsync()
        {
            using (await instance._mutex.LockAsync())
            {
                if (instance._transactionRefCount == 1)
                {
                    await instance._transaction!.CommitAsync();
                }
            }
        }

        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        public async ValueTask DisposeAsync()
        {
            using (await instance._mutex.LockAsync())
            {
                // Sanity
                if (instance._connection == null)
                {
                    throw new ScopedDbConnectionException(
                        "No connection available to dispose transaction. This should never happen.");
                }

                if (--instance._transactionRefCount == 0)
                {
                    await instance._transaction!.DisposeAsync();
                    instance._transaction = null!;
                }

                // Sanity
                if (instance._transactionRefCount < 0)
                {
                    throw new ScopedDbConnectionException("Transaction nesting is negative. This should never happen.");
                }
            }
        }
    }
}

public class ScopedDbConnectionException(string message) : OdinSystemException(message);

public class ScopedSystemConnectionFactory(
    ILogger<ScopedSystemConnectionFactory> logger,
    ISystemDbConnectionFactory connectionFactory)
    : ScopedConnectionFactory<ISystemDbConnectionFactory>(logger, connectionFactory)
{
}

public class ScopedIdentityConnectionFactory(
    ILogger<ScopedIdentityConnectionFactory> logger,
    ITenantDbConnectionFactory connectionFactory)
    : ScopedConnectionFactory<ITenantDbConnectionFactory>(logger, connectionFactory)
{
}
