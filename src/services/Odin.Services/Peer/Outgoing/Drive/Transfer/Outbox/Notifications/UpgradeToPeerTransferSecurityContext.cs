using System;
using System.Collections.Generic;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Notifications;

public class UpgradeToPeerTransferSecurityContext : IDisposable
{
    private readonly IOdinContext _context;

    private const string GroupName = "send_notifications_for_peer_transfer";

    public UpgradeToPeerTransferSecurityContext(IOdinContext context)
    {
        _context = context;
       
        //
        // Upgrade access briefly to perform functions
        //

        //Note TryAdd because this might have already been added when multiple files are coming in
        context.PermissionsContext.PermissionGroups.TryAdd(GroupName,
            new PermissionGroup(
                new PermissionSet(new[] { PermissionKeys.SendPushNotifications }),
                new List<DriveGrant>() { }, null, null));
    }

    public void Dispose()
    {
        _context.PermissionsContext.PermissionGroups.Remove(GroupName);
    }
}