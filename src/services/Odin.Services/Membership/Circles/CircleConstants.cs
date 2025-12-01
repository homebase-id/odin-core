using System;
using System.Collections.Generic;
using Odin.Core;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Drives;

namespace Odin.Services.Membership.Circles;

public static class SystemCircleConstants
{
    public static readonly GuidId ConfirmedConnectionsCircleId = Guid.Parse("bb2683fa402aff866e771a6495765a15");
    public static readonly GuidId AutoConnectionsCircleId = Guid.Parse("9e22b42952f74d2580e11250b651d343");

    public static bool IsSystemCircle(Guid circleId)
    {
        return AllSystemCircles.Exists(c => c == circleId);
    }

    public static readonly List<GuidId> AllSystemCircles =
    [
        ConfirmedConnectionsCircleId,
        AutoConnectionsCircleId
    ];

    public static readonly CircleDefinition ConfirmedConnectionsDefinition = new()
    {
        Id = ConfirmedConnectionsCircleId.Value,
        Name = "Confirmed Connected Identities",
        Description =
            "Contains identities which you have confirmed as a connection, either by approving the connection yourself or upgrading an introduced connection",
        DriveGrants =
        [
            new DriveGrantRequest()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = SystemDriveConstants.ChatDrive,
                    Permission = DrivePermission.Write | DrivePermission.React
                }
            },

            new DriveGrantRequest()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = SystemDriveConstants.MailDrive,
                    Permission = DrivePermission.Write | DrivePermission.React
                }
            },

            new DriveGrantRequest()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = SystemDriveConstants.FeedDrive,
                    Permission = DrivePermission.Write | DrivePermission.React
                }
            },

            new DriveGrantRequest()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = SystemDriveConstants.ShardRecoveryDrive,
                    Permission = DrivePermission.Write
                }
            },
        ],
        Permissions = new PermissionSet()
        {
            Keys = [PermissionKeys.AllowIntroductions]
        }
    };

    public static readonly CircleDefinition AutoConnectionsSystemCircleDefinition = new()
    {
        Id = SystemCircleConstants.AutoConnectionsCircleId.Value,
        Name = "Auto-connected Identities",
        Description = "Contains all identities which were automatically connected (due to an introduction from another-connected identity)",
        DriveGrants =
        [
            new DriveGrantRequest()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = SystemDriveConstants.ChatDrive,
                    Permission = DrivePermission.Write | DrivePermission.React
                }
            },

            new DriveGrantRequest()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = SystemDriveConstants.MailDrive,
                    Permission = DrivePermission.Write | DrivePermission.React
                }
            },

            new DriveGrantRequest()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = SystemDriveConstants.FeedDrive,
                    Permission = DrivePermission.Write | DrivePermission.React
                }
            }
        ],
        Permissions = new PermissionSet()
        {
            Keys = []
        }
    };
}