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
    public static IOdinContext UpgradeToByPassAclCheck(TargetDrive drive, DrivePermission drivePermission, IOdinContext odinContext)
    {
        var patchedContext = odinContext.Clone();
        patchedContext.Caller.SecurityLevel = SecurityGroupType.Owner;

        var driveGrants = new List<DriveGrant>()
        {
            new DriveGrant
            {
                DriveId = drive.Alias,
                PermissionedDrive = new PermissionedDrive
                {
                    Drive = drive,
                    Permission = drivePermission
                },
                KeyStoreKeyEncryptedStorageKey = null
            }
        };

        var pg = new PermissionGroup(new PermissionSet([PermissionKeys.SendPushNotifications]), driveGrants, null, null);
        if (patchedContext.PermissionsContext == null)
        {
            var dict = new Dictionary<string, PermissionGroup>() { { $"patched-drive-permission-{Guid.NewGuid()}", pg } };
            patchedContext.SetPermissionContext(new PermissionContext(dict, null));
        }
        else
        {
            patchedContext.PermissionsContext.PermissionGroups.TryAdd("patched-read-drive", pg);
        }

        return patchedContext;
    }

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

    public static IOdinContext PrepForSynchronizeChannelFiles(
        IOdinContext odinContext,
        Guid feedDriveId,
        SensitiveByteArray keyStoreKey,
        SymmetricKeyEncryptedAes encryptedFeedDriveStorageKey,
        SymmetricKeyEncryptedAes encryptedIcrKey)
    {
        var patchedContext = odinContext.Clone();


        // Upgrade access briefly to perform functions
        var feedDriveGrant = new DriveGrant()
        {
            DriveId = feedDriveId,
            PermissionedDrive = new()
            {
                Drive = WellKnownAppDrives.FeedDrive,
                Permission = DrivePermission.ReadWrite
            },
            KeyStoreKeyEncryptedStorageKey = encryptedFeedDriveStorageKey
        };


        patchedContext.Caller.SecurityLevel = SecurityGroupType.Owner;

        patchedContext.PermissionsContext.PermissionGroups.Add(
            "PrepForSynchronizeChannelFiles",
            new PermissionGroup(
                new PermissionSet(new[] { PermissionKeys.UseTransitRead, PermissionKeys.ManageFeed, PermissionKeys.ReadConnections }),
                new List<DriveGrant>() { feedDriveGrant }, keyStoreKey, encryptedIcrKey));

        return patchedContext;
    }


    public static IOdinContext PatchInSharedSecret(IOdinContext odinContext, SensitiveByteArray sharedSecret)
    {
        var patchedContext = odinContext.Clone();
        patchedContext.PermissionsContext.SetSharedSecretKey(sharedSecret);
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

    public static IOdinContext UpgradeToNonOwnerFeedDistributor(IOdinContext odinContext)
    {
        var patchedContext = odinContext.Clone();


        patchedContext.PermissionsContext.PermissionGroups.TryAdd(nameof(UpgradeToNonOwnerFeedDistributor),
            new PermissionGroup(
                new PermissionSet([
                    PermissionKeys.ReadConnections,
                    PermissionKeys.ReadMyFollowers,
                    // PermissionKeys.SendOnBehalfOfOwner,
                    PermissionKeys.ReadCircleMembership
                ]),
                new List<DriveGrant>(), null, null));

        return patchedContext;
    }

    /// <summary>
    /// Grants the caller what the channel sync needs, for the accept path where the caller cannot have it.
    /// </summary>
    /// <remarks>
    /// Accepting a connection request runs a channel sync, which asserts
    /// <see cref="PermissionKeys.ManageFeed"/> and writes the feed drive. An app accepting on the owner's
    /// behalf has neither, so the sync threw <c>OdinSecurityException</c> every time, the catch-all
    /// swallowed it, and the new contact's channels silently never arrived (#1784).
    /// <para>
    /// The same shape as <see cref="PrepForSynchronizeChannelFiles"/>, which the sender side of this flow
    /// already uses, with one difference: no feed-drive storage key, because the accept path has none to
    /// give. That is enough because a feed write is satisfied by permission rather than key material --
    /// <see cref="UpgradeToReadFollowersForDistribution"/> writes feed files with no drive grant at all,
    /// and it is how every inbound feed post arrives.
    /// </para>
    /// <para>
    /// Added to the caller's own groups rather than replacing them: an owner's groups carry the ICR key
    /// the channel query mints its peer token from, and dropping them breaks the owner's own sync.
    /// </para>
    /// </remarks>
    public static IOdinContext UpgradeToFeedWriterForConnectionAccept(IOdinContext odinContext)
    {
        var patchedContext = odinContext.Clone();
        var feedDrive = WellKnownAppDrives.FeedDrive;

        var feedDriveGrant = new DriveGrant
        {
            DriveId = feedDrive.Alias,
            PermissionedDrive = new PermissionedDrive
            {
                Drive = feedDrive,
                Permission = DrivePermission.ReadWrite
            },
            KeyStoreKeyEncryptedStorageKey = null
        };

        patchedContext.Caller.SecurityLevel = SecurityGroupType.Owner;

        patchedContext.PermissionsContext.PermissionGroups.TryAdd(
            nameof(UpgradeToFeedWriterForConnectionAccept),
            new PermissionGroup(
                new PermissionSet([PermissionKeys.ManageFeed, PermissionKeys.UseTransitRead, PermissionKeys.ReadConnections]),
                new List<DriveGrant> { feedDriveGrant }, null, null));

        return patchedContext;
    }

    public static IOdinContext UsePermissions(IOdinContext odinContext, params int[] permissionKeys)
    {
        var patchedContext = odinContext.Clone();

        patchedContext.PermissionsContext.PermissionGroups.TryAdd($"UsePermissions_{Guid.NewGuid().ToString()}",
            new PermissionGroup(
                new PermissionSet(permissionKeys),
                new List<DriveGrant>(), null, null));

        return patchedContext;
    }
}