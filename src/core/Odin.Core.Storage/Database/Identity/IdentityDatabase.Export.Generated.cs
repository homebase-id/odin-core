// THIS FILE IS AUTO GENERATED - DO NOT EDIT

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Core.Storage.Factory;
using Odin.Core.Storage.Database.Identity.Table;

#nullable disable

namespace Odin.Core.Storage.Database.Identity;

public partial class IdentityDatabase
{
    public static readonly ImmutableList<string> ExportableTables = [
        "Drives",
        "DriveMainIndex",
        "DriveTransferHistory",
        "DriveAclIndex",
        "DriveTagIndex",
        "DriveLocalTagIndex",
        "DriveReactions",
        "AppNotifications",
        "ClientRegistrations",
        "AppRegistrations",
        "Circle",
        "CircleMember",
        "Connections",
        "AppGrants",
        "ImFollowing",
        "FollowsMe",
        "Inbox",
        "Outbox",
        "KeyValue",
        "KeyTwoValue",
        "KeyThreeValue",
        "KeyUniqueThreeValue",
        "Nonce",
    ];

    public static readonly ImmutableDictionary<string, Type> ExportableRecordTypes =
        new Dictionary<string, Type>
        {
            ["Drives"] = typeof(DrivesRecord),
            ["DriveMainIndex"] = typeof(DriveMainIndexRecord),
            ["DriveTransferHistory"] = typeof(DriveTransferHistoryRecord),
            ["DriveAclIndex"] = typeof(DriveAclIndexRecord),
            ["DriveTagIndex"] = typeof(DriveTagIndexRecord),
            ["DriveLocalTagIndex"] = typeof(DriveLocalTagIndexRecord),
            ["DriveReactions"] = typeof(DriveReactionsRecord),
            ["AppNotifications"] = typeof(AppNotificationsRecord),
            ["ClientRegistrations"] = typeof(ClientRegistrationsRecord),
            ["AppRegistrations"] = typeof(AppRegistrationsRecord),
            ["Circle"] = typeof(CircleRecord),
            ["CircleMember"] = typeof(CircleMemberRecord),
            ["Connections"] = typeof(ConnectionsRecord),
            ["AppGrants"] = typeof(AppGrantsRecord),
            ["ImFollowing"] = typeof(ImFollowingRecord),
            ["FollowsMe"] = typeof(FollowsMeRecord),
            ["Inbox"] = typeof(InboxRecord),
            ["Outbox"] = typeof(OutboxRecord),
            ["KeyValue"] = typeof(KeyValueRecord),
            ["KeyTwoValue"] = typeof(KeyTwoValueRecord),
            ["KeyThreeValue"] = typeof(KeyThreeValueRecord),
            ["KeyUniqueThreeValue"] = typeof(KeyUniqueThreeValueRecord),
            ["Nonce"] = typeof(NonceRecord),
        }.ToImmutableDictionary();

    public async Task ExportAsync(Guid identityId, Func<string, object, Task> onRow)
    {
        await Drives.ExportRowsAsync(identityId, async r => await onRow("Drives", r));
        await DriveMainIndex.ExportRowsAsync(identityId, async r => await onRow("DriveMainIndex", r));
        await DriveTransferHistory.ExportRowsAsync(identityId, async r => await onRow("DriveTransferHistory", r));
        await DriveAclIndex.ExportRowsAsync(identityId, async r => await onRow("DriveAclIndex", r));
        await DriveTagIndex.ExportRowsAsync(identityId, async r => await onRow("DriveTagIndex", r));
        await DriveLocalTagIndex.ExportRowsAsync(identityId, async r => await onRow("DriveLocalTagIndex", r));
        await DriveReactions.ExportRowsAsync(identityId, async r => await onRow("DriveReactions", r));
        await AppNotifications.ExportRowsAsync(identityId, async r => await onRow("AppNotifications", r));
        await ClientRegistrations.ExportRowsAsync(identityId, async r => await onRow("ClientRegistrations", r));
        await AppRegistrations.ExportRowsAsync(identityId, async r => await onRow("AppRegistrations", r));
        await Circle.ExportRowsAsync(identityId, async r => await onRow("Circle", r));
        await CircleMember.ExportRowsAsync(identityId, async r => await onRow("CircleMember", r));
        await Connections.ExportRowsAsync(identityId, async r => await onRow("Connections", r));
        await AppGrants.ExportRowsAsync(identityId, async r => await onRow("AppGrants", r));
        await ImFollowing.ExportRowsAsync(identityId, async r => await onRow("ImFollowing", r));
        await FollowsMe.ExportRowsAsync(identityId, async r => await onRow("FollowsMe", r));
        await Inbox.ExportRowsAsync(identityId, async r => await onRow("Inbox", r));
        await Outbox.ExportRowsAsync(identityId, async r => await onRow("Outbox", r));
        await KeyValue.ExportRowsAsync(identityId, async r => await onRow("KeyValue", r));
        await KeyTwoValue.ExportRowsAsync(identityId, async r => await onRow("KeyTwoValue", r));
        await KeyThreeValue.ExportRowsAsync(identityId, async r => await onRow("KeyThreeValue", r));
        await KeyUniqueThreeValue.ExportRowsAsync(identityId, async r => await onRow("KeyUniqueThreeValue", r));
        await Nonce.ExportRowsAsync(identityId, async r => await onRow("Nonce", r));
    }

