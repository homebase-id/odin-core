using System;
using System.Collections.Generic;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Drives;

namespace Odin.Services.Base;

public static class OdinContextUpgrades
{
    public static IOdinContext UpgradeToPeerTransferContext(IOdinContext odinContext)
    {
        var patchedContext = odinContext.Clone();

        //Note TryAdd because this might have already been added when multiple files are coming in
        patchedContext.PermissionsContext.PermissionGroups.TryAdd("send_notifications_for_peer_transfer",
            new PermissionGroup(
                new PermissionSet([PermissionKeys.SendPushNotifications]),
                new List<DriveGrant>(), null, null));

        return patchedContext;
    }

    public static IOdinContext PatchInIcrKey(
        IOdinContext odinContext,
        Guid feedDriveId,
        SensitiveByteArray keyStoreKey,
        SymmetricKeyEncryptedAes encryptedFeedDriveStorageKey,
        SymmetricKeyEncryptedAes encryptedIcrKey)
    {
        var patchedContext = odinContext.Clone();

        //
        // Upgrade access briefly to perform functions
        //
        var feedDriveGrant = new DriveGrant()
        {
            DriveId = feedDriveId,
            PermissionedDrive = new()
            {
                Drive = SystemDriveConstants.FeedDrive,
                Permission = DrivePermission.ReadWrite
            },
            KeyStoreKeyEncryptedStorageKey = encryptedFeedDriveStorageKey
        };

        patchedContext.Caller.SecurityLevel = SecurityGroupType.Owner;
        patchedContext.PermissionsContext.PermissionGroups.Add(
            "patch_in_temp_icr_key",
            new PermissionGroup(
                new PermissionSet(new[] { PermissionKeys.UseTransitRead, PermissionKeys.ManageFeed, PermissionKeys.ReadConnections }), //to allow sending files
                new List<DriveGrant>() { feedDriveGrant }, keyStoreKey, encryptedIcrKey));

        return patchedContext;
    }


    public static IOdinContext UpgradeToReadFollowersForDistribution(IOdinContext odinContext)
    {
        var patchedContext = odinContext.Clone();

        //
        // Upgrade access briefly to perform functions
        //
        patchedContext.Caller.SecurityLevel = SecurityGroupType.Owner;

        //Note TryAdd because this might have already been added when multiple files are coming in

        patchedContext.PermissionsContext.PermissionGroups.TryAdd("read_followers_only_for_distribution",
            new PermissionGroup(
                new PermissionSet([PermissionKeys.ReadMyFollowers]),
                new List<DriveGrant>(), null, null));

        return patchedContext;
    }
}