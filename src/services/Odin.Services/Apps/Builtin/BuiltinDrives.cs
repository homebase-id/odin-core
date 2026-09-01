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
/// Every drive an app owns, fully described: identity, address and settings in one place.
/// </summary>
/// <remarks>
/// Copied from <c>SystemDriveConstants</c>, which still holds its own copies and is what still runs.
/// </remarks>
public static class BuiltinDrives
{
    public const string ChannelDriveTypeSlug = "channel";

    //
    // ============================================================================================
    // THE DRIVES
    //
    // Declared here so a drive is fully described where it is owned: identity, address and settings
    // in one place. Copied from SystemDriveConstants, which still holds its own copies -- the two are
    // duplicated on purpose while this takes shape, and SystemDriveConstants is what still runs.
    // ============================================================================================
    //

    // --- Chat ---
    public static readonly CreateDriveRequest ChatDrive = new()
    {
        Name = "Chat Drive", TargetDrive = WellKnownAppDrives.ChatDrive, Metadata = "",
        AppId = SystemAppConstants.ChatAppId, DriveSlug = "chat", DriveTypeSlug = "chat",
        AllowAnonymousReads = false,
        // TODO: should be owner-only, pending a decision on auto-provisioning; false so it could be
        // added to the system circle.
        OwnerOnly = false
    };

    public static readonly CreateDriveRequest StickerDrive = new()
    {
        Name = "Sticker Drive", TargetDrive = WellKnownAppDrives.StickerDrive, Metadata = "",
        AppId = SystemAppConstants.ChatAppId, DriveSlug = "stickers", DriveTypeSlug = "sticker",
        AllowAnonymousReads = false, OwnerOnly = false
    };

    // --- Contacts ---
    public static readonly CreateDriveRequest ContactDrive = new()
    {
        Name = "Contacts", TargetDrive = WellKnownAppDrives.ContactDrive, Metadata = "",
        AppId = SystemAppConstants.ContactsAppId, DriveSlug = "contacts", DriveTypeSlug = "contact",
        AllowAnonymousReads = false, OwnerOnly = true
    };

    public static readonly CreateDriveRequest ProfileDrive = new()
    {
        Name = "Standard Profile Info", TargetDrive = WellKnownAppDrives.ProfileDrive, Metadata = "",
        AppId = SystemAppConstants.ContactsAppId, DriveSlug = "profile", DriveTypeSlug = "profile",
        AllowAnonymousReads = true, OwnerOnly = false
    };

    // --- Email ---
    public static readonly CreateDriveRequest EmailDrive = new()
    {
        Name = "Email", TargetDrive = WellKnownAppDrives.EmailAppDrive, Metadata = "",
        AppId = SystemAppConstants.EmailAppId, DriveSlug = "email", DriveTypeSlug = "email",
        AllowAnonymousReads = false, OwnerOnly = true
    };

    // --- Feed ---
    public static readonly CreateDriveRequest FeedDrive = new()
    {
        Name = "Feed", TargetDrive = WellKnownAppDrives.FeedDrive, Metadata = "",
        AppId = SystemAppConstants.FeedAppId, DriveSlug = "feed", DriveTypeSlug = "feed",
        AllowAnonymousReads = false, OwnerOnly = true
    };

    public static readonly CreateDriveRequest PublicPostsChannelDrive = new()
    {
        Name = "Public Posts", TargetDrive = WellKnownAppDrives.PublicPostsChannelDrive, Metadata = "",
        AppId = SystemAppConstants.FeedAppId, DriveSlug = "posts", DriveTypeSlug = ChannelDriveTypeSlug,
        AllowAnonymousReads = true, OwnerOnly = false, AllowSubscriptions = true,
        // The only drive seeded CDN-on: public posts and their media are what the CDN exists to serve,
        // and CdnAuthPathHandler fails outright when no drive is enabled.
        AllowCdn = true
    };

    // --- HomePage ---
    public static readonly CreateDriveRequest HomePageConfigDrive = new()
    {
        Name = "Homepage Config", TargetDrive = WellKnownAppDrives.HomePageConfigDrive, Metadata = "",
        AppId = SystemAppConstants.HomePageAppId, DriveSlug = "home", DriveTypeSlug = "profile",
        AllowAnonymousReads = true, OwnerOnly = false
    };

    // --- Location ---
    public static readonly CreateDriveRequest LocationDrive = new()
    {
        Name = "Location Drive", TargetDrive = WellKnownAppDrives.LocationDrive, Metadata = "",
        AppId = SystemAppConstants.LocationAppId, DriveSlug = "location", DriveTypeSlug = "location",
        AllowAnonymousReads = false, OwnerOnly = false
    };

    // --- Mail ---
    public static readonly CreateDriveRequest MailDrive = new()
    {
        Name = "Mail Drive", TargetDrive = WellKnownAppDrives.MailDrive, Metadata = "",
        AppId = SystemAppConstants.MailAppId, DriveSlug = "mail", DriveTypeSlug = "mail",
        AllowAnonymousReads = false,
        // TODO: same pending decision as ChatDrive.
        OwnerOnly = false
    };

    // --- Recovery ---
    public static readonly CreateDriveRequest ShardRecoveryDrive = new()
    {
        Name = "Shard Recovery Drive", TargetDrive = WellKnownAppDrives.ShardRecoveryDrive, Metadata = "",
        AppId = SystemAppConstants.RecoveryAppId, DriveSlug = "shard-recovery", DriveTypeSlug = "shard-recovery",
        AllowAnonymousReads = false, OwnerOnly = false
    };

