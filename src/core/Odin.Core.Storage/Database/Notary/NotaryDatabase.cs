using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Autofac;
using Odin.Core.Storage.Database.Notary.Connection;
using Odin.Core.Storage.Database.Notary.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database.Notary;

public partial class NotaryDatabase(ILifetimeScope lifetimeScope) : AbstractDatabase<INotaryDbConnectionFactory>(lifetimeScope)
{
    private readonly ILifetimeScope _lifetimeScope = lifetimeScope;

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

    public override async Task MigrateDatabaseAsync()
    {
        var migrator = _lifetimeScope.Resolve<NotaryMigrator>();
        await migrator.MigrateAsync();
    }

}