using System;
using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Database;
using Odin.Core.Util;

namespace Odin.Core.Storage.Factory;

#nullable enable

/// <summary>
/// Provides a factory for creating and managing DI scoped database connections, transactions, and commands
/// for use within a specific dependency injection scope.
/// This class is not thread-safe and should be used within a scoped service. If you need to run parallel tasks (or threads)
/// within a single scope, you must create a new child scope for each task.
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

//
// Interfaces
//

public interface IScopedConnectionFactory
{
    Task<IConnectionWrapper> CreateScopedConnectionAsync(
        [CallerFilePath] string? filePath = null,
        [CallerLineNumber] int lineNumber = 0);

    DatabaseType DatabaseType { get; }
}

public interface IConnectionWrapper : IDisposable, IAsyncDisposable
{
    DbConnection DangerousInstance { get; }
    int RefCount { get; }
    Task<ITransactionWrapper> BeginStackedTransactionAsync(
        IsolationLevel isolationLevel = IsolationLevel.Unspecified,
        CancellationToken cancellationToken = default);
    ICommandWrapper CreateCommand([CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = 0);
}

public interface ITransactionWrapper : IDisposable, IAsyncDisposable
{
    DbTransaction DangerousInstance { get; }
    int RefCount { get; }
    void Commit();
}

public interface ICommandWrapper : IDisposable, IAsyncDisposable
{
    DbCommand DangerousInstance { get; }
    string CommandText { get; set; }
    int CommandTimeout { get; set; }
    CommandType CommandType { get; set; }
    DbParameterCollection Parameters { get; }
    void Cancel();
    DbParameter CreateParameter();
    Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default);
    Task<DbDataReader> ExecuteReaderAsync(
        CommandBehavior behavior = CommandBehavior.Default,
        CancellationToken cancellationToken = default);
    Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken = default);
    Task PrepareAsync(CancellationToken cancellationToken = default);
}

//
// Implementation
//

