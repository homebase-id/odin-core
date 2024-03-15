using System;
using System.Collections.Generic;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;

namespace Odin.Services.Peer.Incoming.Drive.Reactions;

/// <summary>
/// Patches in the icr key during the EstablishConnection process so we can synchronize feed items
/// </summary>
public class PeerReactionSecurityContext : IDisposable
{
    private readonly SecurityGroupType _prevSecurityGroupType;
    private readonly OdinContextAccessor _odinContextAccessor;

    private const string GroupName = "patch_in_owner_access_to_get_file_by_global_transit_id";

    /// <summary>
    /// Temporarily upgrades access so we can complete the reaction operation
    /// </summary>
    public PeerReactionSecurityContext(OdinContextAccessor odinContextAccessor, Guid driveId, TargetDrive targetDrive)
    {
        _odinContextAccessor = odinContextAccessor;
        var ctx = odinContextAccessor.GetCurrent();

        _prevSecurityGroupType = ctx.Caller.SecurityLevel;

        var driveGrant = new DriveGrant()
        {
            DriveId = driveId,
            PermissionedDrive = new()
            {
                Drive = targetDrive,
                Permission = DrivePermission.Read
            },
            KeyStoreKeyEncryptedStorageKey = null
        };

        ctx.Caller.SecurityLevel = SecurityGroupType.Owner;
        ctx.PermissionsContext.PermissionGroups.Add(GroupName,
            new PermissionGroup(
                new PermissionSet(),
                new List<DriveGrant>() { driveGrant }, null, null));
    }

    public void Dispose()
    {
        _odinContextAccessor.GetCurrent().Caller.SecurityLevel = _prevSecurityGroupType;
        _odinContextAccessor.GetCurrent().PermissionsContext.PermissionGroups.Remove(GroupName);
    }
}