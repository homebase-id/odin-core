using System.Collections.Generic;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Drives;

namespace Odin.Core.Services.Contacts.Circle;

public static class CircleConstants
{
    public static readonly GuidId SystemCircleId = GuidId.FromString("we_are_connected");

    public static readonly List<DriveGrantRequest> InitialSystemCircleDrives = new()
    {
        new DriveGrantRequest()
        {
            PermissionedDrive = new PermissionedDrive()
            {
                Drive = SystemDriveConstants.ChatDrive,
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
    };
}