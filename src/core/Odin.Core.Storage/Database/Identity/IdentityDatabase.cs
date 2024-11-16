using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Autofac;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Identity;

public class IdentityDatabase(ILifetimeScope lifetimeScope)
{
    //
    // Put all database tables alphabetically here:
    //
    public static readonly ImmutableList<Type> TableTypes = [
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
        typeof(TableOutbox)
    ];

    //
    // Convenience properties (resolved, not injected)
    // SEB:TODO make these Lazy<T>
    public TableAppGrants AppGrants => lifetimeScope.Resolve<TableAppGrants>();
    public TableAppNotifications AppNotifications => lifetimeScope.Resolve<TableAppNotifications>();
    public TableCircle Circle => lifetimeScope.Resolve<TableCircle>();
    public TableCircleMember CircleMember => lifetimeScope.Resolve<TableCircleMember>();
    public TableConnections Connections => lifetimeScope.Resolve<TableConnections>();
    public TableDriveAclIndex DriveAclIndex => lifetimeScope.Resolve<TableDriveAclIndex>();
    public TableDriveMainIndex DriveMainIndex => lifetimeScope.Resolve<TableDriveMainIndex>();
    public TableDriveReactions DriveReactions => lifetimeScope.Resolve<TableDriveReactions>();
    public TableDriveTagIndex DriveTagIndex => lifetimeScope.Resolve<TableDriveTagIndex>();
    public TableFollowsMe FollowsMe => lifetimeScope.Resolve<TableFollowsMe>();
    public TableImFollowing ImFollowing => lifetimeScope.Resolve<TableImFollowing>();
    public TableInbox Inbox => lifetimeScope.Resolve<TableInbox>();
    public TableKeyThreeValue KeyThreeValue => lifetimeScope.Resolve<TableKeyThreeValue>();
    public TableKeyTwoValue KeyTwoValue => lifetimeScope.Resolve<TableKeyTwoValue>();
    public TableKeyUniqueThreeValue KeyUniqueThreeValue => lifetimeScope.Resolve<TableKeyUniqueThreeValue>();
    public TableKeyValue KeyValue => lifetimeScope.Resolve<TableKeyValue>();
    public TableOutbox Outbox => lifetimeScope.Resolve<TableOutbox>();

    //
    // Migration
    //

    // SEB:NOTE this is temporary until we have a proper migration system
    public async Task CreateDatabaseAsync(bool dropExistingTables)
    {
        await using var scope = lifetimeScope.BeginLifetimeScope();
        var scopedConnectionFactory = scope.Resolve<ScopedIdentityConnectionFactory>();
        await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var tx = await cn.BeginStackedTransactionAsync();
        foreach (var tableType in TableTypes)
        {
            var table = (ITableMigrator)scope.Resolve(tableType);
            await table.EnsureTableExistsAsync(dropExistingTables);
        }
        await tx.CommitAsync();
    }

}