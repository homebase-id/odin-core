using System.Collections.Generic;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Drives;

namespace Youverse.Core.Services.Contacts.Circle;

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
        }
    };
}