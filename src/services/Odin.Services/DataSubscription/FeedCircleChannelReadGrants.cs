using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Odin.Core;
using Microsoft.Extensions.Logging;
using Odin.Services.Apps.Builtin;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.Mediator;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;

namespace Odin.Services.DataSubscription;

/// <summary>
/// Keeps the Feed circle holding Read, with the storage key, on every channel drive the Feed app owns (#1956).
/// </summary>
/// <remarks>
/// <para>
/// An encrypted post goes only to followers who could decrypt it: keyed Read on the post's drive
/// (<see cref="CircleNetworkService.CanDecryptDriveAsync"/>).  Every channel has a drive of its own.  Until #1809 the
/// Confirmed Connections circle carried keyed Read on every anonymous drive, and nothing replaced it for channels but
/// Public Posts, so a custom or collaborative channel's encrypted posts reached nobody.
/// </para>
/// <para>
/// The Feed circle already holds it on Public Posts (<see cref="BuiltinCircles.FeedCircle"/>); this gives it the same on
/// every other channel.  Each post's ACL still decides who gets it and who may read it: the grant only lets a member
/// decrypt a post the ACL already lets them see.  A new channel drive is granted when it is created, from the owner
/// console, which holds the master key re-minting the members' grants needs; an existing one by the v21 → v22 upgrade.
/// </para>
/// <para>
/// A channel drive is one of channel type owned by the Feed app, the only drives its circle may grant
/// (<see cref="BuiltinCircles"/>).  A zero-knowledge one is left out: the server sends nothing from it and has no key
/// to give.
/// </para>
/// </remarks>
public class FeedCircleChannelReadGrants(
    ILogger<FeedCircleChannelReadGrants> logger,
    CircleDefinitionService circleDefinitionService,
    CircleNetworkService circleNetworkService,
    IDriveManager driveManager) : INotificationHandler<DriveDefinitionAddedNotification>
{
    public static bool IsFeedChannelDrive(StorageDrive drive) =>
        drive.TargetDriveInfo.Type == SystemDriveConstants.ChannelDriveType &&
        drive.AppId == BuiltinCircles.FeedCircle.AppId &&
        !drive.IsZeroKnowledge;

    public async Task Handle(DriveDefinitionAddedNotification notification, CancellationToken cancellationToken)
    {
        if (notification.IsNewDrive && IsFeedChannelDrive(notification.Drive) &&
            notification.OdinContext.Caller.HasMasterKey)
        {
            await GrantReadAsync([notification.Drive.TargetDriveInfo], notification.OdinContext);
        }
    }

    /// <summary>Grants the Feed circle Read on every channel drive it lacks it on; the v21 → v22 upgrade.</summary>
    public async Task GrantReadOnAllChannelDrivesAsync(IOdinContext odinContext, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await GrantReadAsync(await ChannelDrivesAsync(odinContext), odinContext);
    }

    /// <summary>The channel drives the Feed circle lacks Read on; none when the identity has no Feed circle.</summary>
    public async Task<List<TargetDrive>> ChannelDrivesMissingReadAsync(IOdinContext odinContext)
    {
        var def = await circleDefinitionService.GetCircleAsync(BuiltinCircles.FeedCircle.Id);
        return def == null ? [] : Missing(def, await ChannelDrivesAsync(odinContext));
    }

    private async Task<List<TargetDrive>> ChannelDrivesAsync(IOdinContext odinContext)
    {
        var drives = await driveManager.GetDrivesAsync(SystemDriveConstants.ChannelDriveType, PageOptions.All, odinContext);
        return drives.Results.Where(IsFeedChannelDrive).Select(d => d.TargetDriveInfo).ToList();
    }

    private async Task GrantReadAsync(List<TargetDrive> drives, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        // A drive made before the Feed circle exists, as at provisioning, which then makes the circle with its grants.
        var def = await circleDefinitionService.GetCircleAsync(BuiltinCircles.FeedCircle.Id);
        if (def == null)
        {
            logger.LogDebug("Feed circle not found; no channel drive Read to grant");
            return;
        }

        var missing = Missing(def, drives);
        if (missing.Count == 0)
        {
            return;
        }

        logger.LogInformation("Granting the Feed circle Read on {count} channel drive(s)", missing.Count);

        // Read joins a grant the circle has on the drive already, else comes as one of its own.
        var grants = def.DriveGrants?.ToList() ?? [];
        foreach (var drive in missing)
        {
            var existing = grants.FirstOrDefault(g => g.PermissionedDrive.Drive == drive);
            if (existing != null)
            {
                existing.PermissionedDrive.Permission |= DrivePermission.Read;
                continue;
            }

            grants.Add(new DriveGrantRequest
            {
                PermissionedDrive = new PermissionedDrive { Drive = drive, Permission = DrivePermission.Read }
            });
        }

        def.DriveGrants = grants;

        // Persists the definition and re-mints every member's grant, storage keys included.
        await circleNetworkService.UpdateCircleDefinitionAsync(def, odinContext);
    }

    private static List<TargetDrive> Missing(CircleDefinition def, List<TargetDrive> drives) =>
        drives.Where(drive => !(def.DriveGrants ?? []).Any(g =>
            g.PermissionedDrive.Drive == drive && g.PermissionedDrive.Permission.HasFlag(DrivePermission.Read))).ToList();
}
