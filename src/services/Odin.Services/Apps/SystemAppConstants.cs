using System;
using System.Collections.Generic;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Membership.Circles;

namespace Odin.Services.Apps;

public static class SystemAppConstants
{
    public static readonly Guid OwnerAppId = Guid.Parse("ac126e09-54cb-4878-a690-856be692da16");
    public static readonly Guid ChatAppId = Guid.Parse("2d781401-3804-4b57-b4aa-d8e4e2ef39f4");
    public static readonly Guid FeedAppId = Guid.Parse("5f887d80-0132-4294-ba40-bda79155551d");
    public static readonly Guid PhotoAppId = Guid.Parse("32f0bdbf-017f-4fc0-8004-2d4631182d1e");
    public static readonly Guid MailAppId = Guid.Parse("6e8ecfff-7c15-40e4-94f4-d6e83bfb5857");

    public static readonly AppRegistrationRequest ChatAppRegistrationRequest = new()
    {
        AppId = ChatAppId,
        Name = "Homebase - Chat",
        AuthorizedCircles = new List<Guid>() //note: by default the system circle will have write access to chat drive
        {
            SystemCircleConstants.ConfirmedConnectionsCircleId,
            SystemCircleConstants.AutoConnectionsCircleId
        },
        CircleMemberPermissionGrant = new PermissionSetGrantRequest()
        {
            Drives =
            [
                new()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = SystemDriveConstants.ChatDrive,
                        Permission = DrivePermission.Write | DrivePermission.React
                    }
                }
            ],
            PermissionSet = new PermissionSet()
        },
        Drives =
        [
            new()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = SystemDriveConstants.ChatDrive,
                    Permission = DrivePermission.ReadWrite
                }
            },
            new()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = SystemDriveConstants.ContactDrive,
                    Permission = DrivePermission.ReadWrite
                }
            },
            new()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = SystemDriveConstants.ProfileDrive,
                    Permission = DrivePermission.Read
                }
            }
        ],
        PermissionSet = new PermissionSet(
            PermissionKeys.ReadConnections,
            PermissionKeys.SendPushNotifications,
            PermissionKeys.ReadConnectionRequests,
            PermissionKeys.SendIntroductions,
            PermissionKeys.UseTransitWrite)
    };


    public static readonly AppRegistrationRequest MailAppRegistrationRequest = new()
    {
        AppId = MailAppId,
        Name = "Homebase - Mail",
        AuthorizedCircles = new List<Guid>() //note: by default the system circle will have write access to chat drive
        {
            SystemCircleConstants.ConfirmedConnectionsCircleId,
            SystemCircleConstants.AutoConnectionsCircleId
        },
        CircleMemberPermissionGrant = new PermissionSetGrantRequest()
        {
            Drives =
            [
                new()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = SystemDriveConstants.MailDrive,
                        Permission = DrivePermission.Write
                    }
                }
            ],
            PermissionSet = new PermissionSet()
        },
        Drives =
        [
            new()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = SystemDriveConstants.MailDrive,
                    Permission = DrivePermission.ReadWrite
                }
            },
            new()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = SystemDriveConstants.ContactDrive,
                    Permission = DrivePermission.ReadWrite
                }
            },
            new()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = SystemDriveConstants.ProfileDrive,
                    Permission = DrivePermission.Read
                }
            }
        ],
        PermissionSet = new PermissionSet(
            PermissionKeys.ReadConnections,
            PermissionKeys.SendPushNotifications,
            PermissionKeys.ReadConnectionRequests,
            PermissionKeys.SendIntroductions,
            PermissionKeys.UseTransitWrite)
    };
}
