using System;
using System.Collections.Generic;
using Odin.Services.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Drives;

namespace Odin.Services.Membership.Circles;

/// <summary>
/// Circles with a fixed, well-known id that belong to an app rather than to the owner.  The counterpart
/// of <see cref="WellKnownAppDrives"/>, and the same rule applies: an app may create, modify and delete
/// only circles whose <see cref="CircleDefinition.AppId"/> is its own.
///
/// The two system circles stay in <see cref="SystemCircleConstants"/> and Emergency Location Access in
/// <see cref="BuiltInCircleConstants"/>; both are stamped with their owning app there rather than moved,
/// to keep their existing call sites intact.
///
/// DO NOT CHANGE ANY VALUE: a circle id is what <c>CircleMember</c> rows and minted grants point at, so
/// changing one orphans every membership already recorded against it.
/// </summary>
public static class WellKnownAppCircles
{
    private static DriveGrantRequest Grant(TargetDrive drive, DrivePermission permission) =>
        new() { PermissionedDrive = new PermissionedDrive { Drive = drive, Permission = permission } };

    private const DrivePermission WriteReact = DrivePermission.Write | DrivePermission.React;

    //
    // Chat -- the one GrantOn = Connect circle, so it is granted ambiently to auto-connections with no
    // owner review. Write/react only, and no permission keys: that is the deposit-only invariant in
    // docs/connection-defaults.md, and it is why a read grant does not belong here.
    //
    public static readonly CircleDefinition ChatCircle = new()
    {
        Id = Guid.Parse("55900e0ab05347dca85c5ac2514e7fd3"),
        Name = "Chat",
        Description = "Members can chat with you",
        AppId = SystemAppConstants.ChatAppId,
        GrantOn = CircleGrantOn.Connect,
        Designation = CircleDesignation.Personal,
        DriveGrants =
        [
            Grant(WellKnownAppDrives.ChatDrive, WriteReact)
        ],
        Permissions = new PermissionSet { Keys = [] }
    };

    //
    // Contacts -- the relationship circles. These ids predate the column: the owner console's setup
    // wizard has been creating them client-side, so an identity that ran it already holds them.
    //
    public static readonly CircleDefinition FriendsCircle = new()
    {
        Id = Guid.Parse("3d594614f445f6b00014e9b77730b833"),
        Name = "Friends",
        Description = "Your friends",
        AppId = SystemAppConstants.ContactsAppId,
        GrantOn = CircleGrantOn.None,
        Designation = CircleDesignation.Personal,
        DriveGrants = [],
        Permissions = new PermissionSet { Keys = [] }
    };

    public static readonly CircleDefinition FamilyCircle = new()
    {
        Id = Guid.Parse("cefc4f7cbc8c34762e0f76703e7e174e"),
        Name = "Family",
        Description = "Your family",
        AppId = SystemAppConstants.ContactsAppId,
        GrantOn = CircleGrantOn.None,
        Designation = CircleDesignation.Personal,
        DriveGrants = [],
        Permissions = new PermissionSet { Keys = [] }
    };

    public static readonly CircleDefinition WorkCircle = new()
    {
        Id = Guid.Parse("0f9263536b9fc61ada745644735bfd8f"),
        Name = "Work",
        Description = "Your professional connections",
        AppId = SystemAppConstants.ContactsAppId,
        GrantOn = CircleGrantOn.None,
        Designation = CircleDesignation.Personal,
        DriveGrants = [],
        Permissions = new PermissionSet { Keys = [] }
    };

    public static readonly CircleDefinition AcquaintancesCircle = new()
    {
        Id = Guid.Parse("55c53cfda992192581cb4f006109df47"),
        Name = "Acquaintances",
        Description = "Your network",
        AppId = SystemAppConstants.ContactsAppId,
        GrantOn = CircleGrantOn.None,
        Designation = CircleDesignation.Personal,
        DriveGrants = [],
        Permissions = new PermissionSet { Keys = [] }
    };

    //
    // One circle per remaining app
    //
    public static readonly CircleDefinition CommunityCircle = new()
    {
        Id = Guid.Parse("678306e834d0484ab7aabbb98e5c65b2"),
        Name = "Community",
        Description = "Members of your community",
        AppId = SystemAppConstants.CommunityAppId,
        GrantOn = CircleGrantOn.None,
        Designation = CircleDesignation.Personal,
        DriveGrants = [],
        Permissions = new PermissionSet { Keys = [] }
    };

    public static readonly CircleDefinition EmailCircle = new()
    {
        Id = Guid.Parse("e210f78d11b7473f9ca8a837f7da369d"),
        Name = "Email",
        Description = "Members can use your email drive",
        AppId = SystemAppConstants.EmailAppId,
        GrantOn = CircleGrantOn.None,
        Designation = CircleDesignation.Personal,
        DriveGrants = [Grant(WellKnownAppDrives.EmailAppDrive, DrivePermission.ReadWrite)],
        Permissions = new PermissionSet { Keys = [] }
    };

