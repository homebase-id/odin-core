using System;
using System.Threading.Tasks;
using Autofac;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database.Identity;

public partial class IdentityDatabase(ILifetimeScope lifetimeScope) : AbstractDatabase<IIdentityDbConnectionFactory>(lifetimeScope)
{
    private readonly ILifetimeScope _lifetimeScope = lifetimeScope;

    //
    // Abstractions
    //
    private Lazy<MainIndexMeta> _mainIndexMeta;
    public MainIndexMeta MainIndexMeta => LazyResolve(ref _mainIndexMeta);

    //
    // Caching
    //
    private Lazy<TableAppGrantsCached> _appGrantsCached;
    public TableAppGrantsCached AppGrantsCached => LazyResolve(ref _appGrantsCached);

    private Lazy<TableAppNotificationsCached> _appNotificationsCached;
    public TableAppNotificationsCached AppNotificationsCached => LazyResolve(ref _appNotificationsCached);

    private Lazy<TableCircleCached> _circleCached;
    public TableCircleCached CircleCached => LazyResolve(ref _circleCached);

    private Lazy<TableCircleMemberCached> _circleMemberCached;
    public TableCircleMemberCached CircleMemberCached => LazyResolve(ref _circleMemberCached);

    private Lazy<TableConnectionsCached> _connectionsCached;
    public TableConnectionsCached ConnectionsCached => LazyResolve(ref _connectionsCached);

    private Lazy<TableDriveMainIndexCached> _driveMainIndexCached;
    public TableDriveMainIndexCached DriveMainIndexCached => LazyResolve(ref _driveMainIndexCached);

    private Lazy<TableFollowsMeCached> _followsMeCached;
    public TableFollowsMeCached FollowsMeCached => LazyResolve(ref _followsMeCached);

    private Lazy<TableImFollowingCached> _imFollowingCached;
    public TableImFollowingCached ImFollowingCached => LazyResolve(ref _imFollowingCached);

    private Lazy<TableKeyThreeValueCached> _keyThreeValueCached;
    public TableKeyThreeValueCached KeyThreeValueCached => LazyResolve(ref _keyThreeValueCached);

    private Lazy<TableKeyTwoValueCached> _keyTwoValueCached;
    public TableKeyTwoValueCached KeyTwoValueCached => LazyResolve(ref _keyTwoValueCached);

    private Lazy<TableKeyValueCached> _keyValueCached;
    public TableKeyValueCached KeyValueCached => LazyResolve(ref _keyValueCached);

    private Lazy<MainIndexMetaCached> _mainIndexMetaCached;
    public MainIndexMetaCached MainIndexMetaCached => LazyResolve(ref _mainIndexMetaCached);

    private Lazy<TableDrivesCached> _tableDrivesCached;
    public TableDrivesCached DrivesCached => LazyResolve(ref _tableDrivesCached);

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