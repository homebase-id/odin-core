using System.Threading.Tasks;
using Autofac;
using Odin.Core.Storage.Database.System.Connection;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database.System;

public partial class SystemDatabase(ILifetimeScope lifetimeScope) : AbstractDatabase<ISystemDbConnectionFactory>(lifetimeScope)
{
    private readonly ILifetimeScope _lifetimeScope = lifetimeScope;

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

    public override async Task MigrateDatabaseAsync()
    {
        var migrator = _lifetimeScope.Resolve<SystemMigrator>();
        await migrator.MigrateAsync();
    }
}