public class ScopedConnectionFactory<T>(
    ILifetimeScope lifetimeScope,
    ILogger<ScopedConnectionFactory<T>> logger,
    T connectionFactory,
    CacheHelper cache,
    DatabaseCounters counters) : IScopedConnectionFactory where T : IDbConnectionFactory
{
    // ReSharper disable once StaticMemberInGenericType
    private static readonly ConcurrentDictionary<Guid, string> Diagnostics = new();

    private readonly ILogger<ScopedConnectionFactory<T>> _logger = logger;
    private readonly T _connectionFactory = connectionFactory;
    private readonly CacheHelper _cache = cache; // SEB:NOTE ported from earlier db code, cache needs redesign
    private readonly DatabaseCounters _counters = counters;
    private readonly AsyncLock _mutex = new();
    private DbConnection? _connection;
    private int _connectionRefCount;
    private DbTransaction? _transaction;
    private int _transactionRefCount;
    private bool _commit;
    private Guid _connectionId;
    private DbCommand? _command;
    private string _commandDiagnostics = "";

    public DatabaseType DatabaseType => _connectionFactory.DatabaseType;

    //

    public async Task<IConnectionWrapper> CreateScopedConnectionAsync(
        [CallerFilePath] string? filePath = null,
        [CallerLineNumber] int lineNumber = 0)
    {
        using (await _mutex.LockAsync())
        {
            if (_connectionRefCount == 0)
            {
                _counters.IncrementNoDbOpened();
                _connection = await _connectionFactory.OpenAsync();
                _connectionId = Guid.NewGuid();
                Diagnostics[_connectionId] = $"scope:{lifetimeScope.Tag} {filePath}:{lineNumber}";

                LogTrace("Created connection");
            }

            _connectionRefCount++;

            // if (_logger.IsEnabled(LogLevel.Trace))
            // {
            //     LogTrace($"Access connection ref: {_connectionRefCount} on {Environment.CurrentManagedThreadId} at {filePath}:{lineNumber}");
            // }

            // Sanity
            if (_connectionRefCount != 0 && _connection == null)
            {
                var message =
                    $"Connection ref count is {_connectionRefCount} but connection is null. This should never happen";
                LogError(message);
                throw new OdinDatabaseException(DatabaseType, message);
            }

            return new ConnectionWrapper(this);
        }
    }

    //

    private void LogDiagnostics()
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            foreach (var (guid, info) in Diagnostics)
            {
                _logger.LogDebug("Connection {id} was created at {info}", guid, info);
            }
        }
    }

    //

    private void LogError(string message)
    {
        LogDiagnostics();
        _logger.LogError("{message} (ScopedConnectionFactory:{id} scope:{tag})",
            message, _connectionId, lifetimeScope.Tag);
        _logger.LogError(Environment.StackTrace);
    }

    //

    private void LogException(string message, Exception exception)
    {
        LogDiagnostics();

        // SEB:NOTE we log the exception as a non-error, because it should be possible for the caller
        // to catch and handle the exception silently (e.g. in case of an expected sql constraint error),
        // but we prefix it with an "ERR" to make it easier to spot in the logs.
        _logger.LogDebug(exception, "ERR {message}: {error} (ScopedConnectionFactory:{id} scope:{tag})",
            message, exception.Message, _connectionId, lifetimeScope.Tag);
    }

    //

    private void LogTrace(string message)
    {
        if (_logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogTrace("{message} (ScopedConnectionFactory:{id} scope:{tag})",
                message, _connectionId, lifetimeScope.Tag);
        }
    }

    //

    //
    // ConnectionWrapper
    // A wrapper around a DbConnection that ensures that the transaction is disposed correctly.
    //
    public sealed class ConnectionWrapper(ScopedConnectionFactory<T> instance) : IConnectionWrapper
    {
        private bool _disposed;
        public DbConnection DangerousInstance => instance._connection!;
        public int RefCount => instance._connectionRefCount;

        //

        public async Task<ITransactionWrapper> BeginStackedTransactionAsync(
            IsolationLevel isolationLevel = IsolationLevel.Unspecified,
            CancellationToken cancellationToken = default)
        {
            using (await instance._mutex.LockAsync(cancellationToken))
            {
                if (instance._connection == null)
                {
                    instance.LogError("No connection available to begin transaction");
                    throw new OdinDatabaseException(instance.DatabaseType, "No connection available to begin transaction");
                }

                if (instance._transactionRefCount == 0)
                {
                    try
                    {
                        instance.LogTrace("Beginning transaction");
                        instance._transaction = await instance._connection.BeginTransactionAsync(isolationLevel, cancellationToken);
                    }
                    catch (Exception e)
                    {
                        instance.LogException("BeginTransactionAsync failed", e);
                        throw new OdinDatabaseException(instance.DatabaseType, "BeginTransactionAsync failed", e);
                    }
                }

                instance._transactionRefCount++;

                // Sanity
                if (instance._transactionRefCount != 0 && instance._transaction == null)
                {
                    var message =
                        $"Transaction ref count is {instance._transactionRefCount} but transaction is null. This should never happen";
                    instance.LogError(message);
                    throw new OdinDatabaseException(instance.DatabaseType, message);
                }

                return new TransactionWrapper(instance);
            }
        }

        //

        public ICommandWrapper CreateCommand(
            [CallerFilePath] string? filePath = null,
            [CallerLineNumber] int lineNumber = 0)
        {
            using (instance._mutex.Lock())
            {
                if (instance._connection == null)
                {
                    instance.LogError("No connection available to create command");
                    throw new OdinDatabaseException(instance.DatabaseType, "No connection available to create command");
                }

                // We only allow one command to be created at a time per connection. This is to prevent multiple
                // commands from being executed concurrently on the same connection, which is not supported by
                // most drivers. While this makes it more cumbersome to run multiple commands, it makes
                // troubleshooting easier and prevents potential issues.
                if (instance._command != null)
                {
                    var error = $"Only one command can be created at a time per connection. Current command: {instance._commandDiagnostics}";
                    instance.LogError(error);
                    throw new OdinDatabaseException(instance.DatabaseType, error);
                }

                instance._commandDiagnostics = $"tid:{Environment.CurrentManagedThreadId} at {filePath}:{lineNumber}";
                instance.LogTrace($" Creating command on {instance._commandDiagnostics}");

                instance._command = instance._connection.CreateCommand();
                return new CommandWrapper(instance, instance._command);
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
            using (await instance._mutex.LockAsync())
            {
                if (_disposed)
                {
                    instance.LogError("Connection already disposed");
                    return;
                }

                if (--instance._connectionRefCount == 0)
                {
                    if (instance._transaction != null)
                    {
                        const string message = "Cannot dispose connection while a transaction is active";
                        instance.LogError(message);
                        throw new OdinDatabaseException(instance.DatabaseType, message);
                    }

                    if (instance._command != null)
                    {
                        const string message = "Cannot dispose connection while a command is active";
                        instance.LogError(message);
                        throw new OdinDatabaseException(instance.DatabaseType, message);
                    }

                    instance.LogTrace("Disposing connection");
                    instance._counters.IncrementNoDbClosed();

                    await instance._connectionFactory.CloseAsync(instance._connection!);
                    instance._connection = null;
                    _disposed = true;

                    Diagnostics.TryRemove(instance._connectionId, out _);

                    instance.LogTrace("Disposed connection");
                }

                // Sanity
                if (instance._connectionRefCount < 0)
                {
                    var message =
                        $"Connection ref count is negative ({instance._connectionRefCount}). This should never happen";
                    instance.LogError(message);
                    throw new OdinDatabaseException(instance.DatabaseType, message);
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
    public sealed class TransactionWrapper(ScopedConnectionFactory<T> instance) : ITransactionWrapper
    {
        private bool _disposed;
        public DbTransaction DangerousInstance => instance._transaction!;
        public int RefCount => instance._transactionRefCount;

        //

        // Note that only outermost transaction is marked as commit.
        // The disposer takes care of the actual commit (or rollback).
        // The reason we don't do an explicit commit here is because it leaves the internal
        // transaction in an odd state, and it's unclear how a cmd following the commit (or rollback)
        // behaves.
        public void Commit()
        {
            using (instance._mutex.Lock())
            {
                if (instance._transaction == null)
                {
                    const string message = "No transaction available";
                    instance.LogError(message);
                    throw new OdinDatabaseException(instance.DatabaseType, message);
                }

                if (instance._transactionRefCount == 1)
                {
                    instance._commit = true;
                }
            }
        }

        //

        // There is no explicit rollback support. This is by design to make reference counting easier.
        // If you want to rollback, simply do not commit.
        // ReSharper disable once UnusedMember.Local
        private void Rollback()
        {
            // Do nothing
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
                if (_disposed)
                {
                    instance.LogError("Transaction already disposed");
                    return;
                }

                // Sanity
                if (instance._connection == null)
                {
                    const string message = "No connection available to dispose transaction. This should never happen.";
                    instance.LogError(message);
                    throw new OdinDatabaseException(instance.DatabaseType, message);
                }

                if (instance._transaction == null)
                {
                    return;
                }

                if (--instance._transactionRefCount == 0)
                {
                    instance.LogTrace("Disposing transaction");

                    try
                    {
                        if (instance._commit)
                        {
                            await instance._transaction!.CommitAsync();
                        }
                        else
                        {
                            await instance._transaction!.RollbackAsync();
                            instance._cache.ClearCache();
                        }
                    }
                    finally
                    {
                        await instance._transaction!.DisposeAsync();
                        instance._transaction = null!;
                        instance._commit = false;
                        _disposed = true;
                    }

                    instance.LogTrace("Disposed transaction");
                }

                // Sanity
                if (instance._transactionRefCount < 0)
                {
                    const string message = "Transaction stacking is negative. This should never happen.";
                    instance.LogError(message);
                    throw new OdinDatabaseException(instance.DatabaseType, message);
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
    public sealed class CommandWrapper(ScopedConnectionFactory<T> instance, DbCommand command) : ICommandWrapper

    {
        private bool _disposed;
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
            instance._counters.IncrementNoDbExecuteNonQueryAsync();

            try
            {
                using (await instance._mutex.LockAsync(cancellationToken))
                {
                    instance.LogTrace("  ExecuteNonQueryAsync start");
                    command.Transaction = instance._transaction;
                    var result = await command.ExecuteNonQueryAsync(cancellationToken);
                    instance.LogTrace("  ExecuteNonQueryAsync done");
                    return result;
                }
            }
            catch (Exception e)
            {
                instance.LogException("ExecuteNonQueryAsync failed", e);
                throw new OdinDatabaseException(instance.DatabaseType, "ExecuteNonQueryAsync failed", e);
            }
        }

        //

        public async Task<DbDataReader> ExecuteReaderAsync(
            CommandBehavior behavior = CommandBehavior.Default, CancellationToken cancellationToken = default)
        {
            instance._counters.IncrementNoDbExecuteReaderAsync();

            try
            {
                using (await instance._mutex.LockAsync(cancellationToken))
                {
                    instance.LogTrace("  ExecuteReaderAsync start");
                    command.Transaction = instance._transaction;
                    var result = await command.ExecuteReaderAsync(behavior, cancellationToken);
                    instance.LogTrace("  ExecuteReaderAsync done");
                    return result;
                }
            }
            catch (Exception e)
            {
                instance.LogException("ExecuteReaderAsync failed", e);
                throw new OdinDatabaseException(instance.DatabaseType, "ExecuteReaderAsync failed", e);
            }
        }

        //

        public async Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken = default)
        {
            instance._counters.IncrementNoDbExecuteScalarAsync();

            try
            {
                using (await instance._mutex.LockAsync(cancellationToken))
                {
                    instance.LogTrace("  ExecuteScalarAsync start");
                    command.Transaction = instance._transaction;
                    var result = await command.ExecuteScalarAsync(cancellationToken);
                    instance.LogTrace("  ExecuteScalarAsync done");
                    return result;
                }
            }
            catch (Exception e)
            {
                instance.LogException("ExecuteScalarAsync failed", e);
                throw new OdinDatabaseException(instance.DatabaseType, "ExecuteScalarAsync failed", e);
            }
        }

        //

        public async Task PrepareAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using (await instance._mutex.LockAsync(cancellationToken))
                {
                    instance.LogTrace("  PrepareAsync start");
                    command.Transaction = instance._transaction;
                    await command.PrepareAsync(cancellationToken);
                    instance.LogTrace("  PrepareAsync done");
                }
            }
            catch (Exception e)
            {
                instance.LogException("PrepareAsync failed", e);
                throw new OdinDatabaseException(instance.DatabaseType, "PrepareAsync failed", e);
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

            using (await instance._mutex.LockAsync())
            {
                if (_disposed)
                {
                    instance.LogError("Command already disposed");
                    return;
                }

                _disposed = true;
                instance._command = null;

                instance.LogTrace(" Disposing command");
                await command.DisposeAsync();
                instance.LogTrace(" Disposed command");
            }
        }

        //

        ~CommandWrapper()
        {
            FinalizerError.ReportMissingDispose(GetType(), instance._logger);
        }
    }

    //
}

