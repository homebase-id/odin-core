using System;
using System.Collections.Generic;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;

namespace Odin.Services.Peer.Incoming.Drive.Transfer;

public class UpgradeToPeerTransferSecurityContext : IDisposable
{
    private readonly OdinContextAccessor _odinContextAccessor;

    private const string GroupName = "send_notifications_for_peer_transfer";

    public UpgradeToPeerTransferSecurityContext(OdinContextAccessor odinContextAccessor)
    {
        _odinContextAccessor = odinContextAccessor;
        var ctx = odinContextAccessor.GetCurrent();

        //
        // Upgrade access briefly to perform functions
        //

        //Note TryAdd because this might have already been added when multiple files are coming in
        ctx.PermissionsContext.PermissionGroups.TryAdd(GroupName,
            new PermissionGroup(
                new PermissionSet(new[] { PermissionKeys.SendPushNotifications }),
                new List<DriveGrant>() { }, null, null));
    }

    public void Dispose()
    {
        _odinContextAccessor.GetCurrent().PermissionsContext.PermissionGroups.Remove(GroupName);
    }
}