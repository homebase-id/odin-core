using System;
using System.Collections.Generic;
using Odin.Core;
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
        Alias = GuidId.FromString("transit_temp_drive"),
        Type = GuidId.FromString("transit_temp_drive")
    };

    public static readonly TargetDrive ContactDrive = new()
    {
        Alias = GuidId.FromString("built_in_contacts_drive"),
        Type = GuidId.FromString("contact_drive")
    };

    public static readonly TargetDrive ProfileDrive = new()
    {
        Alias = GuidId.FromString("standard_profile_drive"),
        Type = GuidId.FromString("profile_drive")
    };

    public static readonly TargetDrive WalletDrive = new()
    {
        Alias = GuidId.FromString("standard_wallet_drive"),
        Type = GuidId.FromString("profile_drive")
    };

    public static readonly TargetDrive ChatDrive = new()
    {
        Alias = GuidId.FromString("builtin_chat_drive"),
        Type = GuidId.FromString("chat_drive")
    };

    public static readonly TargetDrive MailDrive = new()
    {
        Alias = Guid.Parse("e69b5a48a663482fbfd846f3b0b143b0"),
        Type = Guid.Parse("2dfecc40311e41e5a12455e925144202")
    };

    public static readonly TargetDrive FeedDrive = new()
    {
        Alias = GuidId.FromString("builtin_feed_drive"),
        Type = GuidId.FromString("feed_drive")
    };

    public static readonly TargetDrive HomePageConfigDrive = new()
    {
        Alias = Guid.Parse("ec83345af6a747d4404ef8b0f8844caa"),
        Type = Guid.Parse("597241530e3ef24b28b9a75ec3a5c45c")
    };

    public static readonly TargetDrive PublicPostsChannelDrive = new()
    {
        Alias = Guid.Parse("e8475dc46cb4b6651c2d0dbd0f3aad5f"),
        Type = ChannelDriveType
    };

    public static readonly List<TargetDrive> SystemDrives =
    [
        TransientTempDrive,
        ContactDrive,
        ProfileDrive,
        WalletDrive,
        ChatDrive,
        FeedDrive,
        HomePageConfigDrive,
        MailDrive,
        PublicPostsChannelDrive
    ];

    public static readonly CreateDriveRequest CreateTransientTempDriveRequest = new()
    {
        Name = "Transient temp drive",
        AllowAnonymousReads = false,
        Metadata = "",
        TargetDrive = TransientTempDrive,
        OwnerOnly = false
    };

    public static readonly CreateDriveRequest CreateFeedDriveRequest = new()
    {
        Name = "Feed",
        AllowAnonymousReads = false,
        Metadata = "",
        TargetDrive = FeedDrive,
        OwnerOnly = true
    };

    public static readonly CreateDriveRequest CreateHomePageConfigDriveRequest = new()
    {
        Name = "Homepage Config",
        AllowAnonymousReads = true,
        Metadata = "",
        TargetDrive = HomePageConfigDrive,
        OwnerOnly = false
    };

    public static readonly CreateDriveRequest CreatePublicPostsChannelDriveRequest = new()
    {
        Name = "Public Posts",
        AllowAnonymousReads = true,
        Metadata = "",
        TargetDrive = PublicPostsChannelDrive,
        OwnerOnly = false,
        AllowSubscriptions = true
    };

    public static readonly CreateDriveRequest CreateContactDriveRequest = new()
    {
        Name = "Contacts",
        AllowAnonymousReads = false,
        Metadata = "",
        TargetDrive = ContactDrive,
        OwnerOnly = true
    };

    public static readonly CreateDriveRequest CreateProfileDriveRequest = new()
    {
        Name = "Standard Profile Info",
        AllowAnonymousReads = true,
        Metadata = "",
        TargetDrive = ProfileDrive,
        OwnerOnly = false
    };

    public static readonly CreateDriveRequest CreateWalletDriveRequest = new()
    {
        Name = "Wallet",
        AllowAnonymousReads = false,
        Metadata = "",
        TargetDrive = WalletDrive,
        OwnerOnly = true
    };

    public static readonly CreateDriveRequest CreateChatDriveRequest = new()
    {
        Name = "Chat Drive",
        AllowAnonymousReads = false,
        Metadata = "",
        TargetDrive = ChatDrive,
        OwnerOnly = false //TODO: this needs to be set to true but is waiting on decision for how to auto-provision it.  I set it to false so it could be added to the system circle
    };

    public static readonly CreateDriveRequest CreateMailDriveRequest = new()
    {
        Name = "Mail Drive",
        AllowAnonymousReads = false,
        Metadata = "",
        TargetDrive = MailDrive,
        OwnerOnly = false //TODO: this needs to be set to true but is waiting on decision for how to auto-provision it.  I set it to false so it could be added to the system circle
    };
}
