using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
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
///    tx.Commit();
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
///    tx.Commit();
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
    bool HasTransaction { get; }
    void AddPostCommitAction(Func<Task> action);
    void AddPostRollbackAction(Func<Task> action);
}

//

public interface IConnectionWrapper : IDisposable, IAsyncDisposable
{
    DbConnection DangerousInstance { get; }
    DatabaseType DatabaseType { get; }
    int RefCount { get; }
    bool HasTransaction { get; }
    Task<ITransactionWrapper> BeginStackedTransactionAsync(
        IsolationLevel isolationLevel = IsolationLevel.Unspecified,
        CancellationToken cancellationToken = default);
    void AddPostCommitAction(Func<Task> action);
    void AddPostRollbackAction(Func<Task> action);
    ICommandWrapper CreateCommand([CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = 0);
}

//

public interface ITransactionWrapper : IDisposable, IAsyncDisposable
{
    DbTransaction DangerousInstance { get; }
    DatabaseType DatabaseType { get; }
    int RefCount { get; }
    void AddPostCommitAction(Func<Task> action);
    void AddPostRollbackAction(Func<Task> action);
    void Commit();
}

//

public interface ICommandWrapper : IDisposable, IAsyncDisposable
{
    DbCommand DangerousInstance { get; }
    DatabaseType DatabaseType { get; }
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
    DatabaseCounters counters) : IScopedConnectionFactory where T : IDbConnectionFactory
{
    // ReSharper disable once StaticMemberInGenericType
    private static readonly ConcurrentDictionary<Guid, string> Diagnostics = new();

    private readonly ILogger<ScopedConnectionFactory<T>> _logger = logger;
    private readonly T _connectionFactory = connectionFactory;
    private readonly DatabaseCounters _counters = counters;
    private readonly List<Func<Task>> _postCommitActions = [];
    private readonly List<Func<Task>> _postRollbackActions = [];
    private int _parallelDetectionRefCount;
    private DbConnection? _connection;
    private int _connectionRefCount;
    private DbTransaction? _transaction;
    private int _transactionRefCount;
    private bool _commit;
    private Guid _connectionId;

    public DatabaseType DatabaseType => _connectionFactory.DatabaseType;
    public bool HasTransaction => _transaction != null;

    //

    public async Task<IConnectionWrapper> CreateScopedConnectionAsync(
        [CallerFilePath] string? filePath = null,
        [CallerLineNumber] int lineNumber = 0)
    {
        using var _ = NoParallelism(nameof(CreateScopedConnectionAsync));

        if (_connectionRefCount == 0)
        {
            _counters.IncrementNoDbOpened();
            _connection = await _connectionFactory.OpenAsync();
            _connectionId = Guid.NewGuid();
            Diagnostics[_connectionId] = $"scope:{lifetimeScope.Tag} {filePath}:{lineNumber}";
            LogTrace("Created connection");
        }

        _connectionRefCount++;

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

    //

    public void AddPostCommitAction(Func<Task> action)
    {
        using var _ = NoParallelism(nameof(AddPostCommitAction));

        // Sanity #1
        if (_transaction == null)
        {
            const string message = "Must be in a transaction to add a post transaction commit action";
            LogError(message);
            throw new OdinDatabaseException(DatabaseType, message);
        }

        // Sanity #2
        if (_transactionRefCount == 0)
        {
            const string message =
                "Transaction ref count is zero. " +
                "Probably you tried to add a post transaction commit while the transaction is being disposed.";
            LogError(message);
            throw new OdinDatabaseException(DatabaseType, message);
        }

        _postCommitActions.Add(action);
    }

    //

    public void AddPostRollbackAction(Func<Task> action)
    {
        using var _ = NoParallelism(nameof(AddPostRollbackAction));

        // Sanity #1
        if (_transaction == null)
        {
            const string message = "Must be in a transaction to add a post transaction rollback action";
            LogError(message);
            throw new OdinDatabaseException(DatabaseType, message);
        }

        // Sanity #2
        if (_transactionRefCount == 0)
        {
            const string message =
                "Transaction ref count is zero. " +
                "Probably you tried to add a post transaction rollback while the transaction is being disposed.";
            LogError(message);
            throw new OdinDatabaseException(DatabaseType, message);
        }

        _postRollbackActions.Add(action);
    }

    //

    #region ConnectionWrapper

    //
    // ConnectionWrapper
    // A wrapper around a DbConnection that ensures that the connection is disposed correctly.
    //
    private sealed class ConnectionWrapper(ScopedConnectionFactory<T> instance) : IConnectionWrapper
    {
        private bool _disposed;
        public DbConnection DangerousInstance => instance._connection!;
        public DatabaseType DatabaseType => instance.DatabaseType;
        public int RefCount => instance._connectionRefCount;
        public bool HasTransaction => instance.HasTransaction;

        //

        public async Task<ITransactionWrapper> BeginStackedTransactionAsync(
            IsolationLevel isolationLevel = IsolationLevel.Unspecified,
            CancellationToken cancellationToken = default)
        {
            using var _ = instance.NoParallelism(nameof(BeginStackedTransactionAsync));

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

        //

        public void AddPostCommitAction(Func<Task> action)
        {
            instance.AddPostCommitAction(action);
        }

        //

        public void AddPostRollbackAction(Func<Task> action)
        {
            instance.AddPostRollbackAction(action);
        }

        //

        public ICommandWrapper CreateCommand(
            [CallerFilePath] string? filePath = null,
            [CallerLineNumber] int lineNumber = 0)
        {
            using var _ = instance.NoParallelism(nameof(CreateCommand));

            if (instance._connection == null)
            {
                instance.LogError("No connection available to create command");
                throw new OdinDatabaseException(instance.DatabaseType, "No connection available to create command");
            }

            if (instance._logger.IsEnabled(LogLevel.Trace))
            {
                instance.LogTrace(
                    $" Creating command on tid:{Environment.CurrentManagedThreadId} at {filePath}:{lineNumber}");
            }

            var command = instance._connection.CreateCommand();
            return new CommandWrapper(instance, command);
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
            // Don't check for parallelism here. Dispose must always be allowed to run.

            GC.SuppressFinalize(this);
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

        //

        ~ConnectionWrapper()
        {
            FinalizerError.ReportMissingDispose(GetType(), instance._logger);
        }
    }

    //

    #endregion

    //

    #region TransactionWrapper

    //
    // TransactionWrapper
    // A wrapper around a DbTransaction that supports stacked transactions
    // and ensures that the transaction is disposed correctly.
    //
    private sealed class TransactionWrapper(ScopedConnectionFactory<T> instance) : ITransactionWrapper
    {
        private bool _disposed;
        public DbTransaction DangerousInstance => instance._transaction!;
        public DatabaseType DatabaseType => instance.DatabaseType;
        public int RefCount => instance._transactionRefCount;

        //

        public void AddPostCommitAction(Func<Task> action)
        {
            instance.AddPostCommitAction(action);
        }

        //

        public void AddPostRollbackAction(Func<Task> action)
        {
            instance.AddPostRollbackAction(action);
        }

        //

        // Note that only outermost transaction is marked as commit.
        // The disposer takes care of the actual commit (or rollback).
        // The reason we don't do an explicit commit here is because it leaves the internal
        // transaction in an odd state, and it's unclear how a cmd following the commit (or rollback)
        // behaves.
        public void Commit()
        {
            using var _ = instance.NoParallelism(nameof(Commit));

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
            // Don't check for parallelism here. Dispose must always be allowed to run.

            GC.SuppressFinalize(this);
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
                        instance.LogTrace("Committing transaction");
                        await instance._transaction!.CommitAsync();
                        foreach (var action in instance._postCommitActions)
                        {
                            try
                            {
                                instance.LogTrace("Running post-commit action");
                                await action();
                            }
                            catch (Exception e)
                            {
                                instance.LogException("Post-commit action failed", e);
                            }
                        }
                    }
                    else
                    {
                        instance._logger.LogDebug("Rolling back transaction");
                        await instance._transaction!.RollbackAsync();
                        foreach (var action in instance._postRollbackActions)
                        {
                            try
                            {
                                instance.LogTrace("Running post-rollback action");
                                await action();
                            }
                            catch (Exception e)
                            {
                                instance.LogException("Post-rollback action failed", e);
                            }
                        }
                    }
                }
                finally
                {
                    await instance._transaction!.DisposeAsync();
                    instance._postCommitActions.Clear();
                    instance._postRollbackActions.Clear();
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

        //

        ~TransactionWrapper()
        {
            FinalizerError.ReportMissingDispose(GetType(), instance._logger);
        }
    }

    //

    #endregion

    //

    #region CommandWrapper

    //
    // CommandWrapper
    // A wrapper around a DbCommand that ensures that the command is disposed correctly.
    //
    private sealed class CommandWrapper(ScopedConnectionFactory<T> instance, DbCommand command) : ICommandWrapper
    {
        private readonly TimeSpan _queryRunTimeWarningThreshold = TimeSpan.FromSeconds(10);
        private bool _disposed;
        public DbCommand DangerousInstance => command;
        public DatabaseType DatabaseType => instance.DatabaseType;

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
            using var _ = instance.NoParallelism(nameof(Cancel));
            command.Cancel();
        }

        //
        public DbParameter CreateParameter()
        {
            using var _ = instance.NoParallelism(nameof(CreateParameter));
            return command.CreateParameter();
        }

        //

        public async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default)
        {
            using var _ = instance.NoParallelism(nameof(ExecuteNonQueryAsync));

            instance._counters.IncrementNoDbExecuteNonQueryAsync();

            try
            {
                instance.LogTrace("  ExecuteNonQueryAsync start");
                command.Transaction = instance._transaction;

                var start = Stopwatch.StartNew();
                var result = await command.ExecuteNonQueryAsync(cancellationToken);
                if (start.Elapsed > _queryRunTimeWarningThreshold)
                {
                    instance._logger.LogWarning("ExecuteNonQueryAsync - slow query: {query} took {time}",
                        command.CommandText, start.Elapsed);
                }

                instance.LogTrace("  ExecuteNonQueryAsync done");
                return result;
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
            using var _ = instance.NoParallelism(nameof(ExecuteReaderAsync));

            instance._counters.IncrementNoDbExecuteReaderAsync();

            try
            {
                instance.LogTrace("  ExecuteReaderAsync start");
                command.Transaction = instance._transaction;

                var start = Stopwatch.StartNew();
                var result = await command.ExecuteReaderAsync(behavior, cancellationToken);
                if (start.Elapsed > _queryRunTimeWarningThreshold)
                {
                    instance._logger.LogWarning("ExecuteReaderAsync - slow query: {query} took {time}",
                        command.CommandText, start.Elapsed);
                }

                instance.LogTrace("  ExecuteReaderAsync done");
                return result;
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
            using var _ = instance.NoParallelism(nameof(ExecuteScalarAsync));

            instance._counters.IncrementNoDbExecuteScalarAsync();

            try
            {
                instance.LogTrace("  ExecuteScalarAsync start");
                command.Transaction = instance._transaction;

                var start = Stopwatch.StartNew();
                var result = await command.ExecuteScalarAsync(cancellationToken);
                if (start.Elapsed > _queryRunTimeWarningThreshold)
                {
                    instance._logger.LogWarning("ExecuteScalarAsync - slow query: {query} took {time}",
                        command.CommandText, start.Elapsed);
                }

                instance.LogTrace("  ExecuteScalarAsync done");
                return result;
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
            using var _ = instance.NoParallelism(nameof(PrepareAsync));

            try
            {
                instance.LogTrace("  PrepareAsync start");
                command.Transaction = instance._transaction;
                await command.PrepareAsync(cancellationToken);
                instance.LogTrace("  PrepareAsync done");
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
            // Don't check for parallelism here. Dispose must always be allowed to run.

            GC.SuppressFinalize(this);
            if (_disposed)
            {
                instance.LogError("Command already disposed");
                return;
            }

            _disposed = true;

            instance.LogTrace(" Disposing command");
            await command.DisposeAsync();
            instance.LogTrace(" Disposed command");
        }

        //

        ~CommandWrapper()
        {
            FinalizerError.ReportMissingDispose(GetType(), instance._logger);
        }
    }

    #endregion

    //

    #region Diagnostics

    //

    private void LogDiagnostics()
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            foreach (var (guid, info) in Diagnostics)
            {
                _logger.LogDebug("DB diag: connection {id} was created at {info}", guid, info);
            }
        }
    }

    //

    private void LogError(string message)
    {
        LogDiagnostics();
        _logger.LogError("{message} (ScopedConnectionFactory:{id} scope:{tag}\n{stackTrace}",
            message, _connectionId, lifetimeScope.Tag, Environment.StackTrace);
    }

    //

    private void LogException(string message, Exception exception)
    {
        LogDiagnostics();

        // SEB:NOTE we log the exception as a non-error, because it should be possible for the caller
        // to catch and handle the exception silently (e.g. in case of an expected sql constraint error),
        // but we prefix it with "DBEX" to make it easier to spot in the logs.
        _logger.LogDebug(exception, "DBEX {message}: {error} (ScopedConnectionFactory:{id} scope:{tag})",
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

    #endregion

    //

    #region NoParallelism

    // SEB:NOTE
    // This method is used to detect parallelism, i.e. if the same instance of this class is used in multiple threads.
    // We used to have locking all around this class, but while that might save you when (mis)using the class across
    // different threads or parallel tasks, it would not alert you to the fact that you are doing something wrong.
    // Since we already know we're not thread-safe, we might as well make it explicit and throw an exception instead
    // of wasting cycles doing locking.
    private NoParallelismDisposer NoParallelism(string context, int expectedRefCount = 1)
    {
        if (Interlocked.Increment(ref _parallelDetectionRefCount) != expectedRefCount)
        {
            var message =
                $"Parallelism detected ({context}). " +
                "Use a new IOC scope for each parallel task or thread, or make sure to serialize calls with a lock.";
            LogError(message);
            throw new OdinDatabaseException(DatabaseType, message);
        }

        return new NoParallelismDisposer(this);
    }

    //

    private sealed class NoParallelismDisposer(ScopedConnectionFactory<T> instance) : IDisposable
    {
        public void Dispose()
        {
            Interlocked.Decrement(ref instance._parallelDetectionRefCount);
        }
    }

    #endregion

    //
}

