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
public class ScopedTransactionFactory<T>(ScopedConnectionFactory<T> scopedConnectionFactory) where T : IDbConnectionFactory
{
    public async Task<ScopedTransaction> BeginStackedTransactionAsync(
        IsolationLevel isolationLevel = IsolationLevel.Unspecified,
        CancellationToken cancellationToken = default)
    {
        var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
        var tx = await cn.BeginStackedTransactionAsync(isolationLevel, cancellationToken);
        return new ScopedTransaction(cn, tx);
    }

    public sealed class ScopedTransaction(
        ScopedConnectionFactory<T>.ConnectionWrapper cn,
        ScopedConnectionFactory<T>.TransactionWrapper tx) : IDisposable, IAsyncDisposable
    {
        public ScopedConnectionFactory<T>.ConnectionWrapper Connection => cn;
        public ScopedConnectionFactory<T>.TransactionWrapper Transaction => tx;

        public ScopedConnectionFactory<T>.CommandWrapper CreateCommand()
        {
            return cn.CreateCommand();
        }

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            return tx.CommitAsync(cancellationToken);
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            return tx.RollbackAsync(cancellationToken);
        }

        public Task SaveAsync(string savepointName, CancellationToken cancellationToken = default)
        {
            // NOTE: not supported by all providers
            return tx.SaveAsync(savepointName, cancellationToken);
        }

        public Task ReleaseAsync(string savepointName, CancellationToken cancellationToken = default)
        {
            // NOTE: not supported by all providers
            return tx.ReleaseAsync(savepointName, cancellationToken);
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
}