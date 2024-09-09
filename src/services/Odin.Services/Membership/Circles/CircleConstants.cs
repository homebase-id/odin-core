using System;
using System.Collections.Generic;
using Odin.Core;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Drives;

namespace Odin.Services.Membership.Circles;

public static class SystemCircleConstants
{
    public static readonly GuidId ConnectedIdentitiesSystemCircleId = GuidId.FromString("we_are_connected");
    public static readonly GuidId ConfirmedConnectionsCircleId = new Guid("ba4f80d2-eac4-4b31-afc1-a3dfe7043411");
    public static readonly GuidId AutoConnectionsCircleId = new Guid("9e22b429-52f7-4d25-80e1-1250b651d343");

    public static bool IsSystemCircle(Guid circleId)
    {
        return AllSystemCircles.Exists(c => c == circleId);
    }

    public static readonly List<GuidId> AllSystemCircles =
    [
        ConnectedIdentitiesSystemCircleId,
        ConfirmedConnectionsCircleId,
        AutoConnectionsCircleId
    ];

    public static readonly List<DriveGrantRequest> ConnectedIdentitiesSystemCircleInitialDrives =
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
    ];

    public static readonly List<DriveGrantRequest> ConfirmedConnectionsSystemCircleInitialDrives =
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
    ];

    public static readonly List<DriveGrantRequest> AutoConnectionsSystemCircleInitialDrives =
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
    ];
}