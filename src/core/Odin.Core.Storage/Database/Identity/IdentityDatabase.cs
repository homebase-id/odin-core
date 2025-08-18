using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Autofac;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Factory;
using Odin.Core.Storage.Database.Identity.Table;

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
        typeof(TableDrives),
        typeof(TableDriveTagIndex),
        typeof(TableDriveTransferHistory),
        typeof(TableFollowsMe),
        typeof(TableImFollowing),
        typeof(TableInbox),
        typeof(TableKeyThreeValue),
        typeof(TableKeyTwoValue),
        typeof(TableKeyUniqueThreeValue),
        typeof(TableKeyValue),
        typeof(TableNonce),
        typeof(TableOutbox),
    ];

    private readonly ILifetimeScope _lifetimeScope = lifetimeScope;

    //
    // Table convenience properties
    //

    // AppGrants
    private Lazy<TableAppGrants> _appGrants;
    public TableAppGrants AppGrants => LazyResolve(ref _appGrants);

    // AppNotifications
    private Lazy<TableAppNotifications> _appNotifications;
    public TableAppNotifications AppNotifications => LazyResolve(ref _appNotifications);

    // Circle
    private Lazy<TableCircle> _circle;
    public TableCircle Circle => LazyResolve(ref _circle);

    // CircleMember
    private Lazy<TableCircleMember> _circleMember;
    public TableCircleMember CircleMember => LazyResolve(ref _circleMember);

    // Connections
    private Lazy<TableConnections> _connections;
    public TableConnections Connections => LazyResolve(ref _connections);

    // DriveAclIndex
    private Lazy<TableDriveAclIndex> _driveAclIndex;
    public TableDriveAclIndex DriveAclIndex => LazyResolve(ref _driveAclIndex);

    // DriveLocalTagIndex
    private Lazy<TableDriveLocalTagIndex> _driveLocalTagIndex;
    public TableDriveLocalTagIndex DriveLocalTagIndex => LazyResolve(ref _driveLocalTagIndex);

    // DriveMainIndex
    private Lazy<TableDriveMainIndex> _driveMainIndex;
    public TableDriveMainIndex DriveMainIndex => LazyResolve(ref _driveMainIndex);

    // DriveReactions
    private Lazy<TableDriveReactions> _driveReactions;
    public TableDriveReactions DriveReactions => LazyResolve(ref _driveReactions);

    // Drives
    private Lazy<TableDrives> _driveDefinitions;
    public TableDrives Drives => LazyResolve(ref _driveDefinitions);

    // DriveTagIndex
    private Lazy<TableDriveTagIndex> _driveTagIndex;
    public TableDriveTagIndex DriveTagIndex => LazyResolve(ref _driveTagIndex);

    // DriveTransferHistory
    private Lazy<TableDriveTransferHistory> _driveTransferHistory;
    public TableDriveTransferHistory DriveTransferHistory => LazyResolve(ref _driveTransferHistory);

    // FollowsMe
    private Lazy<TableFollowsMe> _followsMe;
    public TableFollowsMe FollowsMe => LazyResolve(ref _followsMe);

    // ImFollowing
    private Lazy<TableImFollowing> _imFollowing;
    public TableImFollowing ImFollowing => LazyResolve(ref _imFollowing);

    // Inbox
    private Lazy<TableInbox> _inbox;
    public TableInbox Inbox => LazyResolve(ref _inbox);

    // KeyThreeValue
    private Lazy<TableKeyThreeValue> _keyThreeValue;
    public TableKeyThreeValue KeyThreeValue => LazyResolve(ref _keyThreeValue);

    // KeyTwoValue
    private Lazy<TableKeyTwoValue> _keyTwoValue;
    public TableKeyTwoValue KeyTwoValue => LazyResolve(ref _keyTwoValue);

    // KeyUniqueThreeValue
    private Lazy<TableKeyUniqueThreeValue> _keyUniqueThreeValue;
    public TableKeyUniqueThreeValue KeyUniqueThreeValue => LazyResolve(ref _keyUniqueThreeValue);

    // KeyValue
    private Lazy<TableKeyValue> _keyValue;
    public TableKeyValue KeyValue => LazyResolve(ref _keyValue);

    // Nonce
    private Lazy<TableNonce> _nonce;
    public TableNonce Nonce => LazyResolve(ref _nonce);

    // Outbox
    private Lazy<TableOutbox> _outbox;
    public TableOutbox Outbox => LazyResolve(ref _outbox);

    //
    // Abstraction convenience properties
    //

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