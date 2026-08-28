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
/// The apps that ship with an identity, each with the drives and circles it owns.
/// </summary>
/// <remarks>
/// <b>Nothing reads this yet.</b>  It is the target shape, built alongside the lists that currently
/// express the same facts separately -- <c>EnsureSystemDrivesExist</c>, <c>EnsureBuiltInApps</c>,
/// <c>SystemDrives</c>, <c>BuiltInAppIds</c>, and the circle constants.  Each becomes a projection of
/// <see cref="All"/>.
///
/// <para>
/// Not every app here is built-in: <see cref="SystemApp.BuiltIn"/> says which are
/// configured when an identity is initialized.  The rest own drives and circles but arrive only when
/// the owner installs them.
/// </para>
///
/// <para>
/// The two system circles are excluded throughout -- they are owned by no app and grant across six
/// drives, which is exactly why they do not fit this shape.  They stay in
/// <c>SystemCircleConstants</c> until they retire.
/// </para>
/// </remarks>
public static class BuiltinApps
{
    // ============================================================================================
    // THE TREE
    // ============================================================================================
    //
    public static readonly IReadOnlyList<SystemApp> All =
    [
        new(SystemAppConstants.ChatAppId, "Chat", "chat", BuiltIn: true,
            Drives:
            [
                BuiltinDrives.ChatDrive,
                BuiltinDrives.StickerDrive
            ],
            Circles: [BuiltinCircles.ChatCircle],
            Permissions: new PermissionSet(
                PermissionKeys.ReadConnections,
                PermissionKeys.SendPushNotifications,
                PermissionKeys.ReadConnectionRequests,
                PermissionKeys.SendIntroductions,
                PermissionKeys.UseTransitRead,
                PermissionKeys.UseTransitWrite,
                PermissionKeys.ManageContacts,
                PermissionKeys.ManageProfile,
                PermissionKeys.ManageCircleMembership)),

        new(SystemAppConstants.ContactsAppId, "Contacts", "contacts", BuiltIn: true,
            Drives:
            [
                BuiltinDrives.ContactDrive,
                BuiltinDrives.ProfileDrive
            ],
            Circles:
            [
                BuiltinCircles.FriendsCircle,
                BuiltinCircles.FamilyCircle,
                BuiltinCircles.WorkCircle,
                BuiltinCircles.AcquaintancesCircle
            ],
            Permissions: new PermissionSet()),

        new(SystemAppConstants.EmailAppId, "Email", "email", BuiltIn: true,
            Drives: [BuiltinDrives.EmailDrive],
            Circles: [BuiltinCircles.EmailCircle],
            Permissions: new PermissionSet()),

        new(SystemAppConstants.FeedAppId, "Feed", "feed", BuiltIn: true,
            Drives:
            [
                BuiltinDrives.FeedDrive,
                BuiltinDrives.PublicPostsChannelDrive
            ],
            Circles: [BuiltinCircles.FeedCircle],
            Permissions: new PermissionSet(
                PermissionKeys.ReadConnections,
                PermissionKeys.ReadConnectionRequests,
                PermissionKeys.ReadCircleMembership,
                PermissionKeys.ReadWhoIFollow,
                PermissionKeys.ReadMyFollowers,
                PermissionKeys.ManageFeed,
                PermissionKeys.UseTransitWrite,
                PermissionKeys.UseTransitRead,
                PermissionKeys.PublishStaticContent,
                PermissionKeys.SendPushNotifications)),

        new(SystemAppConstants.HomePageAppId, "HomePage", "homepage", BuiltIn: true,
            Drives: [BuiltinDrives.HomePageConfigDrive],
            Circles: [BuiltinCircles.HomePageCircle],
            Permissions: new PermissionSet()),

        new(SystemAppConstants.LocationAppId, "Location", "location", BuiltIn: true,
            Drives: [BuiltinDrives.LocationDrive],
            Circles: [BuiltinCircles.EmergencyLocationAccessCircle],
            Permissions: new PermissionSet()),

        new(SystemAppConstants.MailAppId, "Mail", "mail", BuiltIn: true,
            Drives: [BuiltinDrives.MailDrive],
            Circles: [BuiltinCircles.MailCircle],
            Permissions: new PermissionSet(
                PermissionKeys.ReadConnections,
                PermissionKeys.SendPushNotifications,
                PermissionKeys.ReadConnectionRequests,
                PermissionKeys.SendIntroductions,
                PermissionKeys.UseTransitWrite,
                PermissionKeys.ManageContacts)),

        new(SystemAppConstants.RecoveryAppId, "Recovery", "recovery", BuiltIn: true,
            Drives: [BuiltinDrives.ShardRecoveryDrive],
            Circles: [BuiltinCircles.RecoveryCircle],
            Permissions: new PermissionSet()),

        // Owns the transient drive. The two system circles are NOT listed: they belong to no app and
        // grant across six drives, which is exactly why they do not fit this shape.
        new(SystemAppConstants.SystemAppId, "System", "system", BuiltIn: true,
            Drives: [BuiltinDrives.TransientTempDrive],
            Circles: [],
            Permissions: new PermissionSet()),

        //
        // Not built-in: owned, but only arrive when the owner installs the app.
        //
        new(SystemAppConstants.CommunityAppId, "Community", "community", BuiltIn: false,
            Drives: [BuiltinDrives.CommunityDrive],
            Circles: [BuiltinCircles.CommunityCircle],
            Permissions: new PermissionSet()),

        // BuiltinDrives.ListsDrive and BuiltinDrives.MomentsDrive are seeded today despite their apps not being built-in, because
        // the system circles grant them and issuing a grant for an absent drive throws. That ends with
        // those circles.
        new(SystemAppConstants.ListsAppId, "Lists", "lists", BuiltIn: false,
            Drives: [BuiltinDrives.ListsDrive],
            Circles: [BuiltinCircles.ListsCircle],
            Permissions: new PermissionSet()),

        new(SystemAppConstants.MomentsAppId, "Moments", "moments", BuiltIn: false,
            Drives: [BuiltinDrives.MomentsDrive],
            Circles: [BuiltinCircles.MomentsCircle],
            Permissions: new PermissionSet()),

        new(SystemAppConstants.PhotoAppId, "Photo", "photo", BuiltIn: false,
            Drives: [BuiltinDrives.PhotoLibraryDrive],
            Circles: [BuiltinCircles.PhotosCircle],
            Permissions: new PermissionSet()),

        new(SystemAppConstants.SocialSyncAppId, "Social Sync", "social-sync", BuiltIn: false,
            Drives: [],
            Circles: [BuiltinCircles.SocialSyncCircle],
            Permissions: new PermissionSet()),

        new(SystemAppConstants.VaultAppId, "Vault", "vault", BuiltIn: false,
            Drives: [BuiltinDrives.WalletDrive, BuiltinDrives.VaultDrive],
            Circles: [BuiltinCircles.VaultCircle],
            Permissions: new PermissionSet())
    ];

