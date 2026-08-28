using System;
using System.Collections.Generic;
using System.Linq;
using Odin.Core;
using Odin.Services.Apps;
using Odin.Services.Drives.Management;

namespace Odin.Services.Drives;

/// <summary>
/// Built-in drives
/// </summary>
public static class SystemDriveConstants
{
    //
    // DO NOT CHANGE ANY VALUES
    //

    public static readonly Guid ChannelDriveType = Guid.Parse("8f448716-e34c-edf9-0141-45e043ca6612");
    
    
    
    public static readonly TargetDrive TransientTempDrive = new()
    {
        Alias = Guid.Parse("90f5e74ab7f9efda0ac298373a32ad8c"),
        Type = Guid.Parse("90f5e74ab7f9efda0ac298373a32ad8c"),
    };













    public static readonly List<TargetDrive> SystemDrives =
    [
        TransientTempDrive,
        WellKnownAppDrives.ContactDrive,
        WellKnownAppDrives.ProfileDrive,
        WellKnownAppDrives.WalletDrive,
        WellKnownAppDrives.ChatDrive,
        WellKnownAppDrives.FeedDrive,
        WellKnownAppDrives.HomePageConfigDrive,
        WellKnownAppDrives.MailDrive,
        WellKnownAppDrives.PublicPostsChannelDrive,
        WellKnownAppDrives.ShardRecoveryDrive,
        WellKnownAppDrives.MomentsDrive,
        WellKnownAppDrives.StickerDrive,
        WellKnownAppDrives.ListsDrive,
        WellKnownAppDrives.LocationDrive
    ];
    
    public static readonly CreateDriveRequest CreateTransientTempDriveRequest = new()
    {
        Name = "Transient temp drive",
        AllowAnonymousReads = false,
        Metadata = "",
        TargetDrive = TransientTempDrive,
        AppId = SystemAppConstants.SystemAppId,
        OwnerOnly = false
    };

    public static readonly CreateDriveRequest CreateFeedDriveRequest = new()
    {
        Name = "Feed",
        AllowAnonymousReads = false,
        Metadata = "",
        TargetDrive = WellKnownAppDrives.FeedDrive,
        AppId = SystemAppConstants.FeedAppId,
        OwnerOnly = true
    };

    public static readonly CreateDriveRequest CreateHomePageConfigDriveRequest = new()
    {
        Name = "Homepage Config",
        AllowAnonymousReads = true,
        Metadata = "",
        TargetDrive = WellKnownAppDrives.HomePageConfigDrive,
        AppId = SystemAppConstants.HomePageAppId,
        OwnerOnly = false
    };

    public static readonly CreateDriveRequest CreatePublicPostsChannelDriveRequest = new()
    {
        Name = "Public Posts",
        AllowAnonymousReads = true,
        Metadata = "",
        TargetDrive = WellKnownAppDrives.PublicPostsChannelDrive,
        AppId = SystemAppConstants.FeedAppId,
        OwnerOnly = false,
        AllowSubscriptions = true,
        // The only system drive seeded CDN-on. Public posts and their media are what the CDN
        // exists to serve, and having at least one enabled drive is what lets the CDN
        // authenticate at all - CdnAuthPathHandler fails outright when the set is empty, which
        // would take the CDN health ping down with it. Every other drive is opt-in.
        AllowCdn = true
    };

    public static readonly CreateDriveRequest CreateContactDriveRequest = new()
    {
        Name = "Contacts",
        AllowAnonymousReads = false,
        Metadata = "",
        TargetDrive = WellKnownAppDrives.ContactDrive,
        AppId = SystemAppConstants.ContactsAppId,
        OwnerOnly = true
    };

    public static readonly CreateDriveRequest CreateProfileDriveRequest = new()
    {
        Name = "Standard Profile Info",
        AllowAnonymousReads = true,
        Metadata = "",
        TargetDrive = WellKnownAppDrives.ProfileDrive,
        AppId = SystemAppConstants.ContactsAppId,
        OwnerOnly = false
    };

    public static readonly CreateDriveRequest CreateWalletDriveRequest = new()
    {
        Name = "Wallet",
        AllowAnonymousReads = false,
        Metadata = "",
        TargetDrive = WellKnownAppDrives.WalletDrive,
        AppId = SystemAppConstants.VaultAppId,
        OwnerOnly = true
    };

    public static readonly CreateDriveRequest CreateChatDriveRequest = new()
    {
        Name = "Chat Drive",
        AllowAnonymousReads = false,
        Metadata = "",
        TargetDrive = WellKnownAppDrives.ChatDrive,
        AppId = SystemAppConstants.ChatAppId,
        OwnerOnly = false //TODO: this needs to be set to true but is waiting on decision for how to auto-provision it.  I set it to false so it could be added to the system circle
    };
    
    public static readonly CreateDriveRequest CreateMomentsDriveRequest = new()
    {
        Name = "Moments Drive",
        AllowAnonymousReads = false,
        Metadata = "",
        TargetDrive = WellKnownAppDrives.MomentsDrive,
        AppId = SystemAppConstants.MomentsAppId,
        OwnerOnly = false
    };

    public static readonly CreateDriveRequest CreateStickerDriveRequest = new()
    {
        Name = "Sticker Drive",
        AllowAnonymousReads = false,
        Metadata = "",
        TargetDrive = WellKnownAppDrives.StickerDrive,
        AppId = SystemAppConstants.ChatAppId,
        OwnerOnly = false
    };

    public static readonly CreateDriveRequest CreateListsDriveRequest = new()
    {
        Name = "Lists Drive",
        AllowAnonymousReads = false,
        Metadata = "",
        TargetDrive = WellKnownAppDrives.ListsDrive,
        AppId = SystemAppConstants.ListsAppId,
        OwnerOnly = false,
        Attributes = new Dictionary<string, string>
        {
            { BuiltInDriveAttributes.IsCollaborativeChannel, bool.TrueString }
        }
    };

    public static readonly CreateDriveRequest CreateLocationDriveRequest = new()
    {
        Name = "Location Drive",
        AllowAnonymousReads = false,
        Metadata = "",
        TargetDrive = WellKnownAppDrives.LocationDrive,
        AppId = SystemAppConstants.LocationAppId,
        OwnerOnly = false
    };

    public static readonly CreateDriveRequest CreateShardRecoveryDriveRequest = new()
    {
        Name = "Shard Recovery Drive",
        AllowAnonymousReads = false,
        Metadata = "",
        TargetDrive = WellKnownAppDrives.ShardRecoveryDrive,
        AppId = SystemAppConstants.RecoveryAppId,
        OwnerOnly = false
    };

    
    public static readonly CreateDriveRequest CreateMailDriveRequest = new()
    {
        Name = "Mail Drive",
        AllowAnonymousReads = false,
        Metadata = "",
        TargetDrive = WellKnownAppDrives.MailDrive,
        AppId = SystemAppConstants.MailAppId,
        OwnerOnly = false //TODO: this needs to be set to true but is waiting on decision for how to auto-provision it.  I set it to false so it could be added to the system circle
    };
    
    public static bool IsSystemDrive(Guid driveId)
    {
        return SystemDrives.Any(d => d.Alias == driveId);
    }
}
