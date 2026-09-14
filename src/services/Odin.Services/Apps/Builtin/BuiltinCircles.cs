using System;
using System.Collections.Generic;
using System.Linq;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.Membership.Circles;

namespace Odin.Services.Apps.Builtin;

/// <summary>
/// Every circle an app owns.
/// </summary>
/// <remarks>
/// The single declaration of these, and the only one: a circle is created from here, by
/// <c>CircleDefinitionService.EnsureCircleExistsAsync</c>, which is the one path that writes an owning
/// app onto the row.
/// </remarks>
public static class BuiltinCircles
{
    // ============================================================================================
    // THE CIRCLES
    //
    // Every circle here grants only drives its own app owns -- no exceptions -- which is why circles
    // nest in the tree while cross-app drive grants do not. The two system circles are owned by no
    // app and grant across six drives, so they stay in SystemCircleConstants until they retire.
    // ============================================================================================
    //

    private static DriveGrantRequest Grant(TargetDrive drive, DrivePermission permission) =>
        new() { PermissionedDrive = new PermissionedDrive { Drive = drive, Permission = permission } };

    private const DrivePermission WriteReact = DrivePermission.Write | DrivePermission.React;

    // --- Chat ---
    public static readonly CircleDefinition ChatCircle = new()
    {
        Id = Guid.Parse("55900e0ab05347dca85c5ac2514e7fd3"),
        Name = "Chat",
        Description = "Members can chat with you",
        Emoji = "💬",
        AppId = SystemAppConstants.ChatAppId,
        GrantOn = CircleGrantOn.Connect,
        Designation = CircleDesignation.Personal,
        DriveGrants = [Grant(WellKnownAppDrives.ChatDrive, WriteReact)],
        Permissions = new PermissionSet { Keys = [] }
    };

    // --- Contacts ---
    public static readonly CircleDefinition AcquaintancesCircle = new()
    {
        Id = Guid.Parse("55c53cfda992192581cb4f006109df47"),
        Name = "Acquaintances",
        Description = "Your network",
        Emoji = "👋",
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
        Emoji = "👪",
        AppId = SystemAppConstants.ContactsAppId,
        GrantOn = CircleGrantOn.None,
        Designation = CircleDesignation.Personal,
        DriveGrants = [],
        Permissions = new PermissionSet { Keys = [] }
    };

    public static readonly CircleDefinition FriendsCircle = new()
    {
        Id = Guid.Parse("3d594614f445f6b00014e9b77730b833"),
        Name = "Friends",
        Description = "Your friends",
        Emoji = "🤝",
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
        Emoji = "💼",
        AppId = SystemAppConstants.ContactsAppId,
        GrantOn = CircleGrantOn.None,
        Designation = CircleDesignation.Personal,
        DriveGrants = [],
        Permissions = new PermissionSet { Keys = [] }
    };

    // --- Email ---
    public static readonly CircleDefinition EmailCircle = new()
    {
        Id = Guid.Parse("e210f78d11b7473f9ca8a837f7da369d"),
        Name = "Email",
        Description = "Members can use your email drive",
        Emoji = "✉️",
        AppId = SystemAppConstants.EmailAppId,
        GrantOn = CircleGrantOn.None,
        Designation = CircleDesignation.Personal,
        DriveGrants = [Grant(WellKnownAppDrives.EmailAppDrive, DrivePermission.ReadWrite)],
        Permissions = new PermissionSet { Keys = [] }
    };

    // --- Feed ---
    public static readonly CircleDefinition FeedCircle = new()
    {
        Id = Guid.Parse("cb29850cd326480b9c91deb0fbf061d7"),
        Name = "Feed",
        Description = "Members can post to your feed",
        Emoji = "📣",
        AppId = SystemAppConstants.FeedAppId,
        GrantOn = CircleGrantOn.None,
        Designation = CircleDesignation.Personal,
        DriveGrants = [Grant(WellKnownAppDrives.FeedDrive, WriteReact)],
        Permissions = new PermissionSet { Keys = [] }
    };

    // --- HomePage ---
    public static readonly CircleDefinition HomePageCircle = new()
    {
        Id = Guid.Parse("9a4b1ce10cf64063900442b4bdf9a78b"),
        Name = "HomePage",
        Description = "Members can use your home page",
        Emoji = "🏠",
        AppId = SystemAppConstants.HomePageAppId,
        GrantOn = CircleGrantOn.None,
        Designation = CircleDesignation.Personal,
        DriveGrants = [Grant(WellKnownAppDrives.HomePageConfigDrive, DrivePermission.ReadWrite)],
        Permissions = new PermissionSet { Keys = [] }
    };

    // --- Location ---
    public static readonly CircleDefinition EmergencyLocationAccessCircle = new()
    {
        Id = Guid.Parse("8b5383a5927246f8a666f4f3fcb7392b"),
        Name = "Emergency Location Access",
        Description = "Contains identities granted read access to your location in an emergency",
        Emoji = "🚨",
        AppId = SystemAppConstants.LocationAppId,
        GrantOn = CircleGrantOn.None,
        Designation = CircleDesignation.Personal,
        DriveGrants = [Grant(WellKnownAppDrives.LocationDrive, DrivePermission.Read)],
        Permissions = new PermissionSet { Keys = [] }
    };

