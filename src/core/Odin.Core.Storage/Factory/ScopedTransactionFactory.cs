using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace Odin.Core.Storage.Factory;

/// <summary>
/// Provides a thin wrapper of a scoped connection with an active transaction. See <see cref="ScopedConnectionFactory{T}"/>.
/// </summary>
/// <remarks>
/// Only use this wrapper when you have short-lived transactions. As soon as you call <see cref="BeginStackedTransactionAsync"/>
/// the database WILL lock the one or more table.
/// </remarks>

//
// Interfaces
//

public interface IScopedTransactionFactory
{
    Task<IScopedTransaction> BeginStackedTransactionAsync(
        IsolationLevel isolationLevel = IsolationLevel.Unspecified,
        CancellationToken cancellationToken = default);
}

//

public interface IScopedTransaction : IDisposable, IAsyncDisposable
{
    IConnectionWrapper Connection { get; }
    ITransactionWrapper Transaction { get; }
    ICommandWrapper CreateCommand();
    void Commit();
    void AddPostCommitAction(Func<Task> action);
    void AddPostRollbackAction(Func<Task> action);
}

//
// Implementation
//

public class ScopedTransactionFactory<T>(ScopedConnectionFactory<T> scopedConnectionFactory)
    : IScopedTransactionFactory where T : IDbConnectionFactory
{
    public async Task<IScopedTransaction> BeginStackedTransactionAsync(
        IsolationLevel isolationLevel = IsolationLevel.Unspecified,
        CancellationToken cancellationToken = default)
    {
        var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
        try
        {
            var tx = await cn.BeginStackedTransactionAsync(isolationLevel, cancellationToken);
            return new ScopedTransaction(cn, tx);
        }
        catch (Exception e)
        {
            await cn.DisposeAsync();
            throw new OdinDatabaseException(scopedConnectionFactory.DatabaseType, "BeginStackedTransactionAsync failed", e);
        }
    }

    //

    private sealed class ScopedTransaction(IConnectionWrapper cn, ITransactionWrapper tx) : IScopedTransaction
    {
        public IConnectionWrapper Connection => cn;
        public ITransactionWrapper Transaction => tx;

        public ICommandWrapper CreateCommand()
        {
            return cn.CreateCommand();
        }

        // Note that only outermost transaction is marked as commit.
        // The disposer takes care of the actual commit (or rollback)
        public void Commit()
        {
            tx.Commit();
        }

        public void AddPostCommitAction(Func<Task> action)
        {
            tx.AddPostCommitAction(action);
        }

        public void AddPostRollbackAction(Func<Task> action)
        {
            tx.AddPostRollbackAction(action);
        }

        // There is no explicit rollback support. This is by design to make reference counting easier.
        // If you want to rollback, simply do not commit.
        private void Rollback()
        {
            // Do nothing
        }

        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        public async ValueTask DisposeAsync()
        {
            await tx.DisposeAsync();
            await cn.DisposeAsync();
        }
    }

    //
}
