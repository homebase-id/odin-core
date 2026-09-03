using System;
using System.Collections.Generic;
using System.Linq;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Membership.Circles;

namespace Odin.Services.Apps;

public static class SystemAppConstants
{
    /// <summary>
    /// Owns every drive that ships with an identity, so that no drive has a null <c>AppId</c>.
    /// </summary>
    /// <remarks>
    /// <c>docs/drive-addressing.md</c> requires <c>AppId</c> and <c>DriveSlug</c> to be set together or
    /// both NULL: NULLs are distinct in a unique index, so a slug on an <c>AppId</c>-less row is not
    /// covered by <c>UNIQUE(identityId, AppId, DriveSlug)</c> and two drives could claim the same one.
    /// Giving the system drives a real owner satisfies that invariant and makes them slug-addressable --
    /// answering open question 3 in that doc, which left it undecided.
    /// <para>
    /// <b>Declared first on purpose.</b>  This type and <see cref="SystemDriveConstants"/> reference each
    /// other, so their static initializers form a cycle; it resolves correctly only because this field is
    /// assigned before any field that reads <c>SystemDriveConstants</c>, and because that type assigns its
    /// <c>TargetDrive</c> fields before the request constants that read this one.  Reordering either
    /// silently yields <see cref="Guid.Empty"/>, which is why a test asserts these are non-empty.
    /// </para>
    /// <para>
    /// A holding position: ownership moves to the real owning app as each is built, one drive at a time.
    /// </para>
    /// </remarks>
    public static readonly Guid SystemAppId = Guid.Parse("ac126e09-54cb-4878-a690-856be692da16");
    public static readonly Guid ChatAppId = Guid.Parse("2d781401-3804-4b57-b4aa-d8e4e2ef39f4");
    public static readonly Guid FeedAppId = Guid.Parse("5f887d80-0132-4294-ba40-bda79155551d");
    public static readonly Guid PhotoAppId = Guid.Parse("32f0bdbf-017f-4fc0-8004-2d4631182d1e");
    public static readonly Guid MailAppId = Guid.Parse("6e8ecfff-7c15-40e4-94f4-d6e83bfb5857");

