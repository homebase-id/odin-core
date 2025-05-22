using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Autofac;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database.Identity;

public class IdentityDatabase(ILifetimeScope lifetimeScope) : AbstractDatabase<IIdentityDbConnectionFactory>(lifetimeScope)
{
    //
    // Put all database tables alphabetically here.
    // Don't forget to add the table to the lazy properties as well.
    //
    public static readonly ImmutableList<Type> TableTypes =
    [
        typeof(TableAppGrants),
        typeof(TableAppNotifications),
        typeof(TableCircle),
        typeof(TableCircleMember),
        typeof(TableConnections),
        typeof(TableDriveAclIndex),
        typeof(TableDriveLocalTagIndex),
        typeof(TableDriveMainIndex),
        typeof(TableDriveReactions),
        typeof(TableDriveTagIndex),
        typeof(TableDriveTransferHistory),
        typeof(TableFollowsMe),
        typeof(TableImFollowing),
        typeof(TableInbox),
        typeof(TableKeyThreeValue),
        typeof(TableKeyTwoValue),
        typeof(TableKeyUniqueThreeValue),
        typeof(TableKeyValue),
        typeof(TableOutbox),
        typeof(TableDriveDefinitions)
    ];

    //
    // Put all database table caches alphabetically here.
    // Don't forget to add the cache to the lazy properties as well.
    //
    public static readonly ImmutableList<Type> TableCacheTypes = [
        typeof(TableKeyValueCache),
    ];

    private readonly ILifetimeScope _lifetimeScope = lifetimeScope;

    //
    // Table convenience properties
    //
    private Lazy<TableAppGrants> _appGrants;
    public TableAppGrants AppGrants => LazyResolve(ref _appGrants);
    private Lazy<TableAppNotifications> _appNotifications;
    public TableAppNotifications AppNotifications => LazyResolve(ref _appNotifications);
    private Lazy<TableCircle> _circle;
    public TableCircle Circle => LazyResolve(ref _circle);
    private Lazy<TableCircleMember> _circleMember;
    public TableCircleMember CircleMember => LazyResolve(ref _circleMember);
    private Lazy<TableConnections> _connections;
    public TableConnections Connections => LazyResolve(ref _connections);
    private Lazy<TableDriveAclIndex> _driveAclIndex;

    public TableDriveAclIndex DriveAclIndex => LazyResolve(ref _driveAclIndex);
    private Lazy<TableDriveMainIndex> _driveMainIndex;
    public TableDriveMainIndex DriveMainIndex => LazyResolve(ref _driveMainIndex);
    private Lazy<TableDriveReactions> _driveReactions;

    private Lazy<TableDriveTagIndex> _driveTagIndex;
    public TableDriveTagIndex DriveTagIndex => LazyResolve(ref _driveTagIndex);

    private Lazy<TableDriveLocalTagIndex> _driveLocalTagIndex;
    public TableDriveLocalTagIndex DriveLocalTagIndex => LazyResolve(ref _driveLocalTagIndex);

    public TableDriveReactions DriveReactions => LazyResolve(ref _driveReactions);
    private Lazy<TableFollowsMe> _followsMe;
    public TableFollowsMe FollowsMe => LazyResolve(ref _followsMe);
    private Lazy<TableImFollowing> _imFollowing;
    public TableImFollowing ImFollowing => LazyResolve(ref _imFollowing);
    private Lazy<TableInbox> _inbox;
    public TableInbox Inbox => LazyResolve(ref _inbox);
    private Lazy<TableKeyThreeValue> _keyThreeValue;
    public TableKeyThreeValue KeyThreeValue => LazyResolve(ref _keyThreeValue);
    private Lazy<TableKeyTwoValue> _keyTwoValue;
    public TableKeyTwoValue KeyTwoValue => LazyResolve(ref _keyTwoValue);
    private Lazy<TableKeyUniqueThreeValue> _keyUniqueThreeValue;
    public TableKeyUniqueThreeValue KeyUniqueThreeValue => LazyResolve(ref _keyUniqueThreeValue);
    private Lazy<TableKeyValue> _keyValue;
    public TableKeyValue KeyValue => LazyResolve(ref _keyValue);
    private Lazy<TableOutbox> _outbox;
    public TableOutbox Outbox => LazyResolve(ref _outbox);

    private Lazy<TableDriveDefinitions> _driveDefinitions;
    public TableDriveDefinitions DriveDefinitions => LazyResolve(ref _driveDefinitions);

    //
    // Table cache convenience properties
    //
    private Lazy<TableKeyValueCache> _keyValueCache;
    public TableKeyValueCache KeyValueCache => LazyResolve(ref _keyValueCache);

    private Lazy<TableDriveTransferHistory> _tableDriveTransferHistory;
    public TableDriveTransferHistory TableDriveTransferHistory => LazyResolve(ref _tableDriveTransferHistory);

    //
    // Abstraction convenience properties (resolved, not injected)
    //
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