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
/// What each app may do to drives, including ones it does not own.
/// </summary>
/// <remarks>
/// Deliberately flat and separate from the tree: this is a graph, not a hierarchy.  Eleven of the
/// eighteen supplied rows cross app boundaries, so nesting them would mean one app's node referencing
/// another's -- a static-initializer cycle waiting to happen.  These reference the drive constants.
/// </remarks>
public static class BuiltinAppDriveGrants
{
    // ============================================================================================
    // CROSS-APP DRIVE GRANTS -- what each app may do to drives, including ones it does not own.
    // Deliberately flat: this is a graph, not a tree.
    // ============================================================================================
    //
    public static readonly IReadOnlyList<AppDriveGrant> DriveGrants =
    [
        new(SystemAppConstants.ChatAppId, WellKnownAppDrives.FeedDrive, DrivePermission.ReadWrite),
        new(SystemAppConstants.ChatAppId, WellKnownAppDrives.ChatDrive, DrivePermission.ReadWrite),
        new(SystemAppConstants.ChatAppId, WellKnownAppDrives.ListsDrive, DrivePermission.ReadWrite),
        new(SystemAppConstants.ChatAppId, WellKnownAppDrives.ContactDrive, DrivePermission.ReadWrite),
        new(SystemAppConstants.ChatAppId, WellKnownAppDrives.ProfileDrive, DrivePermission.ReadWrite),
        new(SystemAppConstants.ChatAppId, WellKnownAppDrives.MomentsDrive, DrivePermission.ReadWrite),
        new(SystemAppConstants.ChatAppId, WellKnownAppDrives.StickerDrive, DrivePermission.ReadWrite),
        new(SystemAppConstants.ChatAppId, WellKnownAppDrives.LocationDrive, DrivePermission.ReadWrite),

        new(SystemAppConstants.FeedAppId, WellKnownAppDrives.StickerDrive, DrivePermission.ReadWrite),
        new(SystemAppConstants.FeedAppId, WellKnownAppDrives.FeedDrive, DrivePermission.ReadWrite),
        new(SystemAppConstants.FeedAppId, WellKnownAppDrives.ProfileDrive, DrivePermission.Read),
        new(SystemAppConstants.FeedAppId, WellKnownAppDrives.HomePageConfigDrive, DrivePermission.Read),
        new(SystemAppConstants.FeedAppId, WellKnownAppDrives.ContactDrive, DrivePermission.ReadWrite),
        new(SystemAppConstants.FeedAppId, WellKnownAppDrives.PublicPostsChannelDrive, DrivePermission.ReadWrite),

        new(SystemAppConstants.MailAppId, WellKnownAppDrives.MailDrive, DrivePermission.ReadWrite),
        new(SystemAppConstants.MailAppId, WellKnownAppDrives.ContactDrive, DrivePermission.ReadWrite),
        new(SystemAppConstants.MailAppId, WellKnownAppDrives.ProfileDrive, DrivePermission.Read),
        new(SystemAppConstants.MailAppId, WellKnownAppDrives.StickerDrive, DrivePermission.ReadWrite),

        // The six apps with no rows in the supplied mapping: each granted what it owns.
        new(SystemAppConstants.ContactsAppId, WellKnownAppDrives.ContactDrive, DrivePermission.ReadWrite),
        new(SystemAppConstants.ContactsAppId, WellKnownAppDrives.ProfileDrive, DrivePermission.ReadWrite),
        new(SystemAppConstants.EmailAppId, WellKnownAppDrives.EmailAppDrive, DrivePermission.ReadWrite),
        new(SystemAppConstants.HomePageAppId, WellKnownAppDrives.HomePageConfigDrive, DrivePermission.ReadWrite),
        new(SystemAppConstants.LocationAppId, WellKnownAppDrives.LocationDrive, DrivePermission.ReadWrite),
        new(SystemAppConstants.RecoveryAppId, WellKnownAppDrives.ShardRecoveryDrive, DrivePermission.ReadWrite),
        new(SystemAppConstants.SystemAppId, SystemDriveConstants.TransientTempDrive, DrivePermission.ReadWrite)
    ];
}