    // --- System ---
    public static readonly CreateDriveRequest TransientTempDrive = new()
    {
        Name = "Transient temp drive", TargetDrive = SystemDriveConstants.TransientTempDrive, Metadata = "",
        AppId = SystemAppConstants.SystemAppId, DriveSlug = "transient", DriveTypeSlug = "transient",
        AllowAnonymousReads = false, OwnerOnly = false
    };

    // --- Lists ---
    public static readonly CreateDriveRequest ListsDrive = new()
    {
        Name = "Lists Drive", TargetDrive = WellKnownAppDrives.ListsDrive, Metadata = "",
        AppId = SystemAppConstants.ListsAppId, DriveSlug = "lists", DriveTypeSlug = "list",
        AllowAnonymousReads = false, OwnerOnly = false,
        Attributes = new Dictionary<string, string>
        {
            { BuiltInDriveAttributes.IsCollaborativeChannel, bool.TrueString }
        }
    };

    // --- Moments ---
    public static readonly CreateDriveRequest MomentsDrive = new()
    {
        Name = "Moments Drive", TargetDrive = WellKnownAppDrives.MomentsDrive, Metadata = "",
        AppId = SystemAppConstants.MomentsAppId, DriveSlug = "moments", DriveTypeSlug = "list",
        AllowAnonymousReads = false, OwnerOnly = false
    };

    // --- Vault ---
    public static readonly CreateDriveRequest WalletDrive = new()
    {
        Name = "Wallet", TargetDrive = WellKnownAppDrives.WalletDrive, Metadata = "",
        AppId = SystemAppConstants.VaultAppId, DriveSlug = "wallet", DriveTypeSlug = "profile",
        AllowAnonymousReads = false, OwnerOnly = true
    };

    //
    // Drives the mapping names that do not exist yet, so have no counterpart in SystemDriveConstants.
    // Settings are placeholders -- nothing seeds these, since none of their apps is built-in.
    //
    public static readonly CreateDriveRequest VaultDrive = new()
    {
        Name = "Vault", TargetDrive = WellKnownAppDrives.VaultDrive, Metadata = "",
        AppId = SystemAppConstants.VaultAppId, DriveSlug = "vault", DriveTypeSlug = "vault",
        AllowAnonymousReads = false, OwnerOnly = true
    };

    public static readonly CreateDriveRequest CommunityDrive = new()
    {
        Name = "Community", TargetDrive = WellKnownAppDrives.CommunityDrive, Metadata = "",
        AppId = SystemAppConstants.CommunityAppId, DriveSlug = "community", DriveTypeSlug = "community",
        AllowAnonymousReads = false, OwnerOnly = false
    };

    public static readonly CreateDriveRequest PhotoLibraryDrive = new()
    {
        Name = "Photo Library", TargetDrive = WellKnownAppDrives.PhotoLibraryDrive, Metadata = "",
        AppId = SystemAppConstants.PhotoAppId, DriveSlug = "photos", DriveTypeSlug = "photos",
        AllowAnonymousReads = false, OwnerOnly = false
    };

    //

    // --- Webdrop ---
    public static readonly CreateDriveRequest WebDropDrive = new()
    {
        Name = "WebDrop", TargetDrive = WellKnownAppDrives.WebDropDrive, Metadata = "",
        AppId = SystemAppConstants.WebdropAppId, DriveSlug = "webdrop", DriveTypeSlug = "webdrop",
        AllowAnonymousReads = true, OwnerOnly = false
    };

    /// <summary>
    /// Drives the owner may not rename, re-mode or archive.  <c>DriveManager</c> refuses those
    /// operations for anything in here, and it is what the owner console shows as a system drive.
    /// </summary>
    /// <remarks>
    /// This is "we provisioned it", not "it is systemic".  ListsDrive and MomentsDrive are in the list
    /// and belong to apps that are not even built-in -- they are provisioned only because the system
    /// circles grant them, and issuing a grant for an absent drive throws.  They leave this list when
    /// those circles retire.
    /// <para>
    /// Protected is deliberately the same set as provisioned: a drive the system creates and the owner
    /// can archive is a trap.  Keep the two in step -- they drifted once already, when WalletDrive
    /// stopped being provisioned and EmailAppDrive started, and neither was reflected here.
    /// </para>
    /// </remarks>
    public static readonly IReadOnlyList<TargetDrive> Protected =
    [
        SystemDriveConstants.TransientTempDrive,
        WellKnownAppDrives.ContactDrive,
        WellKnownAppDrives.ProfileDrive,
        WellKnownAppDrives.ChatDrive,
        WellKnownAppDrives.FeedDrive,
        WellKnownAppDrives.HomePageConfigDrive,
        WellKnownAppDrives.MailDrive,
        WellKnownAppDrives.PublicPostsChannelDrive,
        WellKnownAppDrives.ShardRecoveryDrive,
        WellKnownAppDrives.MomentsDrive,
        WellKnownAppDrives.StickerDrive,
        WellKnownAppDrives.ListsDrive,
        WellKnownAppDrives.LocationDrive,
        WellKnownAppDrives.EmailAppDrive
    ];

    /// <summary>
    /// True when the owner may not modify this drive.  See <see cref="Protected"/>.
    /// </summary>
    public static bool IsProtected(Guid driveId) => Protected.Any(d => d.Alias == driveId);
}
