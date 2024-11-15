using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Identity;

public class IdentityDatabase(IServiceProvider services)
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
    public TableAppGrants AppGrants => services.GetRequiredService<TableAppGrants>();
    public TableAppNotifications AppNotifications => services.GetRequiredService<TableAppNotifications>();
    public TableCircle Circle => services.GetRequiredService<TableCircle>();
    public TableCircleMember CircleMember => services.GetRequiredService<TableCircleMember>();
    public TableConnections Connections => services.GetRequiredService<TableConnections>();
    public TableDriveAclIndex DriveAclIndex => services.GetRequiredService<TableDriveAclIndex>();
    public TableDriveMainIndex DriveMainIndex => services.GetRequiredService<TableDriveMainIndex>();
    public TableDriveReactions DriveReactions => services.GetRequiredService<TableDriveReactions>();
    public TableDriveTagIndex DriveTagIndex => services.GetRequiredService<TableDriveTagIndex>();
    public TableFollowsMe FollowsMe => services.GetRequiredService<TableFollowsMe>();
    public TableImFollowing ImFollowing => services.GetRequiredService<TableImFollowing>();
    public TableInbox Inbox => services.GetRequiredService<TableInbox>();
    public TableKeyThreeValue KeyThreeValue => services.GetRequiredService<TableKeyThreeValue>();
    public TableKeyTwoValue KeyTwoValue => services.GetRequiredService<TableKeyTwoValue>();
    public TableKeyUniqueThreeValue KeyUniqueThreeValue => services.GetRequiredService<TableKeyUniqueThreeValue>();
    public TableKeyValue KeyValue => services.GetRequiredService<TableKeyValue>();
    public TableOutbox Outbox => services.GetRequiredService<TableOutbox>();

    //
    // Migration
    //

    // SEB:NOTE this is temporary until we have a proper migration system
    public async Task CreateDatabaseAsync(bool dropExistingTables)
    {
        using var scope = services.CreateScope();
        var scopedConnectionFactory = scope.ServiceProvider.GetRequiredService<ScopedIdentityConnectionFactory>();
        await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var tx = await cn.BeginStackedTransactionAsync();
        foreach (var tableType in TableTypes)
        {
            var table = (ITableMigrator)scope.ServiceProvider.GetRequiredService(tableType);
            await table.EnsureTableExistsAsync(dropExistingTables);
        }
        await tx.CommitAsync();
    }

}