    //
    // Apps that own a drive but have no registration request yet. Coined here so drive ownership can be
    // expressed; an app id is permanent, because it is what Drives.AppId and Circle.AppId point at and
    // what a drive slug is unique within.
    //
    // The id the community client registers with (odin-js, common-app/src/constants.ts
    // COMMUNITY_APP_ID). It held a different guid here, which nothing in odin-js has ever used, so the
    // registered app matched no tree entry: its slug was derived from the display name as
    // "homebase-commu" rather than the "community" the tree names, and the drive and circle keyed to
    // this constant pointed at an app that does not exist.
    public static readonly Guid CommunityAppId = Guid.Parse("77ed6136-6b33-4654-8088-3d89c91e6065");
    public static readonly Guid ContactsAppId = Guid.Parse("a1a7bd26-7f52-461f-98cf-1f0ec969d97a");
    public static readonly Guid EmailAppId = Guid.Parse("4027937f-8a90-4f60-a5c3-18b850398482");
    public static readonly Guid HomePageAppId = Guid.Parse("135b6399-2d05-42d3-b1b6-124c2de6bd3f");
    public static readonly Guid ListsAppId = Guid.Parse("101c2134-c074-48b9-871b-944bb63548f7");
    public static readonly Guid LocationAppId = Guid.Parse("177d78f6-4084-45f3-b6d1-1f4735936fac");
    public static readonly Guid MomentsAppId = Guid.Parse("c61f5410-93d4-48dd-984a-965f0498e95e");
    public static readonly Guid RecoveryAppId = Guid.Parse("bc2fbb10-7574-4792-8db6-23c9b725a1d8");
    public static readonly Guid VaultAppId = Guid.Parse("6d38d41a-99f5-4f45-a591-9862d83e1fc8");
    public static readonly Guid SocialSyncAppId = Guid.Parse("99bbae1f-4c99-4944-aecd-0356bfe8974e");
    public static readonly Guid WebdropAppId = Guid.Parse("17bbd664-eed2-44d9-a66c-ddd310762b32");


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
                        Drive = WellKnownAppDrives.ChatDrive,
                        Permission = DrivePermission.Write | DrivePermission.React
                    }
                },
                new()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = WellKnownAppDrives.ListsDrive,
                        Permission = DrivePermission.Write | DrivePermission.React
                    }
                },
                new()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = WellKnownAppDrives.MomentsDrive,
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
                    Drive = WellKnownAppDrives.FeedDrive,
                    Permission = DrivePermission.ReadWrite
                }
            },
            new()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = WellKnownAppDrives.ChatDrive,
                    Permission = DrivePermission.ReadWrite
                }
            },
            new()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = WellKnownAppDrives.ListsDrive,
                    Permission = DrivePermission.ReadWrite
                }
            },
            new()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = WellKnownAppDrives.ContactDrive,
                    Permission = DrivePermission.ReadWrite
                }
            },
            new()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = WellKnownAppDrives.ProfileDrive,
                    Permission = DrivePermission.ReadWrite
                }
            },
            new()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = WellKnownAppDrives.MomentsDrive,
                    Permission = DrivePermission.ReadWrite
                }
            },
            new()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = WellKnownAppDrives.StickerDrive,
                    Permission = DrivePermission.ReadWrite
                }
            },
            new()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = WellKnownAppDrives.LocationDrive,
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
        AuthorizedCircles = [],
        Drives =
        [new()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = WellKnownAppDrives.StickerDrive,
                    Permission = DrivePermission.ReadWrite
                }
            },
            new()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = WellKnownAppDrives.FeedDrive,
                    Permission = DrivePermission.ReadWrite
                }
            },
            new()
            {
                // Standard profile Info
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = WellKnownAppDrives.ProfileDrive,
                    Permission = DrivePermission.Read
                }
            },
            new()
            {
                // Homepage Drive
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = WellKnownAppDrives.HomePageConfigDrive,
                    Permission = DrivePermission.Read
                }
            },
            new()
            {
                // Contact Drive
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = WellKnownAppDrives.ContactDrive,
                    Permission = DrivePermission.ReadWrite
                }
            },
            new()
            {
                // Public posts
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = WellKnownAppDrives.PublicPostsChannelDrive,
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
                        Drive = WellKnownAppDrives.MailDrive,
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
                    Drive = WellKnownAppDrives.MailDrive,
                    Permission = DrivePermission.ReadWrite
                }
            },
            new()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = WellKnownAppDrives.ContactDrive,
                    Permission = DrivePermission.ReadWrite
                }
            },
            new()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = WellKnownAppDrives.ProfileDrive,
                    Permission = DrivePermission.Read
                }
            },
            new()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = WellKnownAppDrives.StickerDrive,
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

    /// <summary>
    /// Built-in.  Granted ReadWrite on the drives it owns; no permission keys and no authorized circles
    /// were specified for it, so it holds neither.
    /// </summary>
    public static readonly AppRegistrationRequest ContactsAppRegistrationRequest = new()
    {
        AppId = ContactsAppId,
        Name = "Homebase - Contacts",
        AuthorizedCircles = [],
        CircleMemberPermissionGrant = new PermissionSetGrantRequest()
        {
            Drives = [],
            PermissionSet = new PermissionSet()
        },
        Drives =
        [
            new()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = WellKnownAppDrives.ContactDrive,
                    Permission = DrivePermission.ReadWrite
                }
            },
            new()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = WellKnownAppDrives.ProfileDrive,
                    Permission = DrivePermission.ReadWrite
                }
            }
        ],
        PermissionSet = new PermissionSet()
    };

    /// <summary>
    /// Built-in.  Granted ReadWrite on the drives it owns; no permission keys and no authorized circles
    /// were specified for it, so it holds neither.
    /// </summary>
    public static readonly AppRegistrationRequest EmailAppRegistrationRequest = new()
    {
        AppId = EmailAppId,
        Name = "Homebase - Email",
        AuthorizedCircles = [],
        CircleMemberPermissionGrant = new PermissionSetGrantRequest()
        {
            Drives = [],
            PermissionSet = new PermissionSet()
        },
        Drives =
        [
            new()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = WellKnownAppDrives.EmailAppDrive,
                    Permission = DrivePermission.ReadWrite
                }
            }
        ],
        PermissionSet = new PermissionSet()
    };

    /// <summary>
    /// Built-in.  Granted ReadWrite on the drives it owns; no permission keys and no authorized circles
    /// were specified for it, so it holds neither.
    /// </summary>
    public static readonly AppRegistrationRequest HomePageAppRegistrationRequest = new()
    {
        AppId = HomePageAppId,
        Name = "Homebase - HomePage",
        AuthorizedCircles = [],
        CircleMemberPermissionGrant = new PermissionSetGrantRequest()
        {
            Drives = [],
            PermissionSet = new PermissionSet()
        },
        Drives =
        [
            new()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = WellKnownAppDrives.HomePageConfigDrive,
                    Permission = DrivePermission.ReadWrite
                }
            }
        ],
        PermissionSet = new PermissionSet()
    };

    /// <summary>
    /// Built-in.  Granted ReadWrite on the drives it owns; no permission keys and no authorized circles
    /// were specified for it, so it holds neither.
    /// </summary>
    public static readonly AppRegistrationRequest LocationAppRegistrationRequest = new()
    {
        AppId = LocationAppId,
        Name = "Homebase - Location",
        AuthorizedCircles = [],
        CircleMemberPermissionGrant = new PermissionSetGrantRequest()
        {
            Drives = [],
            PermissionSet = new PermissionSet()
        },
        Drives =
        [
            new()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = WellKnownAppDrives.LocationDrive,
                    Permission = DrivePermission.ReadWrite
                }
            }
        ],
        PermissionSet = new PermissionSet()
    };

    /// <summary>
    /// Built-in.  Granted ReadWrite on the drives it owns; no permission keys and no authorized circles
    /// were specified for it, so it holds neither.
    /// </summary>
    public static readonly AppRegistrationRequest RecoveryAppRegistrationRequest = new()
    {
        AppId = RecoveryAppId,
        Name = "Homebase - Recovery",
        AuthorizedCircles = [],
        CircleMemberPermissionGrant = new PermissionSetGrantRequest()
        {
            Drives = [],
            PermissionSet = new PermissionSet()
        },
        Drives =
        [
            new()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = WellKnownAppDrives.ShardRecoveryDrive,
                    Permission = DrivePermission.ReadWrite
                }
            }
        ],
        PermissionSet = new PermissionSet()
    };

    /// <summary>
    /// Built-in.  Granted ReadWrite on the drives it owns; no permission keys and no authorized circles
    /// were specified for it, so it holds neither.
    /// </summary>
    public static readonly AppRegistrationRequest SystemAppRegistrationRequest = new()
    {
        AppId = SystemAppId,
        Name = "Homebase - System",
        AuthorizedCircles = [],
        CircleMemberPermissionGrant = new PermissionSetGrantRequest()
        {
            Drives = [],
            PermissionSet = new PermissionSet()
        },
        Drives =
        [
            new()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = SystemDriveConstants.TransientTempDrive,
                    Permission = DrivePermission.ReadWrite
                }
            }
        ],
        PermissionSet = new PermissionSet()
    };

    /// <summary>
    /// Built-in.  Granted ReadWrite on the drive it owns; its permission keys mirror chat's.
    /// </summary>
    public static readonly AppRegistrationRequest WebdropAppRegistrationRequest = new()
    {
        AppId = WebdropAppId,
        Name = "Homebase - Webdrop",
        AuthorizedCircles = [],
        CircleMemberPermissionGrant = new PermissionSetGrantRequest()
        {
            Drives = [],
            PermissionSet = new PermissionSet()
        },
        Drives =
        [
            new()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = WellKnownAppDrives.WebDropDrive,
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
            PermissionKeys.ManageContacts,
            PermissionKeys.ManageProfile,
            PermissionKeys.ManageCircleMembership)
    };

    /// <summary>
    /// Moments moved into <c>BuiltinApps.Builtin</c>, and a built-in app is registered at identity
    /// setup, which needs a registration request.
    /// </summary>
    /// <remarks>
    /// MomentsDrive is also in <c>BuiltinProvisioner.SystemCircleCarryOverDrives</c>: it was seeded
    /// before its app was built-in, because the system circles grant it.  Now that the app owns it on
    /// the tree, the carry-over is redundant for Moments -- harmless, since seeding is idempotent, and
    /// left alone so the carry-over list retires as one piece.
    /// </remarks>
    public static readonly AppRegistrationRequest MomentsAppRegistrationRequest = new()
    {
        AppId = MomentsAppId,
        Name = "Homebase - Moments",
        AuthorizedCircles = [],
        CircleMemberPermissionGrant = new PermissionSetGrantRequest()
        {
            Drives = [],
            PermissionSet = new PermissionSet()
        },
        Drives =
        [
            new()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = WellKnownAppDrives.MomentsDrive,
                    Permission = DrivePermission.ReadWrite
                }
            }
        ],
        PermissionSet = new PermissionSet(
            PermissionKeys.ReadConnections,
            PermissionKeys.SendPushNotifications,
            PermissionKeys.ReadConnectionRequests,
            PermissionKeys.UseTransitRead,
            PermissionKeys.UseTransitWrite)
    };

    /// <summary>
    /// Vault moved into <c>BuiltinApps.Builtin</c>.  It owns two drives -- the wallet and the vault --
    /// so its registration is granted both, matching what the tree says it owns.
    /// </summary>
    public static readonly AppRegistrationRequest VaultAppRegistrationRequest = new()
    {
        AppId = VaultAppId,
        Name = "Homebase - Vault",
        AuthorizedCircles = [],
        CircleMemberPermissionGrant = new PermissionSetGrantRequest()
        {
            Drives = [],
            PermissionSet = new PermissionSet()
        },
        Drives =
        [
            new()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = WellKnownAppDrives.VaultDrive,
                    Permission = DrivePermission.ReadWrite
                }
            }
        ],
        PermissionSet = new PermissionSet(
            PermissionKeys.ReadConnections,
            PermissionKeys.SendPushNotifications)
    };
}
