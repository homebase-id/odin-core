using System;
using System.Threading.Tasks;
using Autofac;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database;

public abstract class AbstractDatabase<T>(ILifetimeScope lifetimeScope) where T : IDbConnectionFactory
{
    public abstract Task<IConnectionWrapper> CreateScopedConnectionAsync();
    public abstract Task<IScopedTransaction> BeginStackedTransactionAsync();
    public abstract Task MigrateDatabaseAsync();

    protected TLazyType LazyResolve<TLazyType>(ref Lazy<TLazyType> lazyField) where TLazyType : class
    {
        return (lazyField ??= new Lazy<TLazyType>(lifetimeScope.Resolve<TLazyType>)).Value;
    }
}
