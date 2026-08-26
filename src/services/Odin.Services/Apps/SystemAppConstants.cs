using System;
using System.Collections.Generic;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Membership.Circles;
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

    // Stable ids for the per-app grant-on-connect circles. These replace the chat suite's slice of the
    // frozen system-circle bundle: the same drive grants, but owned by the app that actually wants them
    // and computed at connect time rather than compiled into CircleConstants.
    //
    // Deposit-only by construction -- Write|React and nothing else -- which is what the definition-write
    // validator enforces for anything with GrantOn = Connect.
    public static readonly Guid ChatConnectCircleId = Guid.Parse("c17a1000-0000-4000-8000-000000000001");
    public static readonly Guid MailConnectCircleId = Guid.Parse("c17a1000-0000-4000-8000-000000000002");
    public static readonly Guid FeedConnectCircleId = Guid.Parse("c17a1000-0000-4000-8000-000000000003");

    /// <summary>
    /// The relationship circles the chat app owns: the set the owner console's setup wizard has always
    /// seeded on a fresh identity, now provisioned by the server alongside the app that presents them.
    /// </summary>
    /// <remarks>
    /// The ids are <c>md5(name)</c>, which is what the wizard assigns (odin-js <c>toGuidId</c>).  Keeping
    /// them is what makes an identity that already ran the wizard and one provisioned here the same
    /// identity: the v16 -> v17 migration rebinds the wizard's circles at these very ids rather than
    /// creating a second set beside them.
    /// <para>
    /// Created if missing and never overwritten -- deliberately not declared as
    /// <see cref="AppRegistrationRequest.DefaultCircles"/>, which an app re-registration reapplies and
    /// would use to reset a circle the owner has since renamed or regranted.  These are the owner's
    /// circles with the owner's people in them; the app owns them only in the sense of managing them.
    /// </para>
    /// </remarks>
    public static readonly IReadOnlyList<CreateCircleRequest> ChatRelationshipCircles =
    [
        new()
        {
            Id = Guid.Parse("3d594614f445f6b00014e9b77730b833"),
            Name = "Friends",
            Description = "Your friends",
            AppId = ChatAppId,
            Permissions = new PermissionSet(PermissionKeys.ReadConnections)
        },
        new()
        {
            Id = Guid.Parse("cefc4f7cbc8c34762e0f76703e7e174e"),
            Name = "Family",
            Description = "Your family",
            AppId = ChatAppId,
            Permissions = new PermissionSet(PermissionKeys.ReadConnections)
        },
        new()
        {
            Id = Guid.Parse("0f9263536b9fc61ada745644735bfd8f"),
            Name = "Work",
            Description = "Your professional connections",
            AppId = ChatAppId,
            Permissions = new PermissionSet(PermissionKeys.ReadConnections)
        },
        new()
        {
            Id = Guid.Parse("55c53cfda992192581cb4f006109df47"),
            Name = "Acquaintances",
            Description = "Your network",
            AppId = ChatAppId,
            Permissions = new PermissionSet(PermissionKeys.ReadConnections)
        }
    ];

    public static readonly AppRegistrationRequest ChatAppRegistrationRequest = new()
    {
        AppId = ChatAppId,
        Name = "Homebase - Chat",
        DefaultCircles =
        [
            new AppDefaultCircleRequest
            {
                Id = ChatConnectCircleId,
                Name = "Chat-only",
                Description = "People who can message you before you have reviewed them",
                GrantOn = CircleGrantOn.Connect,
                Designation = CircleDesignation.Personal,
                DriveGrants =
                [
                        new()
                        {
                            PermissionedDrive = new PermissionedDrive()
                            {
                                Drive = SystemDriveConstants.ChatDrive,
                                Permission = DrivePermission.Write | DrivePermission.React
                            }
                        },
                        new()
                        {
                            PermissionedDrive = new PermissionedDrive()
                            {
                                Drive = SystemDriveConstants.ListsDrive,
                                Permission = DrivePermission.Write | DrivePermission.React
                            }
                        },
                        new()
                        {
                            PermissionedDrive = new PermissionedDrive()
                            {
                                Drive = SystemDriveConstants.MomentsDrive,
                                Permission = DrivePermission.Write | DrivePermission.React
                            }
                        }
                ]
            }
        ],

        AuthorizedCircles = new List<Guid>()
        {
            // The app's own grant-on-connect circle, which carries the same drives the system circles did.
            ChatConnectCircleId
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
                },
                new()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = SystemDriveConstants.ListsDrive,
                        Permission = DrivePermission.Write | DrivePermission.React
                    }
                },
                new()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = SystemDriveConstants.MomentsDrive,
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
                    Drive = SystemDriveConstants.FeedDrive,
                    Permission = DrivePermission.ReadWrite
                }
            },
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
                    Drive = SystemDriveConstants.ListsDrive,
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
                    Permission = DrivePermission.ReadWrite
                }
            },
            new()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = SystemDriveConstants.MomentsDrive,
                    Permission = DrivePermission.ReadWrite
                }
            },
            new()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = SystemDriveConstants.StickerDrive,
                    Permission = DrivePermission.ReadWrite
                }
            },
            new()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = SystemDriveConstants.LocationDrive,
                    Permission = DrivePermission.ReadWrite
                }
            }
        ],
        PermissionSet = new PermissionSet(
            PermissionKeys.ReadConnections,
            PermissionKeys.SendPushNotifications,
            PermissionKeys.ReadConnectionRequests,
            PermissionKeys.SendIntroductions,
            PermissionKeys.UseTransitRead,
            PermissionKeys.UseTransitWrite,
            // Writes to the ContactDrive funnel through the Contact API (/api/v2/contacts), which
            // requires ManageContacts. Granted by default so the Chat app can manage contacts.
            PermissionKeys.ManageContacts,
            // Writes to the ProfileDrive funnel through the Profile attribute API, which requires
            // ManageProfile. Granted by default so the Chat app can edit profile attributes.
            PermissionKeys.ManageProfile,
            // Lets the Chat app add/remove an OdinId to/from a circle without the master key,
            // via the write-only deposit path (see PeerKeyStore.WriteOnlyKeyPair).
            PermissionKeys.ManageCircleMembership)
    };

    public static readonly AppRegistrationRequest FeedAppRegistrationRequest = new()
    {
        AppId = FeedAppId,
        Name = "Homebase - Feed",
        DefaultCircles =
        [
            new AppDefaultCircleRequest
            {
                Id = FeedConnectCircleId,
                Name = "Feed",
                Description = "People whose posts can reach your feed before you have reviewed them",
                GrantOn = CircleGrantOn.Connect,
                Designation = CircleDesignation.Personal,
                DriveGrants =
                [
                    new()
                    {
                        PermissionedDrive = new PermissionedDrive()
                        {
                            Drive = SystemDriveConstants.FeedDrive,
                            Permission = DrivePermission.Write | DrivePermission.React
                        }
                    }
                ]
            }
        ],

        AuthorizedCircles = [],
        Drives =
        [new()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = SystemDriveConstants.StickerDrive,
                    Permission = DrivePermission.ReadWrite
                }
            },
            new()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = SystemDriveConstants.FeedDrive,
                    Permission = DrivePermission.ReadWrite
                }
            },
            new()
            {
                // Standard profile Info
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = SystemDriveConstants.ProfileDrive,
                    Permission = DrivePermission.Read
                }
            },
            new()
            {
                // Homepage Drive
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = SystemDriveConstants.HomePageConfigDrive,
                    Permission = DrivePermission.Read
                }
            },
            new()
            {
                // Contact Drive
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = SystemDriveConstants.ContactDrive,
                    Permission = DrivePermission.ReadWrite
                }
            },
            new()
            {
                // Public posts
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = SystemDriveConstants.PublicPostsChannelDrive,
                    Permission = DrivePermission.ReadWrite
                }
            },
        ],
        PermissionSet = new PermissionSet(
            PermissionKeys.ReadConnections,
            PermissionKeys.ReadConnectionRequests,
            PermissionKeys.ReadCircleMembership,
            PermissionKeys.ReadWhoIFollow,
            PermissionKeys.ReadMyFollowers,
            PermissionKeys.ManageFeed,
            PermissionKeys.UseTransitWrite,
            PermissionKeys.UseTransitRead,
            PermissionKeys.PublishStaticContent,
            PermissionKeys.SendPushNotifications)
    };


    public static readonly AppRegistrationRequest MailAppRegistrationRequest = new()
    {
        AppId = MailAppId,
        Name = "Homebase - Mail",
        DefaultCircles =
        [
            new AppDefaultCircleRequest
            {
                Id = MailConnectCircleId,
                Name = "Mail",
                Description = "People who can mail you before you have reviewed them",
                GrantOn = CircleGrantOn.Connect,
                Designation = CircleDesignation.Personal,
                DriveGrants =
                [
                    new()
                    {
                        PermissionedDrive = new PermissionedDrive()
                        {
                            Drive = SystemDriveConstants.MailDrive,
                            Permission = DrivePermission.Write | DrivePermission.React
                        }
                    }
                ]
            }
        ],

        AuthorizedCircles = new List<Guid>()
        {
            MailConnectCircleId
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
            },
            new()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = SystemDriveConstants.StickerDrive,
                    Permission = DrivePermission.ReadWrite
                }
            }
        ],
        PermissionSet = new PermissionSet(
            PermissionKeys.ReadConnections,
            PermissionKeys.SendPushNotifications,
            PermissionKeys.ReadConnectionRequests,
            PermissionKeys.SendIntroductions,
            PermissionKeys.UseTransitWrite,
            // Writes to the ContactDrive funnel through the Contact API (/api/v2/contacts), which
            // requires ManageContacts. Granted by default so the Mail app can manage contacts.
            PermissionKeys.ManageContacts)
    };
}