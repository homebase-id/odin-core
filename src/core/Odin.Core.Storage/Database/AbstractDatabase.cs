using System;
using System.Threading.Tasks;
using Autofac;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database;

public abstract class AbstractDatabase<T>(ILifetimeScope lifetimeScope) where T : IDbConnectionFactory
{
    public abstract Task<IConnectionWrapper> CreateScopedConnectionAsync();
    public abstract Task<IScopedTransaction> BeginStackedTransactionAsync();

    // SEB:NOTE this is temporary until we have a proper migration system
    public abstract Task CreateDatabaseAsync(bool dropExistingTables = false);

    protected TTable GetTable<TTable>(ref Lazy<TTable> lazyField) where TTable : class
    {
        return (lazyField ??= new Lazy<TTable>(lifetimeScope.Resolve<TTable>)).Value;
    }
}
