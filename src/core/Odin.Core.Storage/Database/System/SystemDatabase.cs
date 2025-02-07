using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Autofac;
using Odin.Core.Storage.Database.System.Connection;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database.System;

public class SystemDatabase(ILifetimeScope lifetimeScope) : AbstractDatabase<ISystemDbConnectionFactory>(lifetimeScope)
{
    //
    // Put all database tables alphabetically here.
    // Don't forget to add the table to the lazy properties as well.
    //
    public static readonly ImmutableList<Type> TableTypes = [
        typeof(TableJobs)
    ];

    private readonly ILifetimeScope _lifetimeScope = lifetimeScope;

    //
    // Convenience properties
    //
    private Lazy<TableJobs> _jobs;
    public TableJobs Jobs => LazyResolve(ref _jobs);

    //
    // Connection
    //
    public override async Task<IConnectionWrapper> CreateScopedConnectionAsync()
    {
        var factory = _lifetimeScope.Resolve<ScopedSystemConnectionFactory>();
        var cn = await factory.CreateScopedConnectionAsync();
        return cn;
    }

    //
    // Transaction
    //
    public override async Task<IScopedTransaction> BeginStackedTransactionAsync()
    {
        var factory = _lifetimeScope.Resolve<ScopedSystemTransactionFactory>();
        var tx = await factory.BeginStackedTransactionAsync();
        return tx;
    }

    //
    // Migration
    //

    // SEB:NOTE this is temporary until we have a proper migration system
    public override async Task CreateDatabaseAsync(bool dropExistingTables = false)
    {
        await using var tx = await BeginStackedTransactionAsync();
        foreach (var tableType in TableTypes)
        {
            var table = (ITableMigrator)_lifetimeScope.Resolve(tableType);
            await table.EnsureTableExistsAsync(dropExistingTables);
        }
        tx.Commit();
    }

}