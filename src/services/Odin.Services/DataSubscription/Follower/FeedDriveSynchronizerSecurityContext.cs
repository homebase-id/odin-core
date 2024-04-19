using System;
using System.Collections.Generic;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;

namespace Odin.Services.DataSubscription.Follower;

/// <summary>
/// Patches in the icr key during the EstablishConnection process so we can synchronize feed items
/// </summary>
public class FeedDriveSynchronizerSecurityContext : IDisposable
{
    private readonly SecurityGroupType _prevSecurityGroupType;

    private readonly IOdinContext _odinContext;
    private const string GroupName = "patch_in_temp_icrkey";
    
    public FeedDriveSynchronizerSecurityContext(ref IOdinContext context, Guid feedDriveId, SensitiveByteArray keyStoreKey,
        SymmetricKeyEncryptedAes encryptedFeedDriveStorageKey,
        SymmetricKeyEncryptedAes encryptedIcrKey)
    {
        _odinContext = context;
        _prevSecurityGroupType = context.Caller.SecurityLevel;

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

        _odinContext.Caller.SecurityLevel = SecurityGroupType.Owner;
        _odinContext.PermissionsContext.PermissionGroups.Add(GroupName,
            new PermissionGroup(
                new PermissionSet(new[] { PermissionKeys.UseTransitRead, PermissionKeys.ManageFeed, PermissionKeys.ReadConnections }), //to allow sending files
                new List<DriveGrant>() { feedDriveGrant }, keyStoreKey, encryptedIcrKey));
    }

    public void Dispose()
    {
        _odinContext.Caller.SecurityLevel = _prevSecurityGroupType;
        _odinContext.PermissionsContext.PermissionGroups.Remove(GroupName);
    }
}