    public async Task<int> ImportRowAsync(string tableName, object record)
    {
        switch (tableName)
        {
            case "Drives":
                return await Drives.ImportRowAsync((DrivesRecord)record);
            case "DriveMainIndex":
                return await DriveMainIndex.ImportRowAsync((DriveMainIndexRecord)record);
            case "DriveTransferHistory":
                return await DriveTransferHistory.ImportRowAsync((DriveTransferHistoryRecord)record);
            case "DriveAclIndex":
                return await DriveAclIndex.ImportRowAsync((DriveAclIndexRecord)record);
            case "DriveTagIndex":
                return await DriveTagIndex.ImportRowAsync((DriveTagIndexRecord)record);
            case "DriveLocalTagIndex":
                return await DriveLocalTagIndex.ImportRowAsync((DriveLocalTagIndexRecord)record);
            case "DriveReactions":
                return await DriveReactions.ImportRowAsync((DriveReactionsRecord)record);
            case "AppNotifications":
                return await AppNotifications.ImportRowAsync((AppNotificationsRecord)record);
            case "ClientRegistrations":
                return await ClientRegistrations.ImportRowAsync((ClientRegistrationsRecord)record);
            case "AppRegistrations":
                return await AppRegistrations.ImportRowAsync((AppRegistrationsRecord)record);
            case "Circle":
                return await Circle.ImportRowAsync((CircleRecord)record);
            case "CircleMember":
                return await CircleMember.ImportRowAsync((CircleMemberRecord)record);
            case "Connections":
                return await Connections.ImportRowAsync((ConnectionsRecord)record);
            case "AppGrants":
                return await AppGrants.ImportRowAsync((AppGrantsRecord)record);
            case "ImFollowing":
                return await ImFollowing.ImportRowAsync((ImFollowingRecord)record);
            case "FollowsMe":
                return await FollowsMe.ImportRowAsync((FollowsMeRecord)record);
            case "Inbox":
                return await Inbox.ImportRowAsync((InboxRecord)record);
            case "Outbox":
                return await Outbox.ImportRowAsync((OutboxRecord)record);
            case "KeyValue":
                return await KeyValue.ImportRowAsync((KeyValueRecord)record);
            case "KeyTwoValue":
                return await KeyTwoValue.ImportRowAsync((KeyTwoValueRecord)record);
            case "KeyThreeValue":
                return await KeyThreeValue.ImportRowAsync((KeyThreeValueRecord)record);
            case "KeyUniqueThreeValue":
                return await KeyUniqueThreeValue.ImportRowAsync((KeyUniqueThreeValueRecord)record);
            case "Nonce":
                return await Nonce.ImportRowAsync((NonceRecord)record);
            default:
                throw new ArgumentException($"Unknown exportable table '{tableName}'", nameof(tableName));
        }
    }

    public async Task<Dictionary<string, long>> GetTableVersionsAsync()
    {
        await using var cn = await CreateScopedConnectionAsync();
        var result = new Dictionary<string, long>();
        foreach (var name in ExportableTables)
        {
            result[name] = await SqlHelper.GetTableVersionAsync(cn, name);
        }
        return result;
    }

    public async Task<long> CountRowsForIdentityAsync(Guid identityId)
    {
        await using var cn = await CreateScopedConnectionAsync();
        long total = 0;
        foreach (var name in ExportableTables)
        {
            await using var cmd = cn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {name} WHERE identityId = @identityId;";
            cmd.AddParameter("@identityId", DbType.Binary, identityId);
            total += (long)(await cmd.ExecuteScalarAsync() ?? 0L);
        }
        return total;
    }

}
