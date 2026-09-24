using System;
using System.Collections.Generic;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Identity;
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

    /// <param name="keyStoreKey">
    /// Null when the caller has none to give. The sender side of a connection stashed temp-encrypted
    /// copies of these three while the owner was present; the accept side has no equivalent, and runs
    /// with all three null.
    /// </param>
    /// <remarks>
    /// Without <paramref name="encryptedFeedDriveStorageKey"/> the grant permits writing the feed drive
    /// but cannot seal anything to it, so only unencrypted posts can be written --
    /// <c>CreateServerHeaderInternal</c> needs the storage key to encrypt a key header, and throws
    /// without it. That is the same split inbound distribution makes:
    /// <c>FeedDistributionPerimeterService</c> writes an unencrypted post directly and routes an
    /// encrypted one to the inbox for a keyed pass to finish.
    /// <para>
    /// <see cref="SecurityGroupType.Owner"/> is load-bearing for the sender path, which runs under a
    /// peer/transit context. On the accept path it is a no-op -- app clients are already Owner.
    /// </para>
    /// </remarks>
    public static IOdinContext PrepForSynchronizeChannelFiles(
        IOdinContext odinContext,
        SensitiveByteArray keyStoreKey = null,
        SymmetricKeyEncryptedAes encryptedFeedDriveStorageKey = null,
        SymmetricKeyEncryptedAes encryptedIcrKey = null)
    {
        var patchedContext = odinContext.Clone();


        // Upgrade access briefly to perform functions
        var feedDriveGrant = new DriveGrant()
        {
            DriveId = WellKnownAppDrives.FeedDrive.Alias,
            PermissionedDrive = new()
            {
                Drive = WellKnownAppDrives.FeedDrive,
                Permission = DrivePermission.ReadWrite
            },
            KeyStoreKeyEncryptedStorageKey = encryptedFeedDriveStorageKey
        };


        patchedContext.Caller.SecurityLevel = SecurityGroupType.Owner;

        patchedContext.PermissionsContext.PermissionGroups.TryAdd(
            "PrepForSynchronizeChannelFiles",
            new PermissionGroup(
                new PermissionSet(new[] { PermissionKeys.UseTransitRead, PermissionKeys.ManageFeed, PermissionKeys.ReadConnections }),
                new List<DriveGrant>() { feedDriveGrant }, keyStoreKey, encryptedIcrKey));

        return patchedContext;
    }


    /// <summary>
    /// A context for fetching channel files when there is no caller to upgrade -- the background job that
    /// runs after a connection is accepted.
    /// </summary>
    /// <remarks>
    /// Same grant as <see cref="PrepForSynchronizeChannelFiles"/> and the same limit: no feed-drive
    /// storage key, so unencrypted posts are written and encrypted ones are skipped. The peer credential
    /// does not come from here -- the job carries it, because minting one needs the ICR key and a job has
    /// no master key to unlock it with.
    /// </remarks>
    public static IOdinContext BuildFeedSyncContext(OdinId tenant)
    {
        var feedDriveGrant = new DriveGrant
        {
            DriveId = WellKnownAppDrives.FeedDrive.Alias,
            PermissionedDrive = new PermissionedDrive
            {
                Drive = WellKnownAppDrives.FeedDrive,
                Permission = DrivePermission.ReadWrite
            },
            KeyStoreKeyEncryptedStorageKey = null
        };

        var odinContext = new OdinContext
        {
            Tenant = tenant,
            AuthTokenCreated = null,
            Caller = new CallerContext(
                odinId: tenant,
                masterKey: null,
                securityLevel: SecurityGroupType.Owner,
                tokenType: ClientTokenType.Other)
        };

        var groups = new Dictionary<string, PermissionGroup>
        {
            {
                nameof(BuildFeedSyncContext),
                new PermissionGroup(
                    new PermissionSet([PermissionKeys.ManageFeed, PermissionKeys.UseTransitRead, PermissionKeys.ReadConnections]),
                    new List<DriveGrant> { feedDriveGrant }, null, null)
            }
        };

        odinContext.SetPermissionContext(new PermissionContext(groups, sharedSecretKey: null));
        return odinContext;
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