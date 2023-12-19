using System;
using System.Collections.Generic;
using Odin.Core.Cryptography.Data;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;

namespace Odin.Core.Services.DataSubscription.ReceivingHost;

/// <summary>
/// Patches in the icr key during the EstablishConnection process so we can synchronize feed items
/// </summary>
public class FeedDriveSynchronizerSecurityContext : IDisposable
{
    private readonly SecurityGroupType _prevSecurityGroupType;
    private readonly OdinContextAccessor _odinContextAccessor;

    private const string GroupName = "patch_in_temp_icrkey";

    public FeedDriveSynchronizerSecurityContext(OdinContextAccessor odinContextAccessor, Guid feedDriveId, SensitiveByteArray tempKey, SymmetricKeyEncryptedAes encryptedIcrKey)
    {
        _odinContextAccessor = odinContextAccessor;
        var ctx = odinContextAccessor.GetCurrent();

        _prevSecurityGroupType = ctx.Caller.SecurityLevel;

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
            KeyStoreKeyEncryptedStorageKey = 
        };
        
        ctx.Caller.SecurityLevel = SecurityGroupType.Owner;
        ctx.PermissionsContext.PermissionGroups.Add(GroupName,
            new PermissionGroup(
                new PermissionSet(new[] { PermissionKeys.UseTransitRead, PermissionKeys.ReadConnections }), //to allow sending files
                new List<DriveGrant>() { feedDriveGrant }, tempKey, encryptedIcrKey));
    }

    public void Dispose()
    {
        _odinContextAccessor.GetCurrent().Caller.SecurityLevel = _prevSecurityGroupType;
        _odinContextAccessor.GetCurrent().PermissionsContext.PermissionGroups.Remove(GroupName);
    }
}