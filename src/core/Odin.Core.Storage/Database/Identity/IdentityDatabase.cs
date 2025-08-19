using System;
using System.Threading.Tasks;
using Autofac;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database.Identity;

public partial class IdentityDatabase(ILifetimeScope lifetimeScope) : AbstractDatabase<IIdentityDbConnectionFactory>(lifetimeScope)
{
    private readonly ILifetimeScope _lifetimeScope = lifetimeScope;

    // MainIndexMeta
    private Lazy<MainIndexMeta> _mainIndexMeta;
    public MainIndexMeta MainIndexMeta => LazyResolve(ref _mainIndexMeta);

    //
    // Connection
    //
    public override async Task<IConnectionWrapper> CreateScopedConnectionAsync()
    {
        var factory = _lifetimeScope.Resolve<ScopedIdentityConnectionFactory>();
        var cn = await factory.CreateScopedConnectionAsync();
        return cn;
    }

    //
    // Transaction
    //
    public override async Task<IScopedTransaction> BeginStackedTransactionAsync()
    {
        var factory = _lifetimeScope.Resolve<ScopedIdentityTransactionFactory>();
        var tx = await factory.BeginStackedTransactionAsync();
        return tx;
    }

    //
    // Migration
    //

    public override async Task MigrateDatabaseAsync()
    {
        var migrator = _lifetimeScope.Resolve<IdentityMigrator>();
        await migrator.MigrateAsync();
    }
}