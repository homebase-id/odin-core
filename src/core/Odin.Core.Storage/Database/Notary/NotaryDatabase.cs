using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Autofac;
using Odin.Core.Storage.Database.Notary.Connection;
using Odin.Core.Storage.Factory;
using Odin.Core.Storage.SQLite.NotaryDatabase;

namespace Odin.Core.Storage.Database.Notary;

public class NotaryDatabase(ILifetimeScope lifetimeScope) : AbstractDatabase<INotaryDbConnectionFactory>(lifetimeScope)
{
    //
    // Put all database tables alphabetically here.
    // Don't forget to add the table to the lazy properties as well.
    //
    public static readonly ImmutableList<Type> TableTypes =
    [
        typeof(TableNotaryChain)
    ];

    private readonly ILifetimeScope _lifetimeScope = lifetimeScope;

    //
    // Table convenience properties
    //

    // AppGrants
    private Lazy<TableNotaryChain> _notaryChain;
    public TableNotaryChain NotaryChain => LazyResolve(ref _notaryChain);


    //
    // Connection
    //
    public override async Task<IConnectionWrapper> CreateScopedConnectionAsync()
    {
        var factory = _lifetimeScope.Resolve<ScopedNotaryConnectionFactory>();
        var cn = await factory.CreateScopedConnectionAsync();
        return cn;
    }

    //
    // Transaction
    //
    public override async Task<IScopedTransaction> BeginStackedTransactionAsync()
    {
        var factory = _lifetimeScope.Resolve<ScopedNotaryTransactionFactory>();
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