    //

    //
    // Projections. Each replaces a list that currently states the same facts independently.
    //
    public static IEnumerable<SystemApp> BuiltIn => All.Where(a => a.BuiltIn);

    /// <summary>Drives a new identity is configured with.</summary>
    public static IEnumerable<CreateDriveRequest> SeededDrives => BuiltIn.SelectMany(a => a.Drives);

    /// <summary>Circles a new identity is configured with, excluding the two system circles.</summary>
    public static IEnumerable<CircleDefinition> SeededCircles => BuiltIn.SelectMany(a => a.Circles);

    public static IEnumerable<Guid> BuiltInAppIds => BuiltIn.Select(a => a.AppId);

    /// <summary>Every drive any app owns, whether or not it is seeded.</summary>
    public static IEnumerable<CreateDriveRequest> AllDrives => All.SelectMany(a => a.Drives);

    /// <summary>Every circle any app owns, whether or not it is seeded.</summary>
    public static IEnumerable<CircleDefinition> AllCircles => All.SelectMany(a => a.Circles);

    public static SystemApp Get(Guid appId) => All.FirstOrDefault(a => a.AppId == appId);

    public static IEnumerable<AppDriveGrant> GrantsFor(Guid appId) =>
        BuiltinAppDriveGrants.DriveGrants.Where(g => g.AppId == appId);
}
