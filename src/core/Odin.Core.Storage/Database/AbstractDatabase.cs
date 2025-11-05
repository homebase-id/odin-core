using System;
using System.Data;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database;

#nullable enable

public abstract class AbstractDatabase<T>(ILifetimeScope lifetimeScope) where T : IDbConnectionFactory
{
    public abstract Task<IConnectionWrapper> CreateScopedConnectionAsync(
        [CallerFilePath] string? filePath = null,
        [CallerLineNumber] int lineNumber = 0);

    public abstract Task<IScopedTransaction> BeginStackedTransactionAsync(
        IsolationLevel isolationLevel = IsolationLevel.Unspecified,
        CancellationToken cancellationToken = default,
        [CallerFilePath] string? filePath = null,
        [CallerLineNumber] int lineNumber = 0);

    public abstract Task MigrateDatabaseAsync();

    protected TLazyType LazyResolve<TLazyType>(ref Lazy<TLazyType>? lazyField) where TLazyType : class
    {
        return (lazyField ??= new Lazy<TLazyType>(lifetimeScope.Resolve<TLazyType>)).Value;
    }
}
