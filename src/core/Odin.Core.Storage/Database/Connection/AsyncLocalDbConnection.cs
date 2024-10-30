using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Nito.AsyncEx;
using Odin.Core.Exceptions;

namespace Odin.Core.Storage.Database.Connection;

#nullable enable

/*
    When dealing with either parallel tasks or threads, it is important to ensure that they run in a new async context:
        await Task.Run(() => asyncLocalDbConnection.CreateConnectionAsync());
    or
        using (ExecutionContext.SuppressFlow())
       {
           await asyncLocalDbConnection.CreateConnectionAsync();
       }
    or
       ExecutionContext executionContext = new ExecutionContext();
       ExecutionContext.Run(executionContext, async _ =>
       {
           await asyncLocalDbConnection.CreateConnectionAsync();
       }, null);

    NONE of these are very nice which is why we should use the ScopedDbConnection class instead
 */


public class AsyncLocalDbConnection(IDbConnectionFactory connectionFactory)
{
    private static readonly AsyncLock Mutex = new();
    private static readonly AsyncLocal<DbConnection> ScopedConnection = new();
    private static readonly AsyncLocal<long> ScopedConnectionCount = new();
    private static readonly AsyncLocal<DbTransaction> NestedTransaction = new();
    private static readonly AsyncLocal<long> NestedTransactionLevel = new();

    public async Task<(IAsyncDisposable asyncDisposer, DbConnection connection)> CreateConnectionAsync()
    {
        using (await Mutex.LockAsync())
        {
            if (++ScopedConnectionCount.Value == 1)
            {
                ScopedConnection.Value = await connectionFactory.CreateAsync();
            }
            var connection = ScopedConnection.Value!;
            return (new ConnectionDisposer(), connection);
        }
    }

    public async Task<(IAsyncDisposable asyncDisposer, DbConnection connection, DbTransaction transaction)> BeginNestedTransactionAsync()
    {
        using (await Mutex.LockAsync())
        {
            var connection = ScopedConnection.Value;
            if (connection == null)
            {
                throw new AsyncLocalDbConnectionException("No connection available to begin transaction");
            }

            if (++NestedTransactionLevel.Value == 1)
            {
                NestedTransaction.Value = await connection.BeginTransactionAsync();
            }

            var transaction = NestedTransaction.Value!;

            return (new TransactionDisposer(), connection, transaction);
        }
    }

    private sealed class ConnectionDisposer : IDisposable, IAsyncDisposable
    {
        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        public async ValueTask DisposeAsync()
        {
            using (await Mutex.LockAsync())
            {
                if (--ScopedConnectionCount.Value == 0)
                {
                    if (NestedTransaction.Value != null)
                    {
                        throw new AsyncLocalDbConnectionException("Cannot dispose connection while a transaction is active");
                    }
                    await ScopedConnection.Value!.DisposeAsync();
                    ScopedConnection.Value = null!;
                }
#if DEBUG
                if (ScopedConnectionCount.Value < 0)
                {
                    throw new AsyncLocalDbConnectionException("Scoped connection count is negative. This should never happen");
                }
#endif
            }
        }
    }

    private sealed class TransactionDisposer : IDisposable, IAsyncDisposable
    {
        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        public async ValueTask DisposeAsync()
        {
            using (await Mutex.LockAsync())
            {
                var connection = ScopedConnection.Value;
                if (connection == null)
                {
                    throw new AsyncLocalDbConnectionException("No connection available to dispose transaction. This should never happen");
                }

                if (--NestedTransactionLevel.Value == 0)
                {
                    await NestedTransaction.Value!.DisposeAsync();
                    NestedTransaction.Value = null!;
                }
#if DEBUG
                if (NestedTransactionLevel.Value < 0)
                {
                    throw new AsyncLocalDbConnectionException("Transaction nesting is negative. This should never happen");
                }
#endif

            }
        }
    }

}

public class AsyncLocalDbConnectionException(string message) : OdinSystemException(message);