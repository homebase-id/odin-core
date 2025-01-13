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
        typeof(TableDriveMainIndex),
        typeof(TableDriveReactions),
        typeof(TableDriveTagIndex),
        typeof(TableFollowsMe),
        typeof(TableImFollowing),
        typeof(TableInbox),
        typeof(TableKeyThreeValue),
        typeof(TableKeyTwoValue),
        typeof(TableKeyUniqueThreeValue),
        typeof(TableKeyValue),
        typeof(TableOutbox),
        typeof(TableDriveTransferHistory)
    ];

    private readonly ILifetimeScope _lifetimeScope = lifetimeScope;

    //
    // Table convenience properties
    //
    private Lazy<TableAppGrants> _appGrants;
    public TableAppGrants AppGrants => GetTable(ref _appGrants);
    private Lazy<TableAppNotifications> _appNotifications;
    public TableAppNotifications AppNotifications => GetTable(ref _appNotifications);
    private Lazy<TableCircle> _circle;
    public TableCircle Circle => GetTable(ref _circle);
    private Lazy<TableCircleMember> _circleMember;
    public TableCircleMember CircleMember => GetTable(ref _circleMember);
    private Lazy<TableConnections> _connections;
    public TableConnections Connections => GetTable(ref _connections);
    private Lazy<TableDriveAclIndex> _driveAclIndex;
    public TableDriveAclIndex DriveAclIndex => GetTable(ref _driveAclIndex);
    private Lazy<TableDriveMainIndex> _driveMainIndex;
    public TableDriveMainIndex DriveMainIndex => GetTable(ref _driveMainIndex);
    private Lazy<TableDriveReactions> _driveReactions;
    public TableDriveReactions DriveReactions => GetTable(ref _driveReactions);
    private Lazy<TableDriveTagIndex> _driveTagIndex;
    public TableDriveTagIndex DriveTagIndex => GetTable(ref _driveTagIndex);
    private Lazy<TableFollowsMe> _followsMe;
    public TableFollowsMe FollowsMe => GetTable(ref _followsMe);
    private Lazy<TableImFollowing> _imFollowing;
    public TableImFollowing ImFollowing => GetTable(ref _imFollowing);
    private Lazy<TableInbox> _inbox;
    public TableInbox Inbox => GetTable(ref _inbox);
    private Lazy<TableKeyThreeValue> _keyThreeValue;
    public TableKeyThreeValue KeyThreeValue => GetTable(ref _keyThreeValue);
    private Lazy<TableKeyTwoValue> _keyTwoValue;
    public TableKeyTwoValue KeyTwoValue => GetTable(ref _keyTwoValue);
    private Lazy<TableKeyUniqueThreeValue> _keyUniqueThreeValue;
    public TableKeyUniqueThreeValue KeyUniqueThreeValue => GetTable(ref _keyUniqueThreeValue);
    private Lazy<TableKeyValue> _keyValue;
    public TableKeyValue KeyValue => GetTable(ref _keyValue);
    private Lazy<TableOutbox> _outbox;
    public TableOutbox Outbox => GetTable(ref _outbox);

    private Lazy<TableDriveTransferHistory> _tableDriveTransferHistory;
    public TableDriveTransferHistory TableDriveTransferHistory => GetTable(ref _tableDriveTransferHistory);

    //
    // Abstraction convenience properties (resolved, not injected)
    //
    private Lazy<MainIndexMeta> _mainIndexMeta;
    public MainIndexMeta MainIndexMeta => GetTable(ref _mainIndexMeta);

    private Lazy<TransferHistoryDataOperations> _transferHistoryDataOperations;
    public TransferHistoryDataOperations TransferHistoryDataOperations => GetTable(ref _transferHistoryDataOperations);

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