    public static readonly CircleDefinition FeedCircle = new()
    {
        Id = Guid.Parse("cb29850cd326480b9c91deb0fbf061d7"),
        Name = "Feed",
        Description = "Members can post to your feed",
        AppId = SystemAppConstants.FeedAppId,
        GrantOn = CircleGrantOn.None,
        Designation = CircleDesignation.Personal,
        DriveGrants = [Grant(WellKnownAppDrives.FeedDrive, WriteReact)],
        Permissions = new PermissionSet { Keys = [] }
    };

    public static readonly CircleDefinition HomePageCircle = new()
    {
        Id = Guid.Parse("9a4b1ce10cf64063900442b4bdf9a78b"),
        Name = "HomePage",
        Description = "Members can use your home page",
        AppId = SystemAppConstants.HomePageAppId,
        GrantOn = CircleGrantOn.None,
        Designation = CircleDesignation.Personal,
        DriveGrants = [Grant(WellKnownAppDrives.HomePageConfigDrive, DrivePermission.ReadWrite)],
        Permissions = new PermissionSet { Keys = [] }
    };

    public static readonly CircleDefinition ListsCircle = new()
    {
        Id = Guid.Parse("ff158d5eef354b4d9a7f08bd9c224b09"),
        Name = "Lists",
        Description = "Members can add to your lists",
        AppId = SystemAppConstants.ListsAppId,
        GrantOn = CircleGrantOn.None,
        Designation = CircleDesignation.Personal,
        DriveGrants = [Grant(WellKnownAppDrives.ListsDrive, WriteReact)],
        Permissions = new PermissionSet { Keys = [] }
    };

    public static readonly CircleDefinition MailCircle = new()
    {
        Id = Guid.Parse("34cd48d94fe446c4a92e1b525b942071"),
        Name = "Mail",
        Description = "Members can mail you",
        AppId = SystemAppConstants.MailAppId,
        GrantOn = CircleGrantOn.None,
        Designation = CircleDesignation.Personal,
        DriveGrants = [Grant(WellKnownAppDrives.MailDrive, WriteReact)],
        Permissions = new PermissionSet { Keys = [] }
    };

    public static readonly CircleDefinition MomentsCircle = new()
    {
        Id = Guid.Parse("2942b45424164f85b37842d5e25388ed"),
        Name = "Moments",
        Description = "Members can share moments with you",
        AppId = SystemAppConstants.MomentsAppId,
        GrantOn = CircleGrantOn.Review,
        Designation = CircleDesignation.Personal,
        DriveGrants = [Grant(WellKnownAppDrives.MomentsDrive, WriteReact)],
        Permissions = new PermissionSet { Keys = [] }
    };

    public static readonly CircleDefinition PhotosCircle = new()
    {
        Id = Guid.Parse("d9471317728a431c94d9842118acb676"),
        Name = "Photos",
        Description = "Members can see your photos",
        AppId = SystemAppConstants.PhotoAppId,
        GrantOn = CircleGrantOn.None,
        Designation = CircleDesignation.Personal,
        DriveGrants = [],
        Permissions = new PermissionSet { Keys = [] }
    };

    public static readonly CircleDefinition RecoveryCircle = new()
    {
        Id = Guid.Parse("c570d12b688f40c685c6da056979cef3"),
        Name = "Recovery",
        Description = "Members hold a shard of your recovery key",
        AppId = SystemAppConstants.RecoveryAppId,
        GrantOn = CircleGrantOn.None,
        Designation = CircleDesignation.Personal,
        DriveGrants = [Grant(WellKnownAppDrives.ShardRecoveryDrive, DrivePermission.Write)],
        Permissions = new PermissionSet { Keys = [] }
    };

    public static readonly CircleDefinition SocialSyncCircle = new()
    {
        Id = Guid.Parse("94978395c00247cd8c5f32eb4ca61a06"),
        Name = "SocialSync",
        Description = "Members of your social sync",
        AppId = SystemAppConstants.SocialSyncAppId,
        GrantOn = CircleGrantOn.None,
        Designation = CircleDesignation.Personal,
        DriveGrants = [],
        Permissions = new PermissionSet { Keys = [] }
    };

    public static readonly CircleDefinition VaultCircle = new()
    {
        Id = Guid.Parse("d6529de8b0e94da5a1f676f272da8346"),
        Name = "Vault",
        Description = "Members can use your vault",
        AppId = SystemAppConstants.VaultAppId,
        GrantOn = CircleGrantOn.None,
        Designation = CircleDesignation.Personal,
        DriveGrants = [Grant(WellKnownAppDrives.VaultDrive, DrivePermission.ReadWrite)],
        Permissions = new PermissionSet { Keys = [] }
    };

    /// <summary>
    /// Every app-owned circle declared here, in no particular order.
    /// </summary>
    public static readonly IReadOnlyList<CircleDefinition> All =
    [
        ChatCircle,
        FriendsCircle, FamilyCircle, WorkCircle, AcquaintancesCircle,
        CommunityCircle, EmailCircle, FeedCircle, HomePageCircle, ListsCircle,
        MailCircle, MomentsCircle, PhotosCircle, RecoveryCircle, SocialSyncCircle, VaultCircle
    ];
}
