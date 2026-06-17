using System;
using System.Collections.Generic;
using Odin.Core;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Drives;

namespace Odin.Services.Membership.Circles;

/// <summary>
/// Built-in circles are provisioned for every identity (like system circles), but unlike system
/// circles they are not hidden or master-key-gated — the owner manages their membership as they
/// would any normal circle. They simply already exist with the identity out of the box.
/// </summary>
public static class BuiltInCircleConstants
{
    public static readonly GuidId EmergencyLocationAccessCircleId = Guid.Parse("8b5383a5927246f8a666f4f3fcb7392b");

    public static bool IsBuiltInCircle(Guid circleId)
    {
        return AllBuiltInCircles.Exists(c => c == circleId);
    }

    public static readonly List<GuidId> AllBuiltInCircles =
    [
        EmergencyLocationAccessCircleId
    ];

    public static readonly CircleDefinition EmergencyLocationAccessDefinition = new()
    {
        Id = EmergencyLocationAccessCircleId.Value,
        Name = "Emergency Location Access",
        Description = "Contains identities granted read access to your location in an emergency",
        DriveGrants =
        [
            new DriveGrantRequest()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = SystemDriveConstants.LocationDrive,
                    Permission = DrivePermission.Read
                }
            },
        ],
        Permissions = new PermissionSet()
        {
            Keys = []
        }
    };
}
