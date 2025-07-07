using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Autofac;
using Odin.Core.Storage.Database.KeyChain.Connection;
using Odin.Core.Storage.Factory;
using Odin.Core.Storage.SQLite.KeyChainDatabase;

namespace Odin.Core.Storage.Database.KeyChain;

public class KeyChainDatabase(ILifetimeScope lifetimeScope) : AbstractDatabase<IKeyChainDbConnectionFactory>(lifetimeScope)
{
    //
    // Put all database tables alphabetically here.
    // Don't forget to add the table to the lazy properties as well.
    //
    public static readonly ImmutableList<Type> TableTypes =
    [
        typeof(TableKeyChain)
    ];

    private readonly ILifetimeScope _lifetimeScope = lifetimeScope;

    //
    // Table convenience properties
    //

    // KeyChain
    private Lazy<TableKeyChain> _keyChain;
    public TableKeyChain KeyChain => LazyResolve(ref _keyChain);


    //
    // Connection
    //
    public override async Task<IConnectionWrapper> CreateScopedConnectionAsync()
    {
        var factory = _lifetimeScope.Resolve<ScopedKeyChainConnectionFactory>();
        var cn = await factory.CreateScopedConnectionAsync();
        return cn;
    }

    //
    // Transaction
    //
    public override async Task<IScopedTransaction> BeginStackedTransactionAsync()
    {
        var factory = _lifetimeScope.Resolve<ScopedKeyChainTransactionFactory>();
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