    // --- Mail ---
    public static readonly CircleDefinition MailCircle = new()
    {
        Id = Guid.Parse("34cd48d94fe446c4a92e1b525b942071"),
        Name = "Mail",
        Description = "Members can mail you",
        Emoji = "📬",
        AppId = SystemAppConstants.MailAppId,
        GrantOn = CircleGrantOn.None,
        Designation = CircleDesignation.Personal,
        DriveGrants = [Grant(WellKnownAppDrives.MailDrive, WriteReact)],
        Permissions = new PermissionSet { Keys = [] }
    };

    // --- Recovery ---
    public static readonly CircleDefinition RecoveryCircle = new()
    {
        Id = Guid.Parse("c570d12b688f40c685c6da056979cef3"),
        Name = "Recovery",
        Description = "Members hold a shard of your recovery key",
        Emoji = "🔑",
        AppId = SystemAppConstants.RecoveryAppId,
        // Review, not OwnFlowConnect: holding a shard of the recovery key is the most trusted thing
        // anyone can be given, so it should never be reachable through an app's own consent flow
        // without the owner having vetted the person. Review is also the only tier that may carry
        // read grants and permission keys, which recovery is likelier than most to need.
        GrantOn = CircleGrantOn.Review,
        Designation = CircleDesignation.Personal,
        DriveGrants = [Grant(WellKnownAppDrives.ShardRecoveryDrive, DrivePermission.Write)],
        Permissions = new PermissionSet { Keys = [] }
    };

    // --- Community ---
    public static readonly CircleDefinition CommunityCircle = new()
    {
        Id = Guid.Parse("678306e834d0484ab7aabbb98e5c65b2"),
        Name = "Community",
        Description = "Members of your community",
        Emoji = "🏘️",
        AppId = SystemAppConstants.CommunityAppId,
        GrantOn = CircleGrantOn.OwnFlowConnect,
        Designation = CircleDesignation.Personal,
        DriveGrants = [],
        Permissions = new PermissionSet { Keys = [] }
    };

    // --- Lists ---
    public static readonly CircleDefinition ListsCircle = new()
    {
        Id = Guid.Parse("ff158d5eef354b4d9a7f08bd9c224b09"),
        Name = "Lists",
        Description = "Members can add to your lists",
        Emoji = "📋",
        AppId = SystemAppConstants.ListsAppId,
        GrantOn = CircleGrantOn.None,
        Designation = CircleDesignation.Personal,
        DriveGrants = [Grant(WellKnownAppDrives.ListsDrive, WriteReact)],
        Permissions = new PermissionSet { Keys = [] }
    };

    // --- Moments ---
    public static readonly CircleDefinition MomentsCircle = new()
    {
        Id = Guid.Parse("2942b45424164f85b37842d5e25388ed"),
        Name = "Moments",
        Description = "Members can share moments with you",
        Emoji = "✨",
        AppId = SystemAppConstants.MomentsAppId,
        GrantOn = CircleGrantOn.Review,
        Designation = CircleDesignation.Personal,
        DriveGrants = [Grant(WellKnownAppDrives.MomentsDrive, WriteReact)],
        Permissions = new PermissionSet { Keys = [] }
    };

    // --- Photo ---
    public static readonly CircleDefinition PhotosCircle = new()
    {
        Id = Guid.Parse("d9471317728a431c94d9842118acb676"),
        Name = "Photos",
        Description = "Members can see your photos",
        Emoji = "📷",
        AppId = SystemAppConstants.PhotoAppId,
        GrantOn = CircleGrantOn.OwnFlowConnect,
        Designation = CircleDesignation.Personal,
        DriveGrants = [],
        Permissions = new PermissionSet { Keys = [] }
    };

    // --- SocialSync ---
    public static readonly CircleDefinition SocialSyncCircle = new()
    {
        Id = Guid.Parse("94978395c00247cd8c5f32eb4ca61a06"),
        Name = "SocialSync",
        Description = "Members of your social sync",
        Emoji = "🔄",
        AppId = SystemAppConstants.SocialSyncAppId,
        GrantOn = CircleGrantOn.None,
        Designation = CircleDesignation.Personal,
        DriveGrants = [],
        Permissions = new PermissionSet { Keys = [] }
    };

    // --- Vault ---
    public static readonly CircleDefinition VaultCircle = new()
    {
        Id = Guid.Parse("d6529de8b0e94da5a1f676f272da8346"),
        Name = "Vault",
        Description = "Members can use your vault",
        Emoji = "🔒",
        AppId = SystemAppConstants.VaultAppId,
        GrantOn = CircleGrantOn.None,
        Designation = CircleDesignation.Personal,
        DriveGrants = [Grant(WellKnownAppDrives.VaultDrive, DrivePermission.ReadWrite)],
        Permissions = new PermissionSet { Keys = [] }
    };


    //

    // --- Webdrop ---
    public static readonly CircleDefinition WebdropCircle = new()
    {
        Id = Guid.Parse("a510f814c6054a5c8bddcc0fbd2f627a"),
        Name = "Webdrop",
        Description = "Members can drop files to you",
        Emoji = "📥",
        AppId = SystemAppConstants.WebdropAppId,
        GrantOn = CircleGrantOn.None,
        Designation = CircleDesignation.Personal,
        DriveGrants = [Grant(WellKnownAppDrives.WebDropDrive, WriteReact)],
        Permissions = new PermissionSet { Keys = [] }
    };
}
