using System.Collections.Generic;
using Odin.Core;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Drives;

namespace Odin.Services.Membership.Circles;

public static class SystemCircleConstants
{
    public static readonly GuidId ConnectedIdentitiesSystemCircleId = GuidId.FromString("we_are_connected");

    public static readonly List<DriveGrantRequest> ConnectedIdentitiesSystemCircleInitialDrives =
    [
        new DriveGrantRequest()
        {
            PermissionedDrive = new PermissionedDrive()
            {
                Drive = SystemDriveConstants.ChatDrive,
                Permission = DrivePermission.Write | DrivePermission.WriteReactionsAndComments
            }
        },

        new DriveGrantRequest()
        {
            PermissionedDrive = new PermissionedDrive()
            {
                Drive = SystemDriveConstants.MailDrive,
                Permission = DrivePermission.Write
            }
        },

        new DriveGrantRequest()
        {
            PermissionedDrive = new PermissionedDrive()
            {
                Drive = SystemDriveConstants.FeedDrive,
                Permission = DrivePermission.Write
            }
        }
    ];
}