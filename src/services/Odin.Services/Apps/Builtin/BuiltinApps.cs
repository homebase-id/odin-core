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
/// <c>SystemDrives</c> and the circle constants.  Each becomes a projection of
/// <see cref="All"/>.
///
/// <para>
/// Not every app here is built-in: <see cref="WellknownAppDefinitionBuiltIn"/> says which are
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
    /// <summary>
    /// The apps an identity is configured with: registered, their drives created, their circles
    /// provisioned.  <see cref="BuiltinProvisioner"/> walks exactly this list.
    /// </summary>
    public static readonly IReadOnlyList<WellknownAppDefinition> Builtin =
    [
        new(SystemAppConstants.ChatAppId, "Chat", "chat",
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

        new(SystemAppConstants.ContactsAppId, "Contacts", "contacts",
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

        new(SystemAppConstants.EmailAppId, "Email", "email",
            Drives: [BuiltinDrives.EmailDrive],
            Circles: [BuiltinCircles.EmailCircle],
            Permissions: new PermissionSet()),
        
        new(SystemAppConstants.LocationAppId, "Location", "location",
            Drives: [BuiltinDrives.LocationDrive],
            Circles: [BuiltinCircles.EmergencyLocationAccessCircle],
            Permissions: new PermissionSet()),

        new(SystemAppConstants.FeedAppId, "Feed", "feed",
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

        new(SystemAppConstants.HomePageAppId, "HomePage", "homepage",
            Drives: [BuiltinDrives.HomePageConfigDrive],
            Circles: [BuiltinCircles.HomePageCircle],
            Permissions: new PermissionSet()),


        new(SystemAppConstants.RecoveryAppId, "Recovery", "recovery",
            Drives: [BuiltinDrives.ShardRecoveryDrive],
            Circles: [BuiltinCircles.RecoveryCircle],
            Permissions: new PermissionSet()),

        // Owns the transient drive. The two system circles are NOT listed: they belong to no app and
        // grant across six drives, which is exactly why they do not fit this shape.

        new(SystemAppConstants.SystemAppId, "System", "system",
            Drives: [BuiltinDrives.TransientTempDrive],
            Circles: [],
            Permissions: new PermissionSet()),

        new(SystemAppConstants.MomentsAppId, "Moments", "moments",
            Drives: [BuiltinDrives.MomentsDrive],
            Circles: [BuiltinCircles.MomentsCircle],
            Permissions: new PermissionSet()),
        
        new(SystemAppConstants.WebdropAppId, "Webdrop", "webdrop",
            Drives: [BuiltinDrives.WebDropDrive],
            Circles: [BuiltinCircles.WebdropCircle],
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
        
        new(SystemAppConstants.VaultAppId, "Vault", "vault",
            Drives: [BuiltinDrives.VaultDrive],
            Circles: [BuiltinCircles.VaultCircle],
            Permissions: new PermissionSet()),
    ];

    /// <summary>
    /// Apps we know about that an identity does not start with.  They own drives and circles, but those
    /// arrive only when the owner installs the app.
    /// </summary>
    /// <remarks>
    /// Three of them own a drive that every identity already has, because it was seeded before ownership
    /// existed -- Lists, Moments and Vault.  A migration has to stamp those, which is the one reason
    /// this list is not simply inert.
    /// </remarks>
    public static readonly IReadOnlyList<WellknownAppDefinition> Wellknown =
    [
        new(SystemAppConstants.CommunityAppId, "Community", "community",
            Drives: [BuiltinDrives.CommunityDrive],
            Circles: [BuiltinCircles.CommunityCircle],
            Permissions: new PermissionSet()),
        
        new(SystemAppConstants.PhotoAppId, "Photo", "photo",
            Drives: [BuiltinDrives.PhotoLibraryDrive],
            Circles: [BuiltinCircles.PhotosCircle],
            Permissions: new PermissionSet()),

        new(SystemAppConstants.SocialSyncAppId, "Social Sync", "social-sync",
            Drives: [],
            Circles: [BuiltinCircles.SocialSyncCircle],
            Permissions: new PermissionSet()),
        
        // BuiltinDrives.ListsDrive and BuiltinDrives.MomentsDrive are seeded today despite their apps not being built-in, because
        // the system circles grant them and issuing a grant for an absent drive throws. That ends with
        // those circles.
        //
        // new(SystemAppConstants.ListsAppId, "Lists", "lists",
        //     Drives: [BuiltinDrives.ListsDrive],
        //     Circles: [BuiltinCircles.ListsCircle],
        //     Permissions: new PermissionSet()),
    ];

    /// <summary>Every app we know about, built-in or not.</summary>
    public static readonly IReadOnlyList<WellknownAppDefinition> All = [..Builtin, ..Wellknown];

    //

    //
    // Projections. Each replaces a list that currently states the same facts independently.
    //
    /// <summary>Drives a new identity is configured with.</summary>
    public static IEnumerable<CreateDriveRequest> SeededDrives => Builtin.SelectMany(a => a.Drives);

    /// <summary>Circles a new identity is configured with, excluding the two system circles.</summary>
    public static IEnumerable<CircleDefinition> SeededCircles => Builtin.SelectMany(a => a.Circles);

    public static IEnumerable<Guid> BuiltinAppIds => Builtin.Select(a => a.AppId);

    /// <summary>Every drive any app owns, whether or not it is seeded.</summary>
    public static IEnumerable<CreateDriveRequest> AllDrives => All.SelectMany(a => a.Drives);

    /// <summary>Every circle any app owns, whether or not it is seeded.</summary>
    public static IEnumerable<CircleDefinition> AllCircles => All.SelectMany(a => a.Circles);

    public static WellknownAppDefinition Get(Guid appId) => All.FirstOrDefault(a => a.AppId == appId);

    public static IEnumerable<AppDriveGrant> GrantsFor(Guid appId) =>
        BuiltinAppDriveGrants.DriveGrants.Where(g => g.AppId == appId);
}
