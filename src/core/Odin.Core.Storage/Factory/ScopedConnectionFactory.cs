using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.System.Connection;
using Odin.Core.Util;

namespace Odin.Core.Storage.Factory;

    #nullable enable

    /// <summary>
    /// Provides a factory for creating and managing DI scoped database connections, transactions, and commands
    /// in a thread-safe manner for use within a specific dependency injection scope. This factory ensures that each 
    /// scoped connection and transaction is properly synchronized, allowing for controlled access even 
    /// in multi-threaded environments.
    /// </summary>
    /// <typeparam name="T">The type of the connection factory, which must implement <see cref="IDbConnectionFactory"/>.</typeparam>
    /// <remarks>
    /// <para>
    /// <b>Usage:</b> This class is designed to be used within a scoped service. When creating parallel tasks
    /// or threads, ensure each runs within its own scope by creating a new instance of <see cref="ScopedConnectionFactory{T}"/>
    /// within that scope. This approach avoids sharing a single instance across threads, which could lead to 
    /// concurrency issues.
    /// </para>
    /// <para>
    /// <b>Thread Safety:</b> ALWAYS create a new DI scope for each parallel task or thread. This ensures that each
    /// task or thread will work on its own connection and transaction, preventing concurrency issues.
    /// Be careful when using this class in singleton-registrations: make sure scopes are created and disposed correctly.
    /// `ScopedConnectionFactory` uses an asynchronous lock to ensure only one thread or 
    /// task can access the connection at a time, preventing issues with `DbConnection`'s lack of thread safety. 
    /// To simulate nested transactions (which are not natively supported in SQLite), the factory employs a reference 
    /// count on transactions, committing or rolling back only at the outermost transaction level.
    /// </para>
    /// <para>
    /// <b>Disposal:</b> Proper disposal is crucial for managing database resources effectively. Consumers should 
    /// dispose of <see cref="ConnectionWrapper"/>, <see cref="TransactionWrapper"/>, and <see cref="CommandWrapper"/> 
    /// instances once they are no longer needed. Failure to call <see cref="Dispose"/> or <see cref="DisposeAsync"/> 
    /// will result in a warning in debug mode and may lead to resource leaks in production.
    /// </para>
    /// <example>
    /// The following examples demonstrates how to use <see cref="ScopedConnectionFactory{T}"/> within a scoped 
    /// service to create and manage a database connection and transaction:
    /// <code>
    /// // Autofac
    /// await using var scope = _container.BeginLifetimeScope();
    /// var scopedConnectionFactory = scope.Resolve&lt;ScopedSystemConnectionFactory&gt;();
    /// await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
    /// await using (var tx = await cn.BeginStackedTransactionAsync())
    /// {
    ///    await using var cmd = cn.CreateCommand();
    ///    cmd.CommandText = "INSERT INTO test (name) VALUES ('test');";
    ///    await cmd.ExecuteNonQueryAsync();
    ///    await tx.CommitAsync();
    /// }
    /// </code>
    /// <code>
    /// // Microsoft DI
    /// await using var scope = _services.CreateScope();
    /// var scopedConnectionFactory = scope.GetRequiredService&lt;ScopedSystemConnectionFactory&gt;();
    /// await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
    /// await using (var tx = await cn.BeginStackedTransactionAsync())
    /// {
    ///    await using var cmd = cn.CreateCommand();
    ///    cmd.CommandText = "INSERT INTO test (name) VALUES ('test');";
    ///    await cmd.ExecuteNonQueryAsync();
    ///    await tx.CommitAsync();
    /// }
    /// </code>
    /// <code>
    /// // Parallel tasks MUST use isolated scopes:
    /// var tasks = new List&lt;Task&gt;();
    /// var barrier = new Barrier(2);
    ///     
    /// tasks.Add(Task.Run(async () =&gt;
    /// {
    ///    using var scope = _services.CreateScope();
    ///    var scopedConnectionFactory = scope.ServiceProvider.GetRequiredService&lt;ScopedSystemConnectionFactory&gt;();
    ///         
    ///    barrier.SignalAndWait();
    ///         
    ///    await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
    ///    await using var cmd = cn.CreateCommand();
    ///    cmd.CommandText = "INSERT INTO test (name) VALUES ('test 1');";
    ///    await cmd.ExecuteNonQueryAsync();
    /// }));
    /// tasks.Add(Task.Run(async () =&gt;
    /// {
    ///    using var scope = _services.CreateScope();
    ///    var scopedConnectionFactory = scope.ServiceProvider.GetRequiredService&lt;ScopedSystemConnectionFactory&gt;();
    ///         
    ///    barrier.SignalAndWait();
    ///         
    ///    await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
    ///    await using var cmd = cn.CreateCommand();
    ///    cmd.CommandText = "INSERT INTO test (name) VALUES ('test 2');";
    ///    await cmd.ExecuteNonQueryAsync();
    /// }));
    /// await Task.WhenAll(tasks);
    /// </code>
    /// </example> 
    /// </remarks>

    public class ScopedConnectionFactory<T>(ILogger<ScopedConnectionFactory<T>> logger, T connectionFactory)
        where T : IDbConnectionFactory
    {
        private readonly ILogger<ScopedConnectionFactory<T>> _logger = logger;
        private readonly T _connectionFactory = connectionFactory;
        private readonly AsyncLock _mutex = new();
        private DbConnection? _connection;
        private int _connectionRefCount;
        private DbTransaction? _transaction;
        private int _transactionRefCount;

        //
        
        public async Task<ConnectionWrapper> CreateScopedConnectionAsync()
        {
            _logger.LogTrace("Creating connection");
            
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

                return new ConnectionWrapper(this);
            }
        }

        //

        private async Task<TransactionWrapper> BeginStackedTransactionAsync(
            IsolationLevel isolationLevel = IsolationLevel.Unspecified, 
            CancellationToken cancellationToken = default)
        {
            _logger.LogTrace("Beginning transaction");

            using (await _mutex.LockAsync(cancellationToken))
            {
                if (_connection == null)
                {
                    throw new ScopedDbConnectionException("No connection available to begin transaction");
                }

                if (++_transactionRefCount == 1)
                {
                    _transaction = await _connection.BeginTransactionAsync(isolationLevel, cancellationToken);
                }

                // Sanity
                if (_transactionRefCount != 0 && _transaction == null)
                {
                    throw new ScopedDbConnectionException(
                        $"Transaction ref count is {_transactionRefCount} but transaction is null. This should never happen");
                }

                return new TransactionWrapper(this);
            }
        }

        //
        
        private CommandWrapper CreateCommand()
        {
            using (_mutex.Lock())
            {
                if (_connection == null)
                {
                    throw new ScopedDbConnectionException("No connection available to create command");
                }
                return new CommandWrapper(this, _connection.CreateCommand());
            }
        }
        
        //
        
        //
        // ConnectionWrapper
        // A wrapper around a DbConnection that ensures that the transaction is disposed correctly.
        // 
        public sealed class ConnectionWrapper(ScopedConnectionFactory<T> instance) : IDisposable, IAsyncDisposable
        {
            public DbConnection DangerousInstance => instance._connection!;
            public int RefCount => instance._connectionRefCount;

            //
            
            public async Task<TransactionWrapper> BeginStackedTransactionAsync()
            {
                return await instance.BeginStackedTransactionAsync();
            }
            
            //
            
            public CommandWrapper CreateCommand()
            {
                return instance.CreateCommand();
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
            
            //

            ~ConnectionWrapper()
            {
                FinalizerError.ReportMissingDispose(GetType(), instance._logger);
            }
        }
        
        //
        
        //
        // TransactionWrapper
        // A wrapper around a DbTransaction that supports stacked transactions
        // and ensures that the transaction is disposed correctly.
        //
        // NOTE from MSDN:
        // Dispose should rollback the transaction.
        // However, the behavior of Dispose is provider specific, and should not replace calling Rollback.
        //
        public sealed class TransactionWrapper(ScopedConnectionFactory<T> instance) : IDisposable, IAsyncDisposable
        {
            public DbTransaction DangerousInstance => instance._transaction!;
            public int RefCount => instance._transactionRefCount;
            
            //

            // Note that only outermost transaction is committed.
            public async Task CommitAsync(CancellationToken cancellationToken = default)
            {
                using (await instance._mutex.LockAsync(cancellationToken))
                {
                    if (instance._transactionRefCount == 1)
                    {
                        await instance._transaction!.CommitAsync(cancellationToken);
                    }
                }
            }
            
            //

            // Note that only outermost transaction is rolled back.
            public async Task RollbackAsync(CancellationToken cancellationToken = default)
            {
                using (await instance._mutex.LockAsync(cancellationToken))
                {
                    if (instance._transactionRefCount == 1)
                    {
                        await instance._transaction!.RollbackAsync(cancellationToken);
                    }
                }
            }
            
            //

            public Task SaveAsync(string savepointName, CancellationToken cancellationToken = default)
            {
                // NOTE: not supported by all providers
                return instance._transaction!.SaveAsync(savepointName, cancellationToken);
            }
            
            //
            
            public Task ReleaseAsync(string savepointName, CancellationToken cancellationToken = default)
            {
                // NOTE: not supported by all providers
                return instance._transaction!.ReleaseAsync(savepointName, cancellationToken);
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
                        throw new ScopedDbConnectionException("Transaction stacking is negative. This should never happen.");
                    }
                }
            }
            
            //
            
            ~TransactionWrapper()
            {
                FinalizerError.ReportMissingDispose(GetType(), instance._logger);
            }
        }
        
        //

        //
        // CommandWrapper
        // A wrapper around a DbCommand that ensures that the command is disposed correctly.
        // All non-trivial access to the underlying DbCommand is synchronized. This is necessary because DbConnection is
        // not thread-safe and the DbConnection may be shared between multiple threads when running parallel tasks
        // and having "forgotten" to create a new scope foreach parallel task (or thread). Since DbCommand directly
        // accesses the DbConnection, we make sure it is synchronized.
        //
        public sealed class CommandWrapper(ScopedConnectionFactory<T> instance, DbCommand command) 
            : IDisposable, IAsyncDisposable
        
        {
            public DbCommand DangerousInstance => command;
            
            //
            
            public string CommandText
            {
                get => command.CommandText;
                set => command.CommandText = value;
            }

            //
            
            public int CommandTimeout
            {
                get => command.CommandTimeout;
                set => command.CommandTimeout = value;
            }
            
            //
            
            public CommandType CommandType
            {
                get => command.CommandType;
                set => command.CommandType = value;
            }
            
            //
            
            public DbParameterCollection Parameters => command.Parameters;
            
            //

            public void Cancel()
            {
                using (instance._mutex.Lock())
                {
                    command.Cancel();
                }
            }
            
            //
            public DbParameter CreateParameter()
            {
                using (instance._mutex.Lock())
                {
                    return command.CreateParameter();
                }
            }
            
            //

            public async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default)
            {
                using (await instance._mutex.LockAsync(cancellationToken))
                {
                    return await command.ExecuteNonQueryAsync(cancellationToken);
                }
            }
            
            //

            public async Task<DbDataReader> ExecuteReaderAsync(
                CommandBehavior behavior = CommandBehavior.Default, CancellationToken cancellationToken = default)
            {
                using (await instance._mutex.LockAsync(cancellationToken))
                {
                    return await command.ExecuteReaderAsync(behavior, cancellationToken);
                }
            }
            
            //
                
            public async Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken = default)
            {
                using (await instance._mutex.LockAsync(cancellationToken))
                {
                    return await command.ExecuteScalarAsync(cancellationToken);
                }
            }
            
            //
            
            public async Task PrepareAsync(CancellationToken cancellationToken = default)
            {
                using (await instance._mutex.LockAsync(cancellationToken))
                {
                    await command.PrepareAsync(cancellationToken);
                }
            }

            //
           
            public void Dispose()
            {
                GC.SuppressFinalize(this);
                command.Dispose();
            }
            
            //

            public async ValueTask DisposeAsync()
            {
                GC.SuppressFinalize(this);
                await command.DisposeAsync();
            }
            
            //
            
            ~CommandWrapper()
            {
                FinalizerError.ReportMissingDispose(GetType(), instance._logger);
            }
        }

        //
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
        IIdentityDbConnectionFactory connectionFactory)
        : ScopedConnectionFactory<IIdentityDbConnectionFactory>(logger, connectionFactory)
